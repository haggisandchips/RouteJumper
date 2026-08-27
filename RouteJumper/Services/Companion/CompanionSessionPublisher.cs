using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using RouteJumper.Services.Logging;

namespace RouteJumper.Services.Companion
{
    /// <summary>
    /// Publishes Auto Pilot's key events (SPEC §13: route/jump plotted, arrived in system,
    /// refueled, panic-mode stop) to Firestore so the companion site (Angular, hosted under
    /// /app) can show a live, read-only feed of an in-progress run. Talks to the Firestore REST
    /// API directly via plain HttpClient - no Firebase SDK, and deliberately no auth: privacy is
    /// UUID-obscurity in the session id, not real authentication (see app/firestore.rules), so
    /// there is nothing to sign in as. This is the app's third outbound network integration,
    /// after EdsmStarSystemLookupService and SpanshRouteService (SPEC.md's Network section).
    ///
    /// Every publish call is best-effort and fire-and-forget: a session/event/status update that
    /// fails to reach Firestore is simply dropped and logged (category "Companion") - never
    /// retried, never surfaced to the CMDR, and never allowed to add latency to Auto Pilot or
    /// route tracking, the same "logging never on the hot path" philosophy §12 already applies
    /// elsewhere. StartSessionAsync is the one method that is genuinely awaited by its caller
    /// (MainViewModel) - the QR code can't be rendered before a session id exists - but even then,
    /// Auto Pilot itself is never blocked waiting on it (see MainViewModel's own wiring).
    ///
    /// A fresh session (and so a fresh QR code) is created every time Auto Pilot is engaged - see
    /// StartSessionAsync - never reused across runs.
    ///
    /// Housekeeping (SPEC §13) is self-managed, not Firestore's built-in TTL: TTL turned out to
    /// require the paid Blaze plan even for a single delete (unlike ordinary client-triggered
    /// deletes, which do have a free daily quota) - a poor fit for a "genuinely free, no card"
    /// hobby project. These records aren't kept for posterity - a completed run is of no further
    /// interest to anyone once its final state has actually been seen, so both retention windows
    /// below are deliberately short, not a generous archive:
    /// - <see cref="StartSessionAsync"/> immediately records the new session (in
    ///   <see cref="CompanionSessionStore"/>, a local SQLite table - the desktop app is the *only*
    ///   writer, so it's the only thing that can ever know which sessions exist) as due for
    ///   deletion after <see cref="AbsoluteMaxAgeHours"/> - a fixed, unconditional backstop that
    ///   applies no matter what, covering a session abandoned mid-run (app crash, force-quit,
    ///   EndSession never called) that would otherwise never be recorded as due at all.
    /// - <see cref="EndSession"/> then shortens that same record to
    ///   AppConfigStore.CompanionSessionRetentionHours (default far shorter than the fixed
    ///   backstop - just enough to be fairly confident the run's final state has actually been
    ///   seen on the companion site) once the run actually ends, clamped to never exceed
    ///   <see cref="AbsoluteMaxAgeHours"/> regardless of how that setting is configured.
    /// Every app launch, <see cref="CleanUpExpiredSessionsAsync"/> deletes whatever's actually due
    /// via plain Firestore REST DELETE calls (covered by the ordinary free delete quota) and only
    /// then forgets it locally.
    /// </summary>
    public sealed class CompanionSessionPublisher
    {
        private const string Category = "Companion";

        /// <summary>
        /// The fixed, unconditional maximum age any companion session (and its events) is ever
        /// kept - not configurable, and not something AppConfigStore.CompanionSessionRetentionHours
        /// can push past regardless of how it's set. Applied immediately at StartSessionAsync (so
        /// even a session that's abandoned mid-run and never reaches EndSession still eventually
        /// gets cleaned up), and used to clamp whatever EndSession later shortens it to.
        /// </summary>
        private const int AbsoluteMaxAgeHours = 72;

        // The single, shared Firebase project every installation of ED:FC Auto Pilot publishes
        // to - there is no per-user/per-install project, so every CMDR's companion sessions live
        // in the same Firestore database, isolated from each other only by their own unguessable
        // session id (see SPEC.md §13's privacy model). Only relevant to change if you're forking
        // this project to run your own, separate companion site instance against your own
        // Firebase project instead (see app/README.md) - matching app/src/environments/
        // environment*.ts's own projectId.
        private const string ProjectId = "haggisandchips-routejumper";

        private static readonly HttpClient SharedHttpClient = CreateHttpClient();

        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly Func<int> _getRetentionHours;
        private readonly CompanionSessionStore _sessionStore;

        public CompanionSessionPublisher(Func<int> getRetentionHours)
            : this(ProjectId, SharedHttpClient, getRetentionHours, new CompanionSessionStore())
        {
        }

        /// <summary>Test-only seam: lets RouteJumper.Tests inject a fake HttpMessageHandler/project id/session store instead of the real network and per-user AppData location - same precedent as EdsmStarSystemLookupService's own internal constructor.</summary>
        internal CompanionSessionPublisher(string projectId, HttpClient httpClient, Func<int> getRetentionHours, CompanionSessionStore sessionStore)
        {
            _httpClient = httpClient;
            _baseUrl = $"https://firestore.googleapis.com/v1/projects/{projectId}/databases/(default)/documents";
            _getRetentionHours = getRetentionHours;
            _sessionStore = sessionStore;
        }

        /// <summary>The session Auto Pilot is currently publishing to, if any - null before the first StartSessionAsync call and after EndSession.</summary>
        public Guid? CurrentSessionId { get; private set; }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient(new LoggingHttpMessageHandler(new HttpClientHandler(), Category)) { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("EDFCAutoPilot/1.0 (+https://github.com/haggisandchips/RouteJumper)");
            return client;
        }

        /// <summary>
        /// Creates a fresh session header doc (sessions/{uuid}) for a newly-engaged Auto Pilot
        /// run. Returns the new session id, or null if the request failed - a failure here just
        /// means no QR/link is shown for this run, logged rather than thrown.
        /// </summary>
        public async Task<Guid?> StartSessionAsync(string startSystem, string endSystem, CancellationToken cancellationToken = default)
        {
            var sessionId = Guid.NewGuid();
            try
            {
                var body = new JsonObject
                {
                    ["fields"] = new JsonObject
                    {
                        ["startSystem"] = StringField(startSystem),
                        ["endSystem"] = StringField(endSystem),
                        ["createdUtc"] = TimestampField(DateTime.UtcNow),
                        ["status"] = StringField("active"),
                    }
                };

                var url = $"{_baseUrl}/sessions?documentId={sessionId}";
                using var content = JsonContent.Create(body);
                using var response = await _httpClient.PostAsync(url, content, cancellationToken);
                response.EnsureSuccessStatusCode();

                CurrentSessionId = sessionId;

                // Recorded immediately, not just on EndSession - see this class's own doc comment
                // on why an abandoned session (never reaching EndSession) still needs a backstop.
                _sessionStore.RecordPendingDeletion(sessionId, DateTime.UtcNow.AddHours(AbsoluteMaxAgeHours));

                Log.Info(Category, $"Companion session {sessionId} started ({startSystem} -> {endSystem}).");
                return sessionId;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warn(Category, "Failed to start companion session.", ex);
                return null;
            }
        }

        /// <summary>Fire-and-forget - never throws, never awaited by the caller. No-op if no session is currently open. A dropped publish just stays dropped, the same best-effort philosophy as EDSM's own lookups (SPEC §4.9).</summary>
        public void PublishEvent(CompanionEventKind kind, string systemName, string message)
        {
            if (CurrentSessionId is not { } sessionId)
            {
                return;
            }

            _ = PublishEventInternalAsync(sessionId, kind, systemName, message);
        }

        private async Task PublishEventInternalAsync(Guid sessionId, CompanionEventKind kind, string systemName, string message)
        {
            try
            {
                var body = new JsonObject
                {
                    ["fields"] = new JsonObject
                    {
                        ["kind"] = StringField(WireKind(kind)),
                        ["systemName"] = StringField(systemName),
                        ["message"] = StringField(message),
                        ["clientUtc"] = TimestampField(DateTime.UtcNow),
                    }
                };

                var url = $"{_baseUrl}/sessions/{sessionId}/events";
                using var content = JsonContent.Create(body);
                using var response = await _httpClient.PostAsync(url, content);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                Log.Warn(Category, $"Failed to publish companion event ({kind}).", ex);
            }
        }

        /// <summary>
        /// Fire-and-forget, same contract as PublishEvent. Marks the current session
        /// completed/panicked, shortens its local deletion deadline from the fixed
        /// AbsoluteMaxAgeHours backstop down to AppConfigStore.CompanionSessionRetentionHours
        /// (clamped to never exceed AbsoluteMaxAgeHours regardless - see this class's own doc
        /// comment), and clears CurrentSessionId so a later Auto Pilot re-engage starts a
        /// genuinely fresh session. No-op if no session is currently open.
        /// </summary>
        public void EndSession(bool panicked)
        {
            if (CurrentSessionId is not { } sessionId)
            {
                return;
            }

            CurrentSessionId = null;
            _ = EndSessionInternalAsync(sessionId, panicked);
        }

        private async Task EndSessionInternalAsync(Guid sessionId, bool panicked)
        {
            try
            {
                var status = panicked ? "panicked" : "completed";
                var body = new JsonObject
                {
                    ["fields"] = new JsonObject
                    {
                        ["status"] = StringField(status),
                    }
                };

                var url = $"{_baseUrl}/sessions/{sessionId}?updateMask.fieldPaths=status";
                using var content = JsonContent.Create(body);
                using var request = new HttpRequestMessage(HttpMethod.Patch, url) { Content = content };
                using var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var retentionHours = Math.Clamp(_getRetentionHours(), 1, AbsoluteMaxAgeHours);
                _sessionStore.RecordPendingDeletion(sessionId, DateTime.UtcNow.AddHours(retentionHours));

                Log.Info(Category, $"Companion session {sessionId} ended ({status}) - will be deleted after {retentionHours}h.");
            }
            catch (Exception ex)
            {
                Log.Warn(Category, "Failed to end companion session.", ex);
            }
        }

        /// <summary>
        /// Deletes every companion session recorded locally as due (CompanionSessionStore) -
        /// intended to be called once per app launch (fire-and-forget, best-effort, never blocks
        /// startup). A session is removed from local tracking only once its Firestore documents
        /// are confirmed gone, so a failed attempt (offline, Firestore unreachable, ...) simply
        /// retries in full on the next launch rather than silently losing track of it.
        /// </summary>
        public async Task CleanUpExpiredSessionsAsync()
        {
            IReadOnlyList<Guid> due;
            try
            {
                due = _sessionStore.GetSessionsDueForDeletion(DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                Log.Warn(Category, "Failed to read companion sessions due for deletion.", ex);
                return;
            }

            foreach (var sessionId in due)
            {
                if (await DeleteSessionAsync(sessionId))
                {
                    _sessionStore.Remove(sessionId);
                }
            }
        }

        /// <summary>
        /// Deletes every one of a session's events first, then its own header doc - deleting the
        /// header does not cascade to its `events` subcollection (a genuine Firestore behaviour,
        /// not an oversight). Only reports success once every one of those deletes (or an already-
        /// gone 404, treated as success) actually happened - a partial failure leaves the session
        /// tracked so the whole thing (list, whatever events remain, then the header) is retried
        /// next launch rather than orphaning leftover events with nothing left to find them by.
        /// </summary>
        private async Task<bool> DeleteSessionAsync(Guid sessionId)
        {
            try
            {
                var listUrl = $"{_baseUrl}/sessions/{sessionId}/events?pageSize=1000";
                using var listResponse = await _httpClient.GetAsync(listUrl);
                if (!listResponse.IsSuccessStatusCode)
                {
                    return false;
                }

                var listBody = await listResponse.Content.ReadAsStringAsync();
                var eventNames = JsonNode.Parse(listBody)?["documents"] is JsonArray documents
                    ? documents.Select(doc => doc?["name"]?.GetValue<string>()).Where(name => !string.IsNullOrEmpty(name)).ToList()
                    : new List<string?>();

                var eventDeleteResults = await Task.WhenAll(
                    eventNames.Select(name => DeleteDocumentAsync($"https://firestore.googleapis.com/v1/{name}")));
                if (eventDeleteResults.Any(succeeded => !succeeded))
                {
                    return false;
                }

                if (!await DeleteDocumentAsync($"{_baseUrl}/sessions/{sessionId}"))
                {
                    return false;
                }

                Log.Info(Category, $"Deleted expired companion session {sessionId} ({eventNames.Count} event(s)).");
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(Category, $"Failed to delete expired companion session {sessionId}.", ex);
                return false;
            }
        }

        /// <summary>A 404 counts as success - the document is already gone, nothing left to retry.</summary>
        private async Task<bool> DeleteDocumentAsync(string url)
        {
            using var response = await _httpClient.DeleteAsync(url);
            return response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound;
        }

        /// <summary>Matches the Angular app's SessionEventKind union (app/src/app/core/models/session-event.model.ts) exactly.</summary>
        private static string WireKind(CompanionEventKind kind) => kind switch
        {
            CompanionEventKind.Plotted => "plotted",
            CompanionEventKind.Arrived => "arrived",
            CompanionEventKind.Refueled => "refueled",
            CompanionEventKind.Panic => "panic",
            _ => kind.ToString().ToLowerInvariant()
        };

        private static JsonObject StringField(string value) => new() { ["stringValue"] = value ?? string.Empty };

        /// <summary>Firestore REST's timestampValue wants RFC3339 UTC ("Z" offset, millisecond precision is plenty here).</summary>
        private static JsonObject TimestampField(DateTime utcValue) => new() { ["timestampValue"] = utcValue.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") };
    }
}
