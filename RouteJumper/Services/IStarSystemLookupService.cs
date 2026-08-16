using RouteJumper.Models;

namespace RouteJumper.Services
{
    /// <summary>
    /// Abstraction over <see cref="EdsmStarSystemLookupService"/> so RouteRowEnrichmentService's
    /// own coordinate/star-type lookups can be given deterministic results in tests, without
    /// depending on a real network call to EDSM - the same seam purpose
    /// <see cref="IEliteInstanceScanner"/> serves for process/journal scanning.
    /// </summary>
    public interface IStarSystemLookupService
    {
        /// <summary>
        /// Resolves galactic coordinates for every name in <paramref name="systemNames"/>
        /// (matched case-insensitively; duplicates are fine). The returned dictionary always
        /// contains every requested name as a key - a null value means EDSM has no record of that
        /// system, or the lookup itself failed - so callers never need to special-case a missing
        /// key, only a null value.
        /// </summary>
        Task<IReadOnlyDictionary<string, GalacticCoordinates?>> GetCoordinatesAsync(
            IReadOnlyList<string> systemNames, CancellationToken cancellationToken = default);

        /// <summary>
        /// Resolves one system's main star's EDSM subType (e.g. "K (Yellow-Orange)" - EDSM's own
        /// trailing "Star" word is stripped as redundant, see EdsmStarSystemLookupService), or
        /// null if the system is unknown to EDSM, has no main star on record, or the lookup
        /// itself failed.
        /// </summary>
        Task<string?> GetMainStarTypeAsync(string systemName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Seeds the coordinates cache for a system directly, without a network call - e.g. from
        /// a Ship-mode CMDR's own NavRoute.json (see ShipRouteJournalWatcher), which names exact
        /// coordinates for every system in their currently-plotted in-game route, including
        /// procedurally-generated systems EDSM may have no record of at all. A later
        /// GetCoordinatesAsync for the same name sees it as already resolved. Always overwrites
        /// any existing cached value - journal-derived data is authoritative, never worse than
        /// whatever (if anything) was already cached.
        /// </summary>
        void SeedCoordinates(string systemName, GalacticCoordinates coordinates);

        /// <summary>
        /// Seeds the star-type cache for a system directly, without a network call - e.g. from a
        /// Ship-mode CMDR's own FSDTarget event (its own StarClass field) or NavRoute.json (see
        /// ShipRouteJournalWatcher). Takes the raw journal `StarClass` code itself (e.g. "K",
        /// "TTS"), not pre-formatted display text - the implementation caches it as the canonical
        /// value and formats it for display on read (StarClassNames.ToDisplayName), the same
        /// canonical-code treatment an EDSM-resolved value gets. Always overwrites any existing
        /// cached value.
        /// </summary>
        void SeedStarType(string systemName, string starClassCode);

        /// <summary>
        /// Seeds a system's real, stable Elite Dangerous system address (id64) directly, without a
        /// network call - from NavRoute.json/FSDTarget's own SystemAddress field (see
        /// StarSystemCacheSeeder), or from a Spansh-calculated route's own jumps (Integrations &gt;
        /// Spansh, SpanshRouteJump.Id64). EDSM never supplies this - it's only ever seeded, never
        /// looked up over HTTP. Nothing currently reads this back for display; it's cached purely
        /// "as we go", the same principle Distance/Star Type already follow. Always overwrites any
        /// existing cached value.
        /// </summary>
        void SeedSystemAddress(string systemName, long systemAddress);

        /// <summary>
        /// Peeks the in-memory/DB cache for a system's coordinates without making a network call
        /// or mutating anything - unlike GetCoordinatesAsync, this never triggers an EDSM lookup
        /// on a miss. Returns false if not yet resolved (which - since only positive results are
        /// ever cached - doesn't distinguish "never looked up" from "looked up and EDSM has no
        /// record"). Lets a caller apply an already-resolved value instantly and synchronously -
        /// see RouteRowEnrichmentService.ApplyCachedValues.
        /// </summary>
        bool TryGetCachedCoordinates(string systemName, out GalacticCoordinates? coordinates);

        /// <summary>Same contract as TryGetCachedCoordinates, for star type.</summary>
        bool TryGetCachedStarType(string systemName, out string? starType);

        /// <summary>Same contract as TryGetCachedCoordinates, for the system address (id64) seeded via SeedSystemAddress.</summary>
        bool TryGetCachedSystemAddress(string systemName, out long? systemAddress);

        /// <summary>
        /// Raised whenever SeedCoordinates/SeedStarType writes new data - not raised by an
        /// ordinary GetCoordinatesAsync/GetMainStarTypeAsync resolution, since a caller already
        /// sees those results directly as part of its own in-flight call. This is what lets
        /// already-displayed Route table rows refresh once a live FSDTarget/NavRoute.json seed
        /// resolves a system that was still blank at the time the route was last Saved/restored -
        /// see RouteViewModel's own subscription - rather than only ever updating at the next
        /// Save/restore (e.g. an app restart). May fire from a background thread (journal watchers
        /// run their own file-tailing off the UI thread); subscribers must marshal accordingly.
        /// Consumers should debounce - a single NavRoute.json read seeds many systems in a tight
        /// loop, which would otherwise be many redundant refreshes in a row.
        /// </summary>
        event EventHandler? DataSeeded;
    }
}
