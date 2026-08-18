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
        /// Live autocomplete search (the Spansh menu's Source/Destination fields) - the
        /// caller debounces (200ms) before calling this, not this method itself. Returns an empty
        /// list on any failure (network down, unparsable response, ...) rather than throwing -
        /// same "best effort, never blocks the UI" convention EdsmStarSystemLookupService follows.
        /// </summary>
        Task<IReadOnlyList<SpanshSystemSuggestion>> SearchSystemNamesAsync(string query, CancellationToken cancellationToken = default);

        /// <summary>Requests a fleet-carrier route be calculated between two already-selected systems (by their own Spansh id, not name) and returns the job id to poll via <see cref="GetJobResultAsync"/>.</summary>
        Task<string> StartFleetCarrierRouteAsync(string sourceId, string destinationId, CancellationToken cancellationToken = default);

        /// <summary>One poll of a previously-started fleet-carrier job - see SpanshRouteJobStatus for the three possible outcomes.</summary>
        Task<SpanshRouteJobStatus> GetJobResultAsync(string jobId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Requests a neutron-highway route (Spansh &gt; Neutron Plotter…) be
        /// calculated between two systems, by name (Spansh's neutron router takes system names,
        /// not ids, unlike <see cref="StartFleetCarrierRouteAsync"/>) - <paramref name="range"/>
        /// and <paramref name="efficiency"/> are passed through as raw text exactly as typed, with
        /// no client-side numeric validation, since Spansh's own response already reports a clear,
        /// human-readable reason (e.g. "range must be greater than 10 LY") for anything it
        /// rejects. <paramref name="superchargeMultiplier"/> is Spansh's own
        /// <c>supercharge_multiplier</c> parameter - 4 for a regular neutron/white dwarf
        /// supercharge, 6 for an overcharged FSD booster (confirmed live via Spansh's own web
        /// client's own radio buttons, which post the same two values). Returns the job id to poll
        /// via <see cref="GetNeutronJobResultAsync"/>. Throws
        /// <see cref="InvalidOperationException"/> - with Spansh's own reported reason, when one
        /// was returned - if the request is rejected outright (bad range/efficiency, or a system
        /// name Spansh has no record of) rather than merely queued.
        /// </summary>
        Task<string> StartNeutronRouteAsync(string sourceSystemName, string destinationSystemName, string range, string efficiency, int superchargeMultiplier, CancellationToken cancellationToken = default);

        /// <summary>One poll of a previously-started neutron-route job - see SpanshRouteJobStatus for the three possible outcomes.</summary>
        Task<SpanshRouteJobStatus> GetNeutronJobResultAsync(string jobId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Requests an exact route (Spansh &gt; Galaxy Plotter…) be calculated between two
        /// systems (by id64, like <see cref="StartFleetCarrierRouteAsync"/>), using the CMDR's own
        /// real ship build (<paramref name="request"/>'s ship-derived fields, from
        /// Services\Spansh\ShipBuildDerivation, and its SLEF ship_build, from Services\Spansh\
        /// SlefSerializer) rather than a flat range. Returns the job id to poll via
        /// <see cref="GetGenericJobResultAsync"/>.
        /// </summary>
        Task<string> StartGenericRouteAsync(SpanshGenericRouteRequest request, CancellationToken cancellationToken = default);

        /// <summary>One poll of a previously-started Galaxy Plotter job - see SpanshRouteJobStatus for the three possible outcomes.</summary>
        Task<SpanshRouteJobStatus> GetGenericJobResultAsync(string jobId, CancellationToken cancellationToken = default);
    }
}
