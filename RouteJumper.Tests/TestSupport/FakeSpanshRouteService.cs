using RouteJumper.Models;
using RouteJumper.Services;

namespace RouteJumper.Tests.TestSupport
{
    /// <summary>
    /// Test double for ISpanshRouteService: returns settable, scripted results instead of calling
    /// real Spansh - used by SpanshImportViewModelTests to exercise Calculate/CalculateNeutron
    /// without a real network call.
    /// </summary>
    internal sealed class FakeSpanshRouteService : ISpanshRouteService
    {
        public IReadOnlyList<SpanshSystemSuggestion> SearchResults { get; set; } = Array.Empty<SpanshSystemSuggestion>();

        public string FleetCarrierJobId { get; set; } = "fc-job";

        public string NeutronJobId { get; set; } = "neutron-job";

        /// <summary>Thrown by StartNeutronRouteAsync, if set - simulates Spansh rejecting the request outright (e.g. a bad range).</summary>
        public Exception? StartNeutronException { get; set; }

        public SpanshRouteJobStatus FleetCarrierResult { get; set; } = SpanshRouteJobStatus.Completed(Array.Empty<SpanshRouteJump>());

        public SpanshRouteJobStatus NeutronResult { get; set; } = SpanshRouteJobStatus.Completed(Array.Empty<SpanshRouteJump>());

        public List<(string SourceName, string DestinationName, string Range, string Efficiency, int SuperchargeMultiplier)> NeutronRequests { get; } = new();

        public string GalaxyJobId { get; set; } = "galaxy-job";

        public SpanshRouteJobStatus GalaxyResult { get; set; } = SpanshRouteJobStatus.Completed(Array.Empty<SpanshRouteJump>());

        public List<SpanshGenericRouteRequest> GalaxyRequests { get; } = new();

        public Task<IReadOnlyList<SpanshSystemSuggestion>> SearchSystemNamesAsync(string query, CancellationToken cancellationToken = default) =>
            Task.FromResult(SearchResults);

        public Task<string> StartFleetCarrierRouteAsync(string sourceId, string destinationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(FleetCarrierJobId);

        public Task<SpanshRouteJobStatus> GetJobResultAsync(string jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult(FleetCarrierResult);

        public Task<string> StartNeutronRouteAsync(string sourceSystemName, string destinationSystemName, string range, string efficiency, int superchargeMultiplier, CancellationToken cancellationToken = default)
        {
            NeutronRequests.Add((sourceSystemName, destinationSystemName, range, efficiency, superchargeMultiplier));
            return StartNeutronException != null
                ? Task.FromException<string>(StartNeutronException)
                : Task.FromResult(NeutronJobId);
        }

        public Task<SpanshRouteJobStatus> GetNeutronJobResultAsync(string jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult(NeutronResult);

        public Task<string> StartGenericRouteAsync(SpanshGenericRouteRequest request, CancellationToken cancellationToken = default)
        {
            GalaxyRequests.Add(request);
            return Task.FromResult(GalaxyJobId);
        }

        public Task<SpanshRouteJobStatus> GetGenericJobResultAsync(string jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult(GalaxyResult);
    }
}
