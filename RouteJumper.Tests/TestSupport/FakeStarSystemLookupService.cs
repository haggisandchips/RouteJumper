using RouteJumper.Models;
using RouteJumper.Services;

namespace RouteJumper.Tests.TestSupport
{
    /// <summary>
    /// Test double for IStarSystemLookupService: returns settable, per-name coordinates/star
    /// types instead of calling real EDSM.
    /// - <see cref="Gate"/>, if set, is awaited by every call before it returns - lets tests
    ///   prove Save()/RouteRowEnrichmentService.PopulateAsync don't block on a still-running
    ///   lookup.
    /// - <see cref="StarTypeGates"/> holds up GetStarTypesAsync's own per-name resolution within
    ///   a batch for one specific system name only - lets tests prove a still-pending name inside
    ///   a batch doesn't corrupt other, already-resolved state (e.g. Distance) from an earlier
    ///   phase of the same PopulateAsync call.
    /// - <see cref="StarTypeBatchRequests"/> records each GetStarTypesAsync call's full requested
    ///   name list (in order) - lets tests prove star types are resolved via genuine batched
    ///   calls, not one request per system.
    /// </summary>
    internal sealed class FakeStarSystemLookupService : IStarSystemLookupService
    {
        public Dictionary<string, GalacticCoordinates?> Coordinates { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string?> StarTypes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, long?> SystemAddresses { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> StarTypeCallOrder { get; } = new();

        public List<IReadOnlyList<string>> StarTypeBatchRequests { get; } = new();

        public TaskCompletionSource? Gate { get; set; }

        public Dictionary<string, TaskCompletionSource> StarTypeGates { get; } = new(StringComparer.OrdinalIgnoreCase);

        public async Task<IReadOnlyDictionary<string, GalacticCoordinates?>> GetCoordinatesAsync(
            IReadOnlyList<string> systemNames, CancellationToken cancellationToken = default)
        {
            if (Gate != null)
            {
                await Gate.Task.WaitAsync(cancellationToken);
            }

            var result = new Dictionary<string, GalacticCoordinates?>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in systemNames)
            {
                result[name] = Coordinates.TryGetValue(name, out var coords) ? coords : null;
            }

            return result;
        }

        public async Task<string?> GetMainStarTypeAsync(string systemName, CancellationToken cancellationToken = default)
        {
            var result = await GetStarTypesAsync(new[] { systemName }, cancellationToken);
            return result[systemName];
        }

        public async Task<IReadOnlyDictionary<string, string?>> GetStarTypesAsync(
            IReadOnlyList<string> systemNames, CancellationToken cancellationToken = default)
        {
            StarTypeBatchRequests.Add(systemNames);

            if (Gate != null)
            {
                await Gate.Task.WaitAsync(cancellationToken);
            }

            var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in systemNames)
            {
                if (result.ContainsKey(name))
                {
                    continue;
                }

                StarTypeCallOrder.Add(name);

                if (StarTypeGates.TryGetValue(name, out var gate))
                {
                    await gate.Task.WaitAsync(cancellationToken);
                }

                result[name] = StarTypes.TryGetValue(name, out var type) ? type : null;
            }

            return result;
        }

        public bool TryGetCachedCoordinates(string systemName, out GalacticCoordinates? coordinates) =>
            Coordinates.TryGetValue(systemName, out coordinates);

        public bool TryGetCachedStarType(string systemName, out string? starType) =>
            StarTypes.TryGetValue(systemName, out starType);

        public bool TryGetCachedSystemAddress(string systemName, out long? systemAddress) =>
            SystemAddresses.TryGetValue(systemName, out systemAddress);

        public event EventHandler? DataSeeded;

        public void SeedCoordinates(string systemName, GalacticCoordinates coordinates)
        {
            Coordinates[systemName] = coordinates;
            RaiseDataSeeded();
        }

        public void SeedStarType(string systemName, string starType)
        {
            StarTypes[systemName] = starType;
            RaiseDataSeeded();
        }

        public void SeedSystemAddress(string systemName, long systemAddress)
        {
            SystemAddresses[systemName] = systemAddress;
            RaiseDataSeeded();
        }

        /// <summary>Lets a test raise DataSeeded directly, without going through Seed* - e.g. to test a subscriber's debounce behavior in isolation from Coordinates/StarTypes' own contents.</summary>
        public void RaiseDataSeeded() => DataSeeded?.Invoke(this, EventArgs.Empty);
    }
}
