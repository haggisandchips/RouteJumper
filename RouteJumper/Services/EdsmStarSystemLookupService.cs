using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using RouteJumper.Models;
using RouteJumper.Services.Logging;

namespace RouteJumper.Services
{
    /// <summary>
    /// Looks up star system coordinates (for the Route table's Distance column) and each
    /// system's main star's type (for its Star Type column) against EDSM
    /// (https://www.edsm.net) - a free, no-API-key, community-standard database, chosen after
    /// evaluating Spansh (route-plotting/id64-oriented, not a good fit for simple bulk
    /// name-&gt;coordinate lookups), Inara (stricter API terms, trade/commander-data oriented),
    /// EDDB (defunct since 2021), EDAstro (itself sourced from EDSM), and Ardent Insight
    /// (EDDN market data, not general system metadata).
    ///
    /// This is the app's first-ever outbound network call to a third-party service (aside from
    /// Velopack's own internal update-check HTTP calls) - see SPEC.md's NFR section. Every call
    /// sends only a system name, never any commander/journal data.
    ///
    /// A single bulk endpoint does the real work: `api-v1/systems` (plural) accepts multiple
    /// names in one request via repeated `systemName[]=` params (confirmed against the real API -
    /// GET, not POST, and array-style repeated params, not a JSON body) and, with both
    /// `showCoordinates=1` *and* `showPrimaryStar=1` set, returns each system's coordinates and
    /// its primary star's type in the very same response - there never was a need for a second,
    /// per-system endpoint (`api-system-v1/bodies`, throttled to ~700 lookups/minute) just to get
    /// the star type, so that whole separate call/throttle path has been retired: every coordinate
    /// lookup opportunistically resolves star type too (and vice versa, for the rarer case a
    /// system's coordinates are already cached but its star type still isn't), for free, in the
    /// same chunked request. EDSM's exact per-request batch-size cap isn't publicly documented, so
    /// requests are defensively chunked at <see cref="AppConfigStore.EdsmCoordinatesBatchSize"/>
    /// systems per request (hand-editable in routejumper.conf, default 100) the same as before.
    ///
    /// A resolved value is visible immediately via an in-memory cache (<see cref="_coordsMemoryCache"/>/
    /// <see cref="_starTypeMemoryCache"/>) - every read checks memory first, falling back to
    /// <see cref="EdsmResolvedLookupStore"/> only on a miss (and back-filling memory from that DB read for
    /// next time). But the two ways a value gets resolved persist very differently to disk, to keep
    /// <see cref="EdsmResolvedLookupStore"/> from growing without bound as a commander visits many
    /// thousands of systems over time:
    ///
    /// - A value EDSM itself resolves (<see cref="FetchSystemInfoChunkAsync"/>) is cached in memory
    ///   only, for the running session - never written to <see cref="EdsmResolvedLookupStore"/>.
    ///   EDSM's own lookup is now a single batched request per ~100 systems, so simply re-asking it
    ///   again next launch is cheap; there's no need to keep a system EDSM can answer forever.
    /// - A journal/Spansh-seeded value (<see cref="SeedCoordinates"/>/<see cref="SeedStarType"/>) is
    ///   persisted only if EDSM had *already* confirmed, at some point, that it has no record of
    ///   that exact system (<see cref="IsConfirmedUnresolved"/>) - i.e. the seed is filling a
    ///   genuine gap EDSM can never fill on its own (almost always a procedurally-generated system
    ///   name). Otherwise the seed still updates the in-memory cache immediately (so the row
    ///   displays without waiting on a network round trip, SPEC §4.9), but isn't written to disk -
    ///   a system Import Current Route/Trim for FC/Spansh hands over for free, that EDSM has never
    ///   even been asked about, is simply re-derived from its own source (or from EDSM) again next
    ///   session rather than persisted the first time it's ever seen.
    /// - System address (id64, <see cref="SeedSystemAddress"/>) is never persisted at all right now
    ///   - nothing in the UI reads it yet (see <see cref="SpanshRouteJump"/>'s own doc comment), so
    ///   there's no benefit to writing it to disk, only unbounded growth.
    ///
    /// Star type is cached as a canonical StarClass code, not pre-formatted display text (see
    /// StarClassNames), so a caller always sees the current mapping table's own output rather than
    /// whatever text happened to be cached the first time.
    ///
    /// An unresolved name is a different story: EDSM confirming it simply has no record for a
    /// system is itself remembered - see <see cref="EdsmLookupAttemptStore"/> and
    /// <see cref="AppConfigStore.EdsmUnresolvedRetryHours"/> - both in-memory for the rest of this
    /// running session (<see cref="_unresolvedCoordsThisSession"/>/<see cref="_unresolvedStarTypeThisSession"/>)
    /// and on disk, so the same system isn't queried again for a configurable cooldown (default 12
    /// hours), even across an app restart - and so a later seed can recognise it as a genuine,
    /// worth-persisting gap even if that cooldown has since lapsed (see
    /// <see cref="IsConfirmedUnresolved"/>, which checks for the record's existence, not its
    /// recency). EDSM's crowdsourced coverage is very unlikely to change meaningfully within that
    /// window, so repeating the same request is close to pure waste. This only ever applies to a
    /// lookup EDSM genuinely answered ("no record") - a transient failure (network down,
    /// non-success status, malformed response) is never remembered this way, and is simply retried
    /// on the very next attempt, the same as before.
    ///
    /// Writing to disk is deliberately decoupled from both the memory update and
    /// <see cref="DataSeeded"/> - see <see cref="EnqueuePersist"/> - so a burst of seeds (e.g. every
    /// system in a freshly-read NavRoute.json, SPEC §4.9) updates the UI essentially instantly,
    /// with the (comparatively slow, one-open-close-per-write) SQLite persistence trailing behind
    /// on its own background queue rather than delaying the notification that drives it.
    /// </summary>
    public class EdsmStarSystemLookupService : IStarSystemLookupService
    {
        private const string BaseUrl = "https://www.edsm.net";

        /// <summary>"Kind" discriminators shared by EdsmResolvedLookupStore (resolved values) and EdsmLookupAttemptStore (unresolved-attempt cooldown). Internal (not private) so tests can drive either store directly with the real values rather than a hardcoded duplicate.</summary>
        internal const string CoordsKind = "Coords";
        internal const string StarTypeKind = "StarType";
        internal const string SystemAddressKind = "SystemAddress";

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        private static readonly HttpClient SharedHttpClient = CreateHttpClient();

        // Lazy: a caller that only ever needs the no-arg convenience constructor (several
        // ViewModels' own "?? new EdsmStarSystemLookupService()" fallback, only actually
        // reachable in tests that don't care about EDSM at all) must never eagerly construct
        // AppConfigStore/EdsmLookupAttemptStore/EdsmResolvedLookupStore - all default to
        // AppPaths.DataDirectory, whose static initializer requires a real Velopack app host
        // (VelopackApp.Build().Run(), App.xaml.cs) and throws outside one, which a plain unit
        // test never runs. Deferring construction until a lookup is actually attempted (the only
        // place these are read) means that fallback path never touches any of them.
        private readonly Lazy<AppConfigStore> _config;
        private readonly Lazy<EdsmLookupAttemptStore> _attemptStore;
        private readonly Lazy<EdsmResolvedLookupStore> _resolvedLookups;
        private readonly HttpClient _httpClient;

        private readonly ConcurrentDictionary<string, GalacticCoordinates> _coordsMemoryCache = new();
        private readonly ConcurrentDictionary<string, string> _starTypeMemoryCache = new();
        private readonly ConcurrentDictionary<string, long> _systemAddressMemoryCache = new();

        /// <summary>Systems EDSM has already confirmed (this running session) it has no coordinates for - never re-queried again until the app restarts, on top of the persisted cooldown (EdsmLookupAttemptStore) which survives a restart too.</summary>
        private readonly ConcurrentDictionary<string, byte> _unresolvedCoordsThisSession = new();

        /// <summary>Same as <see cref="_unresolvedCoordsThisSession"/>, for star type.</summary>
        private readonly ConcurrentDictionary<string, byte> _unresolvedStarTypeThisSession = new();

        /// <summary>Serializes background DB writes (see EnqueuePersist) into a single chain, so concurrent SQLite connections are never opened from multiple writes racing each other.</summary>
        private readonly object _persistChainLock = new();
        private Task _persistChain = Task.CompletedTask;

        public EdsmStarSystemLookupService()
            : this(new Lazy<AppConfigStore>(() => new AppConfigStore()), new Lazy<EdsmLookupAttemptStore>(() => new EdsmLookupAttemptStore()), new Lazy<EdsmResolvedLookupStore>(() => new EdsmResolvedLookupStore()), SharedHttpClient)
        {
        }

        public EdsmStarSystemLookupService(AppConfigStore config)
            : this(new Lazy<AppConfigStore>(() => config), new Lazy<EdsmLookupAttemptStore>(() => new EdsmLookupAttemptStore()), new Lazy<EdsmResolvedLookupStore>(() => new EdsmResolvedLookupStore()), SharedHttpClient)
        {
        }

        /// <summary>Test-only seam: lets RouteJumper.Tests inject a fake HttpMessageHandler (and directory-scoped config/attempt/resolved-lookup stores) instead of making real network calls or touching the real per-user AppData location.</summary>
        internal EdsmStarSystemLookupService(AppConfigStore config, EdsmLookupAttemptStore attemptStore, EdsmResolvedLookupStore resolvedLookups, HttpClient httpClient)
            : this(new Lazy<AppConfigStore>(() => config), new Lazy<EdsmLookupAttemptStore>(() => attemptStore), new Lazy<EdsmResolvedLookupStore>(() => resolvedLookups), httpClient)
        {
        }

        private EdsmStarSystemLookupService(
            Lazy<AppConfigStore> config, Lazy<EdsmLookupAttemptStore> attemptStore, Lazy<EdsmResolvedLookupStore> resolvedLookups, HttpClient httpClient)
        {
            _config = config;
            _attemptStore = attemptStore;
            _resolvedLookups = resolvedLookups;
            _httpClient = httpClient;
        }

        private static HttpClient CreateHttpClient()
        {
            // LoggingHttpMessageHandler wraps the real transport so every EDSM request/response
            // (or failure) is logged (SPEC's Logging section) - see that class's own doc comment
            // for why this is the app's only direct-HttpClient logging seam.
            var client = new HttpClient(new LoggingHttpMessageHandler(new HttpClientHandler())) { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("EDFCAutoPilot/1.0 (+https://github.com/haggisandchips/RouteJumper)");
            return client;
        }

        public async Task<IReadOnlyDictionary<string, GalacticCoordinates?>> GetCoordinatesAsync(
            IReadOnlyList<string> systemNames, CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<string, GalacticCoordinates?>(StringComparer.OrdinalIgnoreCase);
            var toFetch = new List<string>();

            foreach (var name in systemNames)
            {
                if (result.ContainsKey(name))
                {
                    continue; // duplicate in the caller's own list
                }

                if (TryGetCachedCoordinates(name, out var cached))
                {
                    result[name] = cached;
                }
                else if (IsUnresolvedRecently(CoordsKind, name))
                {
                    result[name] = null;
                }
                else
                {
                    result[name] = null; // placeholder - overwritten below if this chunk resolves it
                    toFetch.Add(name);
                }
            }

            var batchSize = Math.Max(1, _config.Value.EdsmCoordinatesBatchSize);
            for (var i = 0; i < toFetch.Count; i += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunk = toFetch.Skip(i).Take(batchSize).ToList();
                await FetchSystemInfoChunkAsync(chunk, result, cancellationToken);
            }

            return result;
        }

        public async Task<string?> GetMainStarTypeAsync(string systemName, CancellationToken cancellationToken = default)
        {
            var result = await GetStarTypesAsync(new[] { systemName }, cancellationToken);
            return result[systemName];
        }

        /// <summary>
        /// Bulk counterpart to <see cref="GetMainStarTypeAsync"/> - resolves star type for every
        /// name in <paramref name="systemNames"/> in as few chunked requests as possible
        /// (<see cref="AppConfigStore.EdsmCoordinatesBatchSize"/> names per request), the same
        /// batching GetCoordinatesAsync already gets. This matters because a name whose
        /// coordinates are already cached (e.g. resolved in an earlier session, before Star Type
        /// existed, or simply already known) never goes through GetCoordinatesAsync's own network
        /// fetch at all, so that call alone can't piggyback star-type resolution for it -
        /// RouteRowEnrichmentService calls this afterward specifically to batch-resolve exactly
        /// that remaining set, rather than each such row falling back to its own single-name
        /// request (see GetMainStarTypeAsync's old per-row-in-a-loop caller, and the class doc
        /// comment's "resolved together in one request" note).
        /// </summary>
        public async Task<IReadOnlyDictionary<string, string?>> GetStarTypesAsync(
            IReadOnlyList<string> systemNames, CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var toFetch = new List<string>();

            foreach (var name in systemNames)
            {
                if (result.ContainsKey(name))
                {
                    continue; // duplicate in the caller's own list
                }

                if (TryGetCachedStarType(name, out var cached))
                {
                    result[name] = cached;
                }
                else if (IsUnresolvedRecently(StarTypeKind, name))
                {
                    result[name] = null;
                }
                else
                {
                    result[name] = null; // placeholder - overwritten below if this chunk resolves it
                    toFetch.Add(name);
                }
            }

            var batchSize = Math.Max(1, _config.Value.EdsmCoordinatesBatchSize);
            for (var i = 0; i < toFetch.Count; i += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunk = toFetch.Skip(i).Take(batchSize).ToList();

                // The throwaway coords dictionary is discarded - only the star-type cache
                // FetchSystemInfoChunkAsync populates as a side effect is what this cares about
                // (though any coordinates this incidentally resolves are cached too, for free).
                var throwawayCoordsResult = new Dictionary<string, GalacticCoordinates?>(StringComparer.OrdinalIgnoreCase);
                await FetchSystemInfoChunkAsync(chunk, throwawayCoordsResult, cancellationToken);

                foreach (var name in chunk)
                {
                    result[name] = TryGetCachedStarType(name, out var resolved) ? resolved : null;
                }
            }

            return result;
        }

        /// <summary>
        /// The one HTTP call this whole class makes: `api-v1/systems?...&amp;showCoordinates=1&amp;showPrimaryStar=1`,
        /// resolving both coordinates and star type for every name in <paramref name="names"/> in
        /// a single round trip. Coordinates resolved are written into <paramref name="result"/>
        /// (and cached); star type is only ever cached as a side effect, never returned here -
        /// GetMainStarTypeAsync reads it back out of the cache afterward. Any name from this chunk
        /// still unresolved once a genuinely successful, parsed response has been processed is
        /// EDSM confirming it has no record - see MarkUnresolved - which is different from (and
        /// never triggered by) a transient failure below, always retried next time regardless.
        /// </summary>
        private async Task FetchSystemInfoChunkAsync(
            IReadOnlyList<string> names, Dictionary<string, GalacticCoordinates?> result, CancellationToken cancellationToken)
        {
            try
            {
                var query = string.Join("&", names.Select(n => $"systemName[]={Uri.EscapeDataString(n)}"));
                var url = $"{BaseUrl}/api-v1/systems?{query}&showCoordinates=1&showPrimaryStar=1";

                using var response = await _httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var entries = await JsonSerializer.DeserializeAsync<List<SystemInfoResponse>>(stream, JsonOptions, cancellationToken);
                if (entries is null)
                {
                    return;
                }

                foreach (var entry in entries)
                {
                    if (entry.Name is not { } name)
                    {
                        continue;
                    }

                    if (entry.Coords is { } coordsDto)
                    {
                        var coordinates = new GalacticCoordinates(coordsDto.X, coordsDto.Y, coordsDto.Z);
                        result[name] = coordinates;
                        CacheCoordinatesFromEdsm(name, coordinates);
                    }

                    if (entry.PrimaryStar?.Type is { } rawSubType)
                    {
                        // EDSM never returns a raw journal StarClass code, only this formatted
                        // display text - recovered back into the same canonical code the journal
                        // path caches (see StarClassNames.TryGetCode) wherever a mapping is known,
                        // so both sources converge on identical display text at read time
                        // regardless of which one resolved a given system. Falls back to caching
                        // the stripped display text itself when unrecognized - the same
                        // don't-guess degrade StarClassNames.ToDisplayName already applies to an
                        // unrecognized journal code.
                        var stripped = StripRedundantStarWord(rawSubType);
                        CacheStarTypeFromEdsm(name, StarClassNames.TryGetCode(stripped, out var code) ? code : stripped);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Best-effort - this chunk's names simply stay unresolved (already defaulted to
                // null above): a network failure, non-JSON response, etc. must never propagate
                // out and block/fail the Route tab's own Save. Never marked unresolved (below) -
                // a transient failure carries no information about whether EDSM actually has the
                // data, so it's retried on the very next attempt rather than remembered.
                return;
            }

            foreach (var name in names)
            {
                if (result.TryGetValue(name, out var coords) && coords is null)
                {
                    MarkUnresolved(CoordsKind, name);
                }

                if (!TryGetCachedStarType(name, out _))
                {
                    MarkUnresolved(StarTypeKind, name);
                }
            }
        }

        public event EventHandler? DataSeeded;

        public void SeedCoordinates(string systemName, GalacticCoordinates coordinates)
        {
            CacheCoordinatesFromSeed(systemName, coordinates);
            DataSeeded?.Invoke(this, EventArgs.Empty);
        }

        public void SeedStarType(string systemName, string starClassCode)
        {
            CacheStarTypeFromSeed(systemName, starClassCode);
            DataSeeded?.Invoke(this, EventArgs.Empty);
        }

        public void SeedSystemAddress(string systemName, long systemAddress)
        {
            CacheSystemAddress(systemName, systemAddress);
            DataSeeded?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// True if <paramref name="systemName"/> was already confirmed unresolved for
        /// <paramref name="kind"/> recently enough that it shouldn't be queried again - checked
        /// in-memory first (this session), then, on a miss, against the persisted cooldown
        /// (EdsmLookupAttemptStore/AppConfigStore.EdsmUnresolvedRetryHours), which - if still
        /// within the window - is also promoted into the in-memory set so every later check this
        /// session is a pure memory lookup, never a repeat DB read.
        /// </summary>
        private bool IsUnresolvedRecently(string kind, string systemName)
        {
            var key = NormalizeKey(systemName);
            var sessionSet = SessionSetFor(kind);
            if (sessionSet.ContainsKey(key))
            {
                return true;
            }

            if (_attemptStore.Value.GetLastAttemptUtc(kind, key) is { } lastAttemptUtc)
            {
                var retryAfter = TimeSpan.FromHours(Math.Max(0, _config.Value.EdsmUnresolvedRetryHours));
                if (DateTime.UtcNow - lastAttemptUtc < retryAfter)
                {
                    sessionSet.TryAdd(key, 0);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True if EDSM has, at some point, confirmed it has no record of <paramref name="key"/>
        /// for <paramref name="kind"/> - unlike <see cref="IsUnresolvedRecently"/>, this checks for
        /// the record's mere *existence*, not whether it's still within the retry-cooldown window,
        /// since a gap confirmed weeks ago is still a real gap worth a seed persisting to fill -
        /// used by <see cref="CacheCoordinatesFromSeed"/>/<see cref="CacheStarTypeFromSeed"/> to
        /// decide whether a seed is filling a genuine, otherwise-unrecoverable EDSM gap or merely
        /// duplicating a system EDSM could resolve again for free next session anyway. Expects an
        /// already-normalized <paramref name="key"/> (see <see cref="NormalizeKey"/>).
        /// </summary>
        private bool IsConfirmedUnresolved(string kind, string key) =>
            SessionSetFor(kind).ContainsKey(key) || _attemptStore.Value.GetLastAttemptUtc(kind, key) is not null;

        /// <summary>Records that EDSM was just asked about <paramref name="systemName"/> for <paramref name="kind"/> and confirmed it has no record - in-memory for the rest of this session, and persisted (background, non-blocking) so the cooldown survives a restart too.</summary>
        private void MarkUnresolved(string kind, string systemName)
        {
            var key = NormalizeKey(systemName);
            SessionSetFor(kind).TryAdd(key, 0);

            var attemptedAtUtc = DateTime.UtcNow;
            EnqueuePersist(() => _attemptStore.Value.SetLastAttemptUtc(kind, key, attemptedAtUtc));
        }

        private ConcurrentDictionary<string, byte> SessionSetFor(string kind) =>
            kind == CoordsKind ? _unresolvedCoordsThisSession : _unresolvedStarTypeThisSession;

        public bool TryGetCachedCoordinates(string systemName, out GalacticCoordinates? coordinates)
        {
            var key = NormalizeKey(systemName);
            if (_coordsMemoryCache.TryGetValue(key, out var memoized))
            {
                coordinates = memoized;
                return true;
            }

            var raw = _resolvedLookups.Value.GetValue(CoordsKind, key);
            var parts = raw?.Split('|');
            if (parts is { Length: 3 }
                && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
            {
                var resolved = new GalacticCoordinates(x, y, z);
                _coordsMemoryCache[key] = resolved; // back-fill memory so the next read skips the DB
                coordinates = resolved;
                return true;
            }

            coordinates = null;
            return false;
        }

        public bool TryGetCachedStarType(string systemName, out string? starType)
        {
            var key = NormalizeKey(systemName);
            if (_starTypeMemoryCache.TryGetValue(key, out var memoized))
            {
                starType = StarClassNames.ToDisplayName(memoized);
                return true;
            }

            var raw = _resolvedLookups.Value.GetValue(StarTypeKind, key);
            if (raw != null)
            {
                _starTypeMemoryCache[key] = raw; // back-fill memory (the canonical code/fallback text, not the display string) so the next read skips the DB
                starType = StarClassNames.ToDisplayName(raw);
                return true;
            }

            starType = null;
            return false;
        }

        public bool TryGetCachedSystemAddress(string systemName, out long? systemAddress)
        {
            var key = NormalizeKey(systemName);
            if (_systemAddressMemoryCache.TryGetValue(key, out var memoized))
            {
                systemAddress = memoized;
                return true;
            }

            var raw = _resolvedLookups.Value.GetValue(SystemAddressKind, key);
            if (raw != null && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                _systemAddressMemoryCache[key] = parsed; // back-fill memory so the next read skips the DB
                systemAddress = parsed;
                return true;
            }

            systemAddress = null;
            return false;
        }

        /// <summary>
        /// Updates the in-memory cache immediately (so a caller's very next read, or this same
        /// resolved value flowing straight back out of the async lookup that just produced it, is
        /// already consistent) for a value EDSM itself just resolved. Never persisted to disk - see
        /// the class doc comment - but still clears any recorded unresolved-attempt for this system
        /// (in-memory and persisted), since a value that just resolved must never still be treated
        /// as "recently confirmed unavailable" by a later lookup.
        /// </summary>
        private void CacheCoordinatesFromEdsm(string systemName, GalacticCoordinates coordinates)
        {
            var key = NormalizeKey(systemName);
            _coordsMemoryCache[key] = coordinates;
            _unresolvedCoordsThisSession.TryRemove(key, out _);

            EnqueuePersist(() => _attemptStore.Value.Clear(CoordsKind, key));
        }

        /// <summary>See CacheCoordinatesFromEdsm's own doc comment - identical shape for the star-type cache. <paramref name="starClassCode"/> is the canonical value cached and later rendered via StarClassNames.ToDisplayName on read (TryGetCachedStarType) - the code recovered from EDSM's own display text (see FetchSystemInfoChunkAsync/StarClassNames.TryGetCode).</summary>
        private void CacheStarTypeFromEdsm(string systemName, string starClassCode)
        {
            var key = NormalizeKey(systemName);
            _starTypeMemoryCache[key] = starClassCode;
            _unresolvedStarTypeThisSession.TryRemove(key, out _);

            EnqueuePersist(() => _attemptStore.Value.Clear(StarTypeKind, key));
        }

        /// <summary>
        /// Updates the in-memory cache immediately for a journal/Spansh-seeded value, and persists
        /// it to disk only if EDSM had already confirmed, at some point, that it has no record of
        /// this exact system (<see cref="IsConfirmedUnresolved"/>, checked against the pre-seed
        /// state before anything below mutates it) - a seed that isn't filling a confirmed EDSM gap
        /// simply isn't written to disk, since EDSM can resolve it again for free next session. The
        /// unresolved-attempt record is always cleared regardless, persisted or not - a seed always
        /// supersedes stale "confirmed unavailable" state.
        /// </summary>
        private void CacheCoordinatesFromSeed(string systemName, GalacticCoordinates coordinates)
        {
            var key = NormalizeKey(systemName);
            var fillsConfirmedGap = IsConfirmedUnresolved(CoordsKind, key);
            _coordsMemoryCache[key] = coordinates;
            _unresolvedCoordsThisSession.TryRemove(key, out _);

            EnqueuePersist(() =>
            {
                if (fillsConfirmedGap)
                {
                    var raw = string.Join("|",
                        coordinates.X.ToString(CultureInfo.InvariantCulture),
                        coordinates.Y.ToString(CultureInfo.InvariantCulture),
                        coordinates.Z.ToString(CultureInfo.InvariantCulture));
                    _resolvedLookups.Value.SetValue(CoordsKind, key, raw);
                }

                _attemptStore.Value.Clear(CoordsKind, key);
            });
        }

        /// <summary>See CacheCoordinatesFromSeed's own doc comment - identical shape for the star-type cache. <paramref name="starClassCode"/> is the raw journal StarClass code (FSDTarget/NavRoute.json), cached as-is (see CacheCoordinatesFromEdsm's own note on canonical codes vs. display text).</summary>
        private void CacheStarTypeFromSeed(string systemName, string starClassCode)
        {
            var key = NormalizeKey(systemName);
            var fillsConfirmedGap = IsConfirmedUnresolved(StarTypeKind, key);
            _starTypeMemoryCache[key] = starClassCode;
            _unresolvedStarTypeThisSession.TryRemove(key, out _);

            EnqueuePersist(() =>
            {
                if (fillsConfirmedGap)
                {
                    _resolvedLookups.Value.SetValue(StarTypeKind, key, starClassCode);
                }

                _attemptStore.Value.Clear(StarTypeKind, key);
            });
        }

        /// <summary>
        /// Memory-cache only, deliberately never persisted - nothing in the UI reads a system's
        /// address yet (see SpanshRouteJump's own doc comment), so writing it to disk would only be
        /// unbounded growth for zero current benefit. Revisit once a feature that actually needs it
        /// exists. (Old rows persisted by a previous version are left alone and still readable via
        /// TryGetCachedSystemAddress's own DB fallback - just never written again.)
        /// </summary>
        private void CacheSystemAddress(string systemName, long systemAddress)
        {
            var key = NormalizeKey(systemName);
            _systemAddressMemoryCache[key] = systemAddress;
        }

        /// <summary>
        /// Appends <paramref name="write"/> to a single serial chain of background DB writes -
        /// never awaited or run inline, so a caller (in particular SeedCoordinates/SeedStarType,
        /// called synchronously from a journal watcher's own background thread while reading
        /// NavRoute.json - SPEC §4.9) is never blocked on disk I/O, and the in-memory cache update
        /// + DataSeeded notification that already happened by the time this is called are never
        /// delayed by it either. Chained (not parallelized) deliberately: EdsmResolvedLookupStore
        /// opens and closes its own SQLite connection per call, and concurrent writers would just
        /// contend/fail against each other for no benefit - a strictly serial background queue
        /// avoids that while still keeping every write off of whichever thread is calling in.
        /// </summary>
        private void EnqueuePersist(Action write)
        {
            lock (_persistChainLock)
            {
                _persistChain = _persistChain.ContinueWith(_ => write(), TaskScheduler.Default);
            }
        }

        /// <summary>
        /// Test-only hook: awaits the current tail of the background DB-persist queue (see
        /// EnqueuePersist) so tests can deterministically confirm a seeded/cached value actually
        /// reaches the database, rather than racing a fire-and-forget write. Production code never
        /// calls this - persistence is deliberately decoupled from the in-memory cache and
        /// DataSeeded, so nothing else in this class waits on it either.
        /// </summary>
        internal Task WaitForPendingPersistAsync()
        {
            lock (_persistChainLock)
            {
                return _persistChain;
            }
        }

        /// <summary>
        /// EDSM's own `subType` values (e.g. "K (Yellow-Orange) Star") end in a literal, redundant
        /// "Star" word - the Route tab's own column is already headed "Star Type" (§4.2), so
        /// repeating it inside every cell's own value gains nothing. Stripped here, once, right
        /// where EDSM's raw response is first read, so every caller (and the cache, and
        /// StarClassNames' own local formatting, which never adds it in the first place) sees the
        /// same "Star"-free shape regardless of which of the two sources actually resolved it.
        /// </summary>
        private static string StripRedundantStarWord(string subType) =>
            subType.EndsWith(" Star", StringComparison.OrdinalIgnoreCase) ? subType[..^" Star".Length] : subType;

        private static string NormalizeKey(string systemName) => systemName.Trim().ToUpperInvariant();

        private sealed class SystemInfoResponse
        {
            public string? Name { get; set; }
            public CoordsDto? Coords { get; set; }
            public PrimaryStarDto? PrimaryStar { get; set; }
        }

        private sealed class CoordsDto
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double Z { get; set; }
        }

        private sealed class PrimaryStarDto
        {
            public string? Type { get; set; }
        }
    }
}
