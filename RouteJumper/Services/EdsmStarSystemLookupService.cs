using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using RouteJumper.Models;

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
    /// Two endpoints, two different shapes of care:
    /// - Coordinates: `api-v1/systems` (plural) accepts multiple names in one request via
    ///   repeated `systemName[]=` params (confirmed against the real API - GET, not POST, and
    ///   array-style repeated params, not a JSON body) and returns a JSON array containing only
    ///   the systems it actually has coordinates for - an unresolvable name is simply absent from
    ///   the array, never present with a null/empty entry. EDSM's exact per-request batch-size
    ///   cap isn't publicly documented, so requests are defensively chunked
    ///   (<see cref="CoordinatesBatchSize"/>).
    /// - Star type: `api-system-v1/bodies` is per-system only, no bulk variant, and is throttled
    ///   by EDSM to roughly 700 lookups/minute (~11-12/sec) - <see cref="MinBodiesRequestIntervalMs"/>
    ///   paces requests comfortably under that.
    ///
    /// Both are cached indefinitely in <see cref="AppSettingsStore"/> once resolved (a named
    /// system's coordinates never change) - but only positive results are cached; an unresolved
    /// name is retried on the next lookup rather than permanently remembered as unknown, since
    /// EDSM's own crowdsourced data can fill in later. Any failure (network, non-success status,
    /// malformed response) degrades that lookup to "unresolved" (null) rather than throwing -
    /// this is a best-effort enrichment, never something that should block or fail the Route
    /// tab's own Save.
    ///
    /// A resolved value is visible immediately via an in-memory cache (<see cref="_coordsMemoryCache"/>/
    /// <see cref="_starTypeMemoryCache"/>) - every read checks memory first, falling back to
    /// <see cref="AppSettingsStore"/> only on a miss (and back-filling memory from that DB read for
    /// next time). Writing to disk is deliberately decoupled from both the memory update and
    /// <see cref="DataSeeded"/> - see <see cref="EnqueuePersist"/> - so a burst of seeds (e.g. every
    /// system in a freshly-read NavRoute.json, SPEC §4.9) updates the UI essentially instantly,
    /// with the (comparatively slow, one-open-close-per-write) SQLite persistence trailing behind
    /// on its own background queue rather than delaying the notification that drives it.
    /// </summary>
    public class EdsmStarSystemLookupService : IStarSystemLookupService
    {
        private const string BaseUrl = "https://www.edsm.net";
        private const string CoordsCacheKeyPrefix = "EdsmCoords:";
        private const string StarTypeCacheKeyPrefix = "EdsmStarType:";

        /// <summary>Internal (not private) so tests can assert chunking against the real value rather than a hardcoded duplicate.</summary>
        internal const int CoordinatesBatchSize = 10;

        private const int MinBodiesRequestIntervalMs = 150;

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        private static readonly HttpClient SharedHttpClient = CreateHttpClient();

        private readonly AppSettingsStore _settings;
        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _bodiesThrottleGate = new(1, 1);
        private DateTime _lastBodiesRequestUtc = DateTime.MinValue;

        private readonly ConcurrentDictionary<string, GalacticCoordinates> _coordsMemoryCache = new();
        private readonly ConcurrentDictionary<string, string> _starTypeMemoryCache = new();

        /// <summary>Serializes background DB writes (see EnqueuePersist) into a single chain, so concurrent SQLite connections are never opened from multiple writes racing each other.</summary>
        private readonly object _persistChainLock = new();
        private Task _persistChain = Task.CompletedTask;

        public EdsmStarSystemLookupService(AppSettingsStore settings) : this(settings, SharedHttpClient)
        {
        }

        /// <summary>Test-only seam: lets RouteJumper.Tests inject a fake HttpMessageHandler instead of making real network calls.</summary>
        internal EdsmStarSystemLookupService(AppSettingsStore settings, HttpClient httpClient)
        {
            _settings = settings;
            _httpClient = httpClient;
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
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
                else
                {
                    result[name] = null; // placeholder - overwritten below if this chunk resolves it
                    toFetch.Add(name);
                }
            }

            for (var i = 0; i < toFetch.Count; i += CoordinatesBatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunk = toFetch.Skip(i).Take(CoordinatesBatchSize).ToList();
                await FetchCoordinatesChunkAsync(chunk, result, cancellationToken);
            }

            return result;
        }

        private async Task FetchCoordinatesChunkAsync(
            IReadOnlyList<string> names, Dictionary<string, GalacticCoordinates?> result, CancellationToken cancellationToken)
        {
            try
            {
                var query = string.Join("&", names.Select(n => $"systemName[]={Uri.EscapeDataString(n)}"));
                var url = $"{BaseUrl}/api-v1/systems?{query}&showCoordinates=1";

                using var response = await _httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var entries = await JsonSerializer.DeserializeAsync<List<SystemCoordsResponse>>(stream, JsonOptions, cancellationToken);
                if (entries is null)
                {
                    return;
                }

                foreach (var entry in entries)
                {
                    if (entry.Name is not { } name || entry.Coords is not { } coordsDto)
                    {
                        continue;
                    }

                    var coordinates = new GalacticCoordinates(coordsDto.X, coordsDto.Y, coordsDto.Z);
                    result[name] = coordinates;
                    CacheCoordinates(name, coordinates);
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
                // out and block/fail the Route tab's own Save.
            }
        }

        public async Task<string?> GetMainStarTypeAsync(string systemName, CancellationToken cancellationToken = default)
        {
            if (TryGetCachedStarType(systemName, out var cached))
            {
                return cached;
            }

            await WaitForBodiesRequestSlotAsync(cancellationToken);

            try
            {
                var url = $"{BaseUrl}/api-system-v1/bodies?systemName={Uri.EscapeDataString(systemName)}";

                using var response = await _httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var body = await JsonSerializer.DeserializeAsync<BodiesResponse>(stream, JsonOptions, cancellationToken);
                var stars = body?.Bodies?.Where(b => string.Equals(b.Type, "Star", StringComparison.OrdinalIgnoreCase)).ToList();
                if (stars is not { Count: > 0 })
                {
                    return null;
                }

                var mainStar = stars.FirstOrDefault(b => b.IsMainStar) ?? stars[0];
                if (mainStar.SubType is not { } rawSubType)
                {
                    return null;
                }

                var subType = StripRedundantStarWord(rawSubType);
                CacheStarType(systemName, subType);
                return subType;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public event EventHandler? DataSeeded;

        public void SeedCoordinates(string systemName, GalacticCoordinates coordinates)
        {
            CacheCoordinates(systemName, coordinates);
            DataSeeded?.Invoke(this, EventArgs.Empty);
        }

        public void SeedStarType(string systemName, string starType)
        {
            CacheStarType(systemName, starType);
            DataSeeded?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Enforces MinBodiesRequestIntervalMs between successive `api-system-v1/bodies` requests, since that endpoint (unlike the coordinates one) has no bulk variant to naturally space calls out.</summary>
        private async Task WaitForBodiesRequestSlotAsync(CancellationToken cancellationToken)
        {
            await _bodiesThrottleGate.WaitAsync(cancellationToken);
            try
            {
                var minInterval = TimeSpan.FromMilliseconds(MinBodiesRequestIntervalMs);
                var elapsed = DateTime.UtcNow - _lastBodiesRequestUtc;
                if (elapsed < minInterval)
                {
                    await Task.Delay(minInterval - elapsed, cancellationToken);
                }

                _lastBodiesRequestUtc = DateTime.UtcNow;
            }
            finally
            {
                _bodiesThrottleGate.Release();
            }
        }

        private bool TryGetCachedCoordinates(string systemName, out GalacticCoordinates? coordinates)
        {
            var key = NormalizeKey(systemName);
            if (_coordsMemoryCache.TryGetValue(key, out var memoized))
            {
                coordinates = memoized;
                return true;
            }

            var raw = _settings.GetString(CoordsCacheKeyPrefix + key);
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

        private bool TryGetCachedStarType(string systemName, out string? starType)
        {
            var key = NormalizeKey(systemName);
            if (_starTypeMemoryCache.TryGetValue(key, out var memoized))
            {
                starType = memoized;
                return true;
            }

            var raw = _settings.GetString(StarTypeCacheKeyPrefix + key);
            if (raw != null)
            {
                _starTypeMemoryCache[key] = raw; // back-fill memory so the next read skips the DB
                starType = raw;
                return true;
            }

            starType = null;
            return false;
        }

        /// <summary>
        /// Updates the in-memory cache immediately (so a caller's very next read, or this same
        /// resolved value flowing straight back out of the async lookup that just produced it, is
        /// already consistent) and enqueues the actual disk write - see EnqueuePersist for why that
        /// write deliberately isn't done inline here.
        /// </summary>
        private void CacheCoordinates(string systemName, GalacticCoordinates coordinates)
        {
            var key = NormalizeKey(systemName);
            _coordsMemoryCache[key] = coordinates;

            EnqueuePersist(() =>
            {
                var raw = string.Join("|",
                    coordinates.X.ToString(CultureInfo.InvariantCulture),
                    coordinates.Y.ToString(CultureInfo.InvariantCulture),
                    coordinates.Z.ToString(CultureInfo.InvariantCulture));
                _settings.SetString(CoordsCacheKeyPrefix + key, raw);
            });
        }

        /// <summary>See CacheCoordinates's own doc comment - identical shape for the star-type cache.</summary>
        private void CacheStarType(string systemName, string starType)
        {
            var key = NormalizeKey(systemName);
            _starTypeMemoryCache[key] = starType;
            EnqueuePersist(() => _settings.SetString(StarTypeCacheKeyPrefix + key, starType));
        }

        /// <summary>
        /// Appends <paramref name="write"/> to a single serial chain of background DB writes -
        /// never awaited or run inline, so a caller (in particular SeedCoordinates/SeedStarType,
        /// called synchronously from a journal watcher's own background thread while reading
        /// NavRoute.json - SPEC §4.9) is never blocked on disk I/O, and the in-memory cache update
        /// + DataSeeded notification that already happened by the time this is called are never
        /// delayed by it either. Chained (not parallelized) deliberately: AppSettingsStore opens
        /// and closes its own SQLite connection per call, and concurrent writers would just
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

        private sealed class SystemCoordsResponse
        {
            public string? Name { get; set; }
            public CoordsDto? Coords { get; set; }
        }

        private sealed class CoordsDto
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double Z { get; set; }
        }

        private sealed class BodiesResponse
        {
            public List<BodyDto>? Bodies { get; set; }
        }

        private sealed class BodyDto
        {
            public string? Type { get; set; }
            public string? SubType { get; set; }
            public bool IsMainStar { get; set; }
        }
    }
}
