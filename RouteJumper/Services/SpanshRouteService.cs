using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using RouteJumper.Models;
using RouteJumper.Services.Logging;

namespace RouteJumper.Services
{
    /// <summary>
    /// Talks to Spansh's fleet-carrier route planner (the Spansh menu) - undocumented,
    /// internal API endpoints, used here with the express permission of Spansh's own author,
    /// Gareth Harper (see the Spansh dialog's own thank-you note). This is the app's second-ever
    /// outbound network call to a third-party service, after EdsmStarSystemLookupService - see
    /// SPEC.md's Network section.
    ///
    /// All three endpoints' shapes below were confirmed directly against real responses (curl),
    /// not guessed:
    /// - GET /api/systems/field_values/system_names?q=... - live autocomplete search, used for
    ///   both the Source and Destination fields. Returns
    ///   <c>{"values":["Sol","Solibamba",...], "min_max":[{"id64":...,"name":"Sol","x":...,"y":...,"z":...},...]}</c> -
    ///   "values" is a bare array of matched names (no id of its own), and "min_max" is a
    ///   *separate* array carrying id64/coordinates for (in practice, but not assumed) the same
    ///   names - see <see cref="ParseSuggestions"/> for why matching is done by name rather than
    ///   assumed positional order.
    /// - POST /api/fleetcarrier/route (form fields "source"/"destination", each a system's id64 as
    ///   a string - confirmed live) - queues a route calculation and returns a job id.
    /// - GET /api/results/{job} - polled every 5 seconds by the caller (SpanshImportViewModel;
    ///   this class only ever answers one poll at a time, it doesn't loop itself). The top-level
    ///   response carries both "state" (queued/running/completed/...) and, once completed, a
    ///   sibling top-level "status" ("ok" once the route was actually computable) - both are
    ///   top-level fields, not nested under "result", which itself holds only the route's own
    ///   "jumps" array once state is "completed".
    /// - GET /api/route?efficiency=...&amp;range=...&amp;from=...&amp;to=...&amp;supercharge_multiplier=... -
    ///   the neutron-highway router (Spansh &gt; Neutron Plotter…), confirmed
    ///   live via curl - unlike the fleet-carrier endpoint above, "from"/"to" are system *names*,
    ///   not ids, and a request Spansh can reject outright (an out-of-range "range", a system name
    ///   it has no record of, ...) answers immediately with HTTP 400 and a top-level
    ///   <c>{"error": "..."}</c> rather than queuing a job at all - surfaced verbatim, since it's
    ///   already a clear, human-readable reason (e.g. "range must be greater than 10 LY").
    ///   "supercharge_multiplier" is 4 for a regular neutron/white dwarf supercharge or 6 for an
    ///   overcharged FSD booster - confirmed against Spansh's own web client's bundled JS, which
    ///   posts exactly those two values from its own "regular"/"overcharge" radio buttons; Spansh
    ///   silently accepts any value here (no equivalent immediate-rejection error), so this app
    ///   only ever sends one of those two known-good values, never free text. Once queued, polled
    ///   via the same /api/results/{job} endpoint above, but with a differently-shaped completed
    ///   result - <c>result.system_jumps</c> (not <c>result.jumps</c>), each entry's own system
    ///   name under "system" (not "name"), and only the route's own waypoints (neutron boost stops
    ///   plus the final destination), each carrying how many ordinary jumps separate it from the
    ///   previous one - not a line for every single hop the CMDR will actually fly.
    /// - POST /api/generic/route (the Spansh &gt; Galaxy Plotter… tab) - Spansh's own "exact"
    ///   router, confirmed live via curl (one real request, polled to completion). Form fields:
    ///   "source"/"destination" (id64s, like the fleet-carrier endpoint), six route-option booleans
    ///   ("is_supercharged"/"use_supercharge"/"use_injections"/"use_injections_when_required"/
    ///   "exclude_secondary"/"refuel_every_scoopable", as "1"/"0"), the ship-derived numbers
    ///   Services\Spansh\ShipBuildDerivation resolves ("fuel_power"/"fuel_multiplier"/
    ///   "optimal_mass"/"base_mass"/"tank_size"/"internal_tank_size"/"max_fuel_per_jump"/
    ///   "range_boost"/"supercharge_multiplier"/"injection_multiplier"), "reserve_size"/"cargo"
    ///   (plain user-entered text, same convention as the neutron endpoint's own range/efficiency),
    ///   a fixed "max_time=60", and "algorithm" (one of fuel/fuel_jumps/guided/optimistic/
    ///   pessimistic). Deliberately no "ship_build"/SLEF field is sent - confirmed live that a real
    ///   request with ship_build set to a bare "{}" computed a full, correct route using only the
    ///   numeric fields above, and a second request with the field omitted entirely queued
    ///   identically, so Spansh's own server-side computation never actually parses it. Queues a
    ///   job and returns its id the same way the fleet-carrier endpoint does (never an outright
    ///   HTTP 400 rejection in what was observed - defensively handled the same way regardless,
    ///   see StartGenericRouteAsync). Polled via the same /api/results/{job} endpoint; the
    ///   completed result was confirmed live to carry the same result.jumps[].{id64,name,x,y,z}
    ///   shape as the fleet-carrier endpoint (plus extra per-jump fields this app doesn't need,
    ///   e.g. fuel_used/must_refuel), so GetGenericJobResultAsync reuses the fleet-carrier
    ///   response DTOs.
    /// </summary>
    public sealed class SpanshRouteService : ISpanshRouteService
    {
        private const string BaseUrl = "https://spansh.co.uk";

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        private static readonly HttpClient SharedHttpClient = CreateHttpClient();

        private readonly HttpClient _httpClient;

        public SpanshRouteService() : this(SharedHttpClient)
        {
        }

        /// <summary>Test-only seam: lets RouteJumper.Tests inject a fake HttpMessageHandler instead of making real network calls - same precedent as EdsmStarSystemLookupService's own internal constructor.</summary>
        internal SpanshRouteService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient(new LoggingHttpMessageHandler(new HttpClientHandler())) { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("EDFCAutoPilot/1.0 (+https://github.com/haggisandchips/RouteJumper)");
            return client;
        }

        public async Task<IReadOnlyList<SpanshSystemSuggestion>> SearchSystemNamesAsync(string query, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Array.Empty<SpanshSystemSuggestion>();
            }

            try
            {
                var url = $"{BaseUrl}/api/systems/field_values/system_names?q={Uri.EscapeDataString(query)}";
                using var response = await _httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return Array.Empty<SpanshSystemSuggestion>();
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                return ParseSuggestions(doc.RootElement);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warn("Spansh", "System name search failed.", ex);
                return Array.Empty<SpanshSystemSuggestion>();
            }
        }

        /// <summary>
        /// "values" (a bare array of matched name strings, in relevance order) carries no id of
        /// its own - the only source of a usable system id is the separate "min_max" array
        /// (objects: id64/name/x/y/z). Matched by name (case-insensitive) rather than assumed
        /// positional order, since nothing about the endpoint documents that "min_max"[i]
        /// necessarily corresponds to "values"[i] - only that, in practice, the two arrays have
        /// been observed to name the same systems. A name present in "values" but missing from
        /// "min_max" is skipped entirely (never surfaced as an unselectable suggestion) - Calculate
        /// needs a real id64 to post, and there is no other id64 source for it.
        /// </summary>
        private static IReadOnlyList<SpanshSystemSuggestion> ParseSuggestions(JsonElement root)
        {
            if (!root.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<SpanshSystemSuggestion>();
            }

            var idsByName = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("min_max", out var minMax) && minMax.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in minMax.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.Object
                        && entry.TryGetProperty("name", out var nameEl) && nameEl.GetString() is { } name
                        && entry.TryGetProperty("id64", out var idEl) && idEl.TryGetInt64(out var id64))
                    {
                        idsByName[name] = id64;
                    }
                }
            }

            var results = new List<SpanshSystemSuggestion>();
            foreach (var valueEl in values.EnumerateArray())
            {
                if (valueEl.ValueKind == JsonValueKind.String
                    && valueEl.GetString() is { } name
                    && idsByName.TryGetValue(name, out var id64))
                {
                    results.Add(new SpanshSystemSuggestion(id64.ToString(CultureInfo.InvariantCulture), id64, name));
                }
            }

            return results;
        }

        public async Task<string> StartFleetCarrierRouteAsync(string sourceId, string destinationId, CancellationToken cancellationToken = default)
        {
            var url = $"{BaseUrl}/api/fleetcarrier/route";
            using var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("source", sourceId),
                new KeyValuePair<string, string>("destination", destinationId)
            });

            using var response = await _httpClient.PostAsync(url, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var parsed = await JsonSerializer.DeserializeAsync<SpanshJobResponse>(stream, JsonOptions, cancellationToken);
            if (parsed?.Job is not { } jobId)
            {
                throw new InvalidOperationException("Spansh did not return a job id.");
            }

            return jobId;
        }

        public async Task<SpanshRouteJobStatus> GetJobResultAsync(string jobId, CancellationToken cancellationToken = default)
        {
            var url = $"{BaseUrl}/api/results/{Uri.EscapeDataString(jobId)}";
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return SpanshRouteJobStatus.Failed($"HTTP {(int)response.StatusCode}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var parsed = await JsonSerializer.DeserializeAsync<SpanshResultResponse>(stream, JsonOptions, cancellationToken);
            if (parsed is null)
            {
                return SpanshRouteJobStatus.Failed("Empty response.");
            }

            if (!string.Equals(parsed.State, "completed", StringComparison.OrdinalIgnoreCase))
            {
                return SpanshRouteJobStatus.Pending(parsed.State ?? "unknown");
            }

            if (!string.Equals(parsed.Status, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return SpanshRouteJobStatus.Failed($"Spansh reported status \"{parsed.Status ?? "unknown"}\".");
            }

            var jumps = (parsed.Result?.Jumps ?? new List<SpanshJumpDto>())
                .Select(j => new SpanshRouteJump(j.Id64, j.Name ?? string.Empty, j.X, j.Y, j.Z))
                .ToList();

            return SpanshRouteJobStatus.Completed(jumps);
        }

        public async Task<string> StartNeutronRouteAsync(string sourceSystemName, string destinationSystemName, string range, string efficiency, int superchargeMultiplier, CancellationToken cancellationToken = default)
        {
            var url = $"{BaseUrl}/api/route?efficiency={Uri.EscapeDataString(efficiency)}&range={Uri.EscapeDataString(range)}"
                + $"&from={Uri.EscapeDataString(sourceSystemName)}&to={Uri.EscapeDataString(destinationSystemName)}"
                + $"&supercharge_multiplier={superchargeMultiplier}";
            using var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var reason = await TryReadSpanshErrorAsync(response, cancellationToken);
                throw new InvalidOperationException(reason ?? $"Spansh returned HTTP {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var parsed = await JsonSerializer.DeserializeAsync<SpanshJobResponse>(stream, JsonOptions, cancellationToken);
            if (parsed?.Job is not { } jobId)
            {
                throw new InvalidOperationException("Spansh did not return a job id.");
            }

            return jobId;
        }

        /// <summary>A request Spansh rejects outright (bad range/efficiency, an unrecognised system name, ...) answers with HTTP 400 and a top-level <c>{"error": "..."}</c> body - read here so StartNeutronRouteAsync can surface Spansh's own reason rather than a generic failure. Returns null (never throws) if the body isn't in that shape, so the caller falls back to a generic message instead.</summary>
        private async Task<string?> TryReadSpanshErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var parsed = await JsonSerializer.DeserializeAsync<SpanshErrorResponse>(stream, JsonOptions, cancellationToken);
                return parsed?.Error;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return null;
            }
        }

        public async Task<SpanshRouteJobStatus> GetNeutronJobResultAsync(string jobId, CancellationToken cancellationToken = default)
        {
            var url = $"{BaseUrl}/api/results/{Uri.EscapeDataString(jobId)}";
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return SpanshRouteJobStatus.Failed($"HTTP {(int)response.StatusCode}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var parsed = await JsonSerializer.DeserializeAsync<SpanshNeutronResultResponse>(stream, JsonOptions, cancellationToken);
            if (parsed is null)
            {
                return SpanshRouteJobStatus.Failed("Empty response.");
            }

            if (!string.Equals(parsed.State, "completed", StringComparison.OrdinalIgnoreCase))
            {
                return SpanshRouteJobStatus.Pending(parsed.State ?? "unknown");
            }

            if (!string.Equals(parsed.Status, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return SpanshRouteJobStatus.Failed($"Spansh reported status \"{parsed.Status ?? "unknown"}\".");
            }

            var jumps = (parsed.Result?.SystemJumps ?? new List<SpanshNeutronWaypointDto>())
                .Select(j => new SpanshRouteJump(j.Id64, j.SystemName ?? string.Empty, j.X, j.Y, j.Z))
                .ToList();

            return SpanshRouteJobStatus.Completed(jumps);
        }

        public async Task<string> StartGenericRouteAsync(SpanshGenericRouteRequest request, CancellationToken cancellationToken = default)
        {
            var url = $"{BaseUrl}/api/generic/route";
            using var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("source", request.SourceId),
                new KeyValuePair<string, string>("destination", request.DestinationId),
                new KeyValuePair<string, string>("is_supercharged", request.IsSupercharged ? "1" : "0"),
                new KeyValuePair<string, string>("use_supercharge", request.UseSupercharge ? "1" : "0"),
                new KeyValuePair<string, string>("use_injections", request.UseInjections ? "1" : "0"),
                new KeyValuePair<string, string>("use_injections_when_required", request.UseInjectionsWhenRequired ? "1" : "0"),
                new KeyValuePair<string, string>("exclude_secondary", request.ExcludeSecondary ? "1" : "0"),
                new KeyValuePair<string, string>("refuel_every_scoopable", request.RefuelEveryScoopable ? "1" : "0"),
                new KeyValuePair<string, string>("fuel_power", request.FuelPower.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("fuel_multiplier", request.FuelMultiplier.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("optimal_mass", request.OptimalMass.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("base_mass", request.BaseMass.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("tank_size", request.TankSize.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("internal_tank_size", request.InternalTankSize.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("reserve_size", request.ReserveSize),
                new KeyValuePair<string, string>("max_fuel_per_jump", request.MaxFuelPerJump.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("range_boost", request.RangeBoost.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("max_time", "60"),
                new KeyValuePair<string, string>("cargo", request.Cargo),
                new KeyValuePair<string, string>("algorithm", request.Algorithm),
                new KeyValuePair<string, string>("supercharge_multiplier", request.SuperchargeMultiplier.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("injection_multiplier", request.InjectionMultiplier.ToString(CultureInfo.InvariantCulture))
            });

            using var response = await _httpClient.PostAsync(url, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // Never observed live (the one real request made while building this queued
                // successfully), but handled defensively the same way StartNeutronRouteAsync
                // handles its own outright-rejection case, in case this endpoint can reject too.
                var reason = await TryReadSpanshErrorAsync(response, cancellationToken);
                throw new InvalidOperationException(reason ?? $"Spansh returned HTTP {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var parsed = await JsonSerializer.DeserializeAsync<SpanshJobResponse>(stream, JsonOptions, cancellationToken);
            if (parsed?.Job is not { } jobId)
            {
                throw new InvalidOperationException("Spansh did not return a job id.");
            }

            return jobId;
        }

        public async Task<SpanshRouteJobStatus> GetGenericJobResultAsync(string jobId, CancellationToken cancellationToken = default)
        {
            // Reuses SpanshResultResponse/SpanshJumpDto (the fleet-carrier shape) - see this
            // class's own doc comment on why, and the caveat that it wasn't directly observed.
            var url = $"{BaseUrl}/api/results/{Uri.EscapeDataString(jobId)}";
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return SpanshRouteJobStatus.Failed($"HTTP {(int)response.StatusCode}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var parsed = await JsonSerializer.DeserializeAsync<SpanshResultResponse>(stream, JsonOptions, cancellationToken);
            if (parsed is null)
            {
                return SpanshRouteJobStatus.Failed("Empty response.");
            }

            if (!string.Equals(parsed.State, "completed", StringComparison.OrdinalIgnoreCase))
            {
                return SpanshRouteJobStatus.Pending(parsed.State ?? "unknown");
            }

            if (!string.Equals(parsed.Status, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return SpanshRouteJobStatus.Failed($"Spansh reported status \"{parsed.Status ?? "unknown"}\".");
            }

            var jumps = (parsed.Result?.Jumps ?? new List<SpanshJumpDto>())
                .Select(j => new SpanshRouteJump(j.Id64, j.Name ?? string.Empty, j.X, j.Y, j.Z))
                .ToList();

            return SpanshRouteJobStatus.Completed(jumps);
        }

        private sealed class SpanshErrorResponse
        {
            public string? Error { get; set; }
        }

        /// <summary>"state"/"status" are the same sibling top-level fields as the fleet-carrier response (SpanshResultResponse) - only the nested "result" shape differs (system_jumps, not jumps).</summary>
        private sealed class SpanshNeutronResultResponse
        {
            public string? State { get; set; }
            public string? Status { get; set; }
            public SpanshNeutronResultBody? Result { get; set; }
        }

        private sealed class SpanshNeutronResultBody
        {
            [JsonPropertyName("system_jumps")]
            public List<SpanshNeutronWaypointDto>? SystemJumps { get; set; }
        }

        private sealed class SpanshNeutronWaypointDto
        {
            public long Id64 { get; set; }

            [JsonPropertyName("system")]
            public string? SystemName { get; set; }

            public double X { get; set; }
            public double Y { get; set; }
            public double Z { get; set; }
        }

        private sealed class SpanshJobResponse
        {
            public string? Job { get; set; }
            public string? Status { get; set; }
        }

        /// <summary>"state" and "status" are sibling top-level fields (confirmed live) - "result" itself carries no status of its own, only "jumps" once state is "completed".</summary>
        private sealed class SpanshResultResponse
        {
            public string? State { get; set; }
            public string? Status { get; set; }
            public SpanshResultBody? Result { get; set; }
        }

        private sealed class SpanshResultBody
        {
            public List<SpanshJumpDto>? Jumps { get; set; }
        }

        private sealed class SpanshJumpDto
        {
            public long Id64 { get; set; }
            public string? Name { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public double Z { get; set; }
        }
    }
}
