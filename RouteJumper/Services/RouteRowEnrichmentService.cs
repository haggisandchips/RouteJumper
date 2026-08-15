using RouteJumper.Models;
using RouteJumper.ViewModels;

namespace RouteJumper.Services
{
    /// <summary>
    /// Orchestrates populating a saved route's Distance and Star Type columns
    /// (<see cref="RouteRowViewModel"/>) against <see cref="IStarSystemLookupService"/> - pure
    /// orchestration, no I/O of its own (that's entirely IStarSystemLookupService's concern), so
    /// this class is trivially testable with a hand-written fake. Distance and star-type
    /// population run concurrently, since they're independent (different EDSM endpoints,
    /// different result shapes) and neither should wait on the other.
    /// </summary>
    public class RouteRowEnrichmentService
    {
        private readonly IStarSystemLookupService _lookup;

        public RouteRowEnrichmentService(IStarSystemLookupService lookup)
        {
            _lookup = lookup;
        }

        /// <summary>
        /// Populates every row's Distance and StarType in place. <paramref name="originSystemName"/>
        /// is row 1's own "previous" system (see RouteRowViewModel.Distance's own doc comment) -
        /// null if unknown, which simply leaves row 1's Distance null too, the same as any other
        /// unresolvable system would.
        /// </summary>
        public async Task PopulateAsync(
            IReadOnlyList<RouteRowViewModel> rows, string? originSystemName, CancellationToken cancellationToken = default)
        {
            if (rows.Count == 0)
            {
                return;
            }

            await Task.WhenAll(
                PopulateDistancesAsync(rows, originSystemName, cancellationToken),
                PopulateStarTypesAsync(rows, cancellationToken));
        }

        /// <summary>
        /// One GetCoordinatesAsync call for the origin plus every row's own system (internal
        /// chunking is IStarSystemLookupService's concern), then a single pass chaining each
        /// row's Distance from whichever system preceded it - the origin for row 1, the previous
        /// row's own system from row 2 on. An unresolved link (either endpoint missing
        /// coordinates) leaves that one row's Distance null without affecting any other row's.
        /// </summary>
        private async Task PopulateDistancesAsync(
            IReadOnlyList<RouteRowViewModel> rows, string? originSystemName, CancellationToken cancellationToken)
        {
            var names = new List<string>();
            if (originSystemName is { Length: > 0 })
            {
                names.Add(originSystemName);
            }

            names.AddRange(rows.Select(r => r.SystemText));

            IReadOnlyDictionary<string, GalacticCoordinates?> coordinates;
            try
            {
                coordinates = await _lookup.GetCoordinatesAsync(names, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Best-effort - every row's Distance simply stays null. The whole batch failed
                // (e.g. network down), so every row's own state is unknowable from this pass -
                // marked Unavailable rather than left Resolving, so a genuinely offline session
                // doesn't leave every row looking like it's still silently loading forever.
                foreach (var row in rows)
                {
                    row.OwnCoordinatesState = EdsmLookupState.Unavailable;
                }

                return;
            }

            var from = originSystemName is { Length: > 0 } && coordinates.TryGetValue(originSystemName, out var originCoords)
                ? originCoords
                : null;

            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var to = coordinates.TryGetValue(row.SystemText, out var rowCoords) ? rowCoords : null;
                row.OwnCoordinatesState = to.HasValue ? EdsmLookupState.Resolved : EdsmLookupState.Unavailable;
                row.Distance = from.HasValue && to.HasValue ? from.Value.DistanceTo(to.Value) : null;
                from = to;
            }
        }

        /// <summary>
        /// Resolves each distinct system name at most once (a route revisiting the same system
        /// more than once costs one lookup, not one per occurrence), applying the result to every
        /// row sharing that name. Awaiting one system at a time (rather than fanning every row out
        /// concurrently) is what naturally paces requests against
        /// EdsmStarSystemLookupService's own per-request throttle, and lets rows populate
        /// progressively in the UI as each lookup completes rather than all appearing at once.
        /// </summary>
        private async Task PopulateStarTypesAsync(IReadOnlyList<RouteRowViewModel> rows, CancellationToken cancellationToken)
        {
            var resolved = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (resolved.TryGetValue(row.SystemText, out var cachedType))
                {
                    row.StarType = cachedType;
                    row.OwnStarTypeState = cachedType is null ? EdsmLookupState.Unavailable : EdsmLookupState.Resolved;
                    continue;
                }

                string? starType;
                try
                {
                    starType = await _lookup.GetMainStarTypeAsync(row.SystemText, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    starType = null; // best-effort - this system's StarType simply stays null
                }

                resolved[row.SystemText] = starType;
                row.StarType = starType;
                row.OwnStarTypeState = starType is null ? EdsmLookupState.Unavailable : EdsmLookupState.Resolved;
            }
        }

        /// <summary>
        /// Applies whatever Distance/Star Type values are already resolvable purely from
        /// IStarSystemLookupService's cache - no network calls, so (unlike PopulateAsync) this
        /// never suspends on an await and is safe to call synchronously and immediately, e.g. from
        /// RouteViewModel's DataSeeded handler, ahead of and independent of the slower debounced
        /// PopulateAsync catch-all. Mirrors PopulateDistancesAsync's own chain logic exactly, minus
        /// the EDSM fallback - a row this can't resolve (an uncached neighbor breaking the distance
        /// chain, or a system genuinely not yet seeded/looked up) is simply left as-is for that
        /// later pass to finish, not touched here. Never sets a row's OwnCoordinatesState/
        /// OwnStarTypeState to Unavailable itself - a cache miss here doesn't distinguish "still
        /// resolving" from "confirmed unavailable"; only a completed PopulateAsync pass (which
        /// actually calls EDSM) can make that determination.
        /// </summary>
        public void ApplyCachedValues(IReadOnlyList<RouteRowViewModel> rows, string? originSystemName)
        {
            GalacticCoordinates? from = originSystemName is { Length: > 0 }
                && _lookup.TryGetCachedCoordinates(originSystemName, out var originCoords)
                ? originCoords
                : null;

            foreach (var row in rows)
            {
                var haveTo = _lookup.TryGetCachedCoordinates(row.SystemText, out var to);
                if (haveTo)
                {
                    row.OwnCoordinatesState = EdsmLookupState.Resolved;
                }

                if (from.HasValue && haveTo && to.HasValue)
                {
                    row.Distance = from.Value.DistanceTo(to.Value);
                }

                from = haveTo ? to : null;

                if (_lookup.TryGetCachedStarType(row.SystemText, out var starType))
                {
                    row.StarType = starType;
                    row.OwnStarTypeState = EdsmLookupState.Resolved;
                }
            }
        }
    }
}
