using RouteJumper.Models;
using RouteJumper.Services;

namespace RouteJumper.Tests.TestSupport
{
    /// <summary>
    /// Test double for IStarSystemLookupService: returns settable, per-name coordinates/star
    /// types instead of calling real EDSM. Both methods can be held up:
    /// - <see cref="Gate"/>, if set, is awaited by every call before it returns - lets tests
    ///   prove Save()/RouteRowEnrichmentService.PopulateAsync don't block on a still-running
    ///   lookup.
    /// - <see cref="StarTypeGates"/> holds up GetMainStarTypeAsync for one specific system name
    ///   only - lets tests prove star types populate progressively, one system at a time, rather
    ///   than all at once.
    /// </summary>
    internal sealed class FakeStarSystemLookupService : IStarSystemLookupService
    {
        public Dictionary<string, GalacticCoordinates?> Coordinates { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string?> StarTypes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> StarTypeCallOrder { get; } = new();

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
            StarTypeCallOrder.Add(systemName);

            if (Gate != null)
            {
                await Gate.Task.WaitAsync(cancellationToken);
            }

            if (StarTypeGates.TryGetValue(systemName, out var gate))
            {
                await gate.Task.WaitAsync(cancellationToken);
            }

            return StarTypes.TryGetValue(systemName, out var type) ? type : null;
        }

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

        /// <summary>Lets a test raise DataSeeded directly, without going through Seed* - e.g. to test a subscriber's debounce behavior in isolation from Coordinates/StarTypes' own contents.</summary>
        public void RaiseDataSeeded() => DataSeeded?.Invoke(this, EventArgs.Empty);
    }
}
