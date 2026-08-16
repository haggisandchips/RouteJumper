using RouteJumper.Models;

namespace RouteJumper.Services
{
    /// <summary>
    /// Abstraction over <see cref="SpanshRouteService"/> so SpanshImportViewModel's own tests can
    /// be given deterministic results without a real network call - the same seam purpose
    /// <see cref="IStarSystemLookupService"/> serves for EDSM.
    /// </summary>
    public interface ISpanshRouteService
    {
        /// <summary>
        /// Live autocomplete search (Integrations &gt; Spansh's Source/Destination fields) - the
        /// caller debounces (200ms) before calling this, not this method itself. Returns an empty
        /// list on any failure (network down, unparsable response, ...) rather than throwing -
        /// same "best effort, never blocks the UI" convention EdsmStarSystemLookupService follows.
        /// </summary>
        Task<IReadOnlyList<SpanshSystemSuggestion>> SearchSystemNamesAsync(string query, CancellationToken cancellationToken = default);

        /// <summary>Requests a fleet-carrier route be calculated between two already-selected systems (by their own Spansh id, not name) and returns the job id to poll via <see cref="GetJobResultAsync"/>.</summary>
        Task<string> StartFleetCarrierRouteAsync(string sourceId, string destinationId, CancellationToken cancellationToken = default);

        /// <summary>One poll of a previously-started job - see SpanshRouteJobStatus for the three possible outcomes.</summary>
        Task<SpanshRouteJobStatus> GetJobResultAsync(string jobId, CancellationToken cancellationToken = default);
    }
}
