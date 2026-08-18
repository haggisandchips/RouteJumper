using System.Net;
using System.Net.Http;
using RouteJumper.Models;
using RouteJumper.Services;
using RouteJumper.Tests.TestSupport;
using Xunit;

namespace RouteJumper.Tests.Services
{
    public class SpanshRouteServiceTests
    {
        private static (SpanshRouteService Service, FakeHttpMessageHandler Handler) Create()
        {
            var handler = new FakeHttpMessageHandler();
            var service = new SpanshRouteService(new HttpClient(handler));
            return (service, handler);
        }

        [Fact]
        public async Task SearchSystemNamesAsync_BlankQuery_ReturnsEmptyWithoutRequesting()
        {
            var (service, handler) = Create();

            var result = await service.SearchSystemNamesAsync("   ");

            Assert.Empty(result);
            Assert.Empty(handler.RequestedUrls);
        }

        [Fact]
        public async Task SearchSystemNamesAsync_RequestsExpectedUrl()
        {
            var (service, handler) = Create();
            handler.Respond = _ => (HttpStatusCode.OK, """{"values":[],"min_max":[]}""");

            await service.SearchSystemNamesAsync("Sol");

            var url = Assert.Single(handler.RequestedUrls);
            Assert.Contains("/api/systems/field_values/system_names?q=Sol", url);
        }

        // Real response shape confirmed live via curl against
        // https://spansh.co.uk/api/systems/field_values/system_names?q=Sol - "values" is a bare
        // array of matched name strings; "min_max" is a *separate* array carrying id64/coordinates,
        // matched by name (see SpanshRouteService.ParseSuggestions).
        [Fact]
        public async Task SearchSystemNamesAsync_ValuesAndMinMaxMatchedByName_ParsedIntoSuggestions()
        {
            var (service, handler) = Create();
            handler.Respond = _ => (HttpStatusCode.OK, """
                {"min_max":[{"id64":10477373803,"name":"Sol","x":0.0,"y":0.0,"z":0.0},{"id64":1458376315610,"name":"Solati","x":66.53125,"y":29.1875,"z":34.6875}],"values":["Sol","Solati"]}
                """);

            var result = await service.SearchSystemNamesAsync("Sol");

            Assert.Equal(2, result.Count);
            Assert.Equal(new SpanshSystemSuggestion("10477373803", 10477373803, "Sol"), result[0]);
            Assert.Equal(new SpanshSystemSuggestion("1458376315610", 1458376315610, "Solati"), result[1]);
        }

        [Fact]
        public async Task SearchSystemNamesAsync_NameInValuesButMissingFromMinMax_IsSkipped()
        {
            var (service, handler) = Create();
            handler.Respond = _ => (HttpStatusCode.OK, """
                {"min_max":[{"id64":1,"name":"Deciat","x":0,"y":0,"z":0}],"values":["Sol","Deciat"]}
                """);

            var result = await service.SearchSystemNamesAsync("S");

            var suggestion = Assert.Single(result);
            Assert.Equal("Deciat", suggestion.Name);
        }

        [Fact]
        public async Task SearchSystemNamesAsync_NoMinMaxAtAll_ReturnsEmpty()
        {
            var (service, handler) = Create();
            handler.Respond = _ => (HttpStatusCode.OK, """{"values":["Sol","Solati"]}""");

            var result = await service.SearchSystemNamesAsync("Sol");

            Assert.Empty(result);
        }

        [Fact]
        public async Task SearchSystemNamesAsync_NonSuccessStatus_ReturnsEmpty()
        {
            var (service, handler) = Create();
            handler.Respond = _ => (HttpStatusCode.InternalServerError, "oops");

            var result = await service.SearchSystemNamesAsync("Sol");

            Assert.Empty(result);
        }

        [Fact]
        public async Task SearchSystemNamesAsync_UnparsableResponse_ReturnsEmptyRatherThanThrowing()
        {
            var (service, handler) = Create();
            handler.Respond = _ => (HttpStatusCode.OK, "not json");

            var result = await service.SearchSystemNamesAsync("Sol");

            Assert.Empty(result);
        }

        [Fact]
        public async Task StartFleetCarrierRouteAsync_PostsSourceAndDestinationFormFields_ReturnsJobId()
        {
            var (service, handler) = Create();
            handler.Respond = _ => (HttpStatusCode.OK, """{"job":"abc-123","status":"queued"}""");

            var jobId = await service.StartFleetCarrierRouteAsync("111", "222");

            Assert.Equal("abc-123", jobId);
            var url = Assert.Single(handler.RequestedUrls);
            Assert.Contains("/api/fleetcarrier/route", url);
            var body = Assert.Single(handler.RequestedBodies);
            Assert.Contains("source=111", body);
            Assert.Contains("destination=222", body);
        }

        [Fact]
        public async Task StartFleetCarrierRouteAsync_NoJobInResponse_Throws()
        {
            var (service, handler) = Create();
            handler.Respond = _ => (HttpStatusCode.OK, """{"status":"queued"}""");

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartFleetCarrierRouteAsync("111", "222"));
        }

        [Fact]
        public async Task GetJobResultAsync_StateQueued_ReturnsPending()
        {
            var (service, handler) = Create();
            handler.Respond = _ => (HttpStatusCode.OK, """{"job":"abc-123","state":"queued"}""");

            var status = await service.GetJobResultAsync("abc-123");

            Assert.Equal(SpanshJobState.Pending, status.State);
            Assert.Equal("queued", status.StatusText);
        }

        // Real response shape confirmed live via curl (POST /api/fleetcarrier/route then GET
        // /api/results/{job}) - "state" and "status" are sibling top-level fields, not nested
        // under "result", which itself carries only "jumps" once state is "completed".
        [Fact]
        public async Task GetJobResultAsync_StateCompletedStatusOk_ReturnsJumps()
        {
            var (service, handler) = Create();
            handler.Respond = _ => (HttpStatusCode.OK, """
                {
                  "job": "abc-123",
                  "state": "completed",
                  "status": "ok",
                  "result": {
                    "jumps": [
                      { "id64": 10477373803, "name": "Sol", "x": 0, "y": 0, "z": 0 },
                      { "id64": 6681123623626, "name": "Deciat", "x": 122.625, "y": -0.8125, "z": -47.28125 }
                    ]
                  }
                }
                """);

            var status = await service.GetJobResultAsync("abc-123");

            Assert.Equal(SpanshJobState.Completed, status.State);
            Assert.Equal(2, status.Jumps.Count);
            Assert.Equal(new SpanshRouteJump(10477373803, "Sol", 0.0, 0.0, 0.0), status.Jumps[0]);
            Assert.Equal(new SpanshRouteJump(6681123623626, "Deciat", 122.625, -0.8125, -47.28125), status.Jumps[1]);
        }

        [Fact]
        public async Task GetJobResultAsync_StateCompletedStatusNotOk_ReturnsFailed()
        {
            var (service, handler) = Create();
            handler.Respond = _ => (HttpStatusCode.OK, """{"job":"abc-123","state":"completed","status":"error"}""");

            var status = await service.GetJobResultAsync("abc-123");

            Assert.Equal(SpanshJobState.Failed, status.State);
            Assert.Contains("error", status.FailureReason);
        }

        [Fact]
        public async Task GetJobResultAsync_NonSuccessHttpStatus_ReturnsFailed()
        {
            var (service, handler) = Create();
            handler.Respond = _ => (HttpStatusCode.NotFound, "");

            var status = await service.GetJobResultAsync("abc-123");

            Assert.Equal(SpanshJobState.Failed, status.State);
        }

        // ===================== StartNeutronRouteAsync / GetNeutronJobResultAsync (Neutron Plotter
        // tab) - real request/response shapes confirmed live via curl against
        // https://spansh.co.uk/api/route and https://spansh.co.uk/api/results/{job}. Unlike the
        // fleet-carrier endpoints above, "from"/"to" are system names (not ids), and a request
        // Spansh rejects outright (bad range/efficiency, an unrecognised system name) answers
        // immediately with HTTP 400 and a top-level {"error": "..."} body rather than queuing. =====================

        [Fact]
        public async Task StartNeutronRouteAsync_RequestsExpectedUrl_ReturnsJobId()
        {
            var (service, handler) = Create();
            handler.Respond = _ => (HttpStatusCode.OK, """{"job":"abc-123","status":"queued"}""");

            var jobId = await service.StartNeutronRouteAsync("Sol", "Sirius", "50", "60", 4);

            Assert.Equal("abc-123", jobId);
            var url = Assert.Single(handler.RequestedUrls);
            Assert.Contains("/api/route?", url);
            Assert.Contains("efficiency=60", url);
            Assert.Contains("range=50", url);
            Assert.Contains("from=Sol", url);
            Assert.Contains("to=Sirius", url);
            Assert.Contains("supercharge_multiplier=4", url);
        }

        [Fact]
        public async Task StartNeutronRouteAsync_Overcharge_RequestsMultiplierSix()
        {
            var (service, handler) = Create();
            handler.Respond = _ => (HttpStatusCode.OK, """{"job":"abc-123","status":"queued"}""");

            await service.StartNeutronRouteAsync("Sol", "Sirius", "50", "60", 6);

            var url = Assert.Single(handler.RequestedUrls);
            Assert.Contains("supercharge_multiplier=6", url);
        }

        [Fact]
        public async Task StartNeutronRouteAsync_NoJobInResponse_Throws()
        {
            var (service, handler) = Create();
            handler.Respond = _ => (HttpStatusCode.OK, """{"status":"queued"}""");

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartNeutronRouteAsync("Sol", "Sirius", "50", "60", 4));
        }

        [Fact]
        public async Task StartNeutronRouteAsync_RejectedOutright_ThrowsWithSpanshsOwnReason()
        {
            var (service, handler) = Create();
            handler.Respond = _ => (HttpStatusCode.BadRequest, """{"error":"range must be greater than 10 LY"}""");

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartNeutronRouteAsync("Sol", "Sirius", "1", "60", 4));

            Assert.Equal("range must be greater than 10 LY", ex.Message);
        }

        [Fact]
        public async Task StartNeutronRouteAsync_NonSuccessWithUnparsableBody_ThrowsGenericMessage()
        {
            var (service, handler) = Create();
            handler.Respond = _ => (HttpStatusCode.InternalServerError, "oops");

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartNeutronRouteAsync("Sol", "Sirius", "50", "60", 4));

            Assert.Contains("500", ex.Message);
        }

        [Fact]
        public async Task GetNeutronJobResultAsync_StatePending_ReturnsPending()
        {
            var (service, handler) = Create();
            handler.Respond = _ => (HttpStatusCode.OK, """{"job":"abc-123","state":"started","status":"queued"}""");

            var status = await service.GetNeutronJobResultAsync("abc-123");

            Assert.Equal(SpanshJobState.Pending, status.State);
            Assert.Equal("started", status.StatusText);
        }

        // Real response shape confirmed live via curl - unlike the fleet-carrier result's own
        // result.jumps ({"name": ..., "id64": ...}), the neutron result nests waypoints under
        // result.system_jumps, each carrying its own name under "system" (not "name").
        [Fact]
        public async Task GetNeutronJobResultAsync_StateCompletedStatusOk_ReturnsWaypoints()
        {
            var (service, handler) = Create();
            handler.Respond = _ => (HttpStatusCode.OK, """
                {
                  "job": "abc-123",
                  "state": "completed",
                  "status": "ok",
                  "result": {
                    "system_jumps": [
                      { "id64": 10477373803, "system": "Sol", "x": 0, "y": 0, "z": 0, "jumps": 0, "neutron_star": false },
                      { "id64": 121569805492, "system": "Sirius", "x": 6.25, "y": -1.28125, "z": -5.75, "jumps": 1, "neutron_star": false }
                    ]
                  }
                }
                """);

            var status = await service.GetNeutronJobResultAsync("abc-123");

            Assert.Equal(SpanshJobState.Completed, status.State);
            Assert.Equal(2, status.Jumps.Count);
            Assert.Equal(new SpanshRouteJump(10477373803, "Sol", 0.0, 0.0, 0.0), status.Jumps[0]);
            Assert.Equal(new SpanshRouteJump(121569805492, "Sirius", 6.25, -1.28125, -5.75), status.Jumps[1]);
        }

        [Fact]
        public async Task GetNeutronJobResultAsync_StateCompletedStatusNotOk_ReturnsFailed()
        {
            var (service, handler) = Create();
            handler.Respond = _ => (HttpStatusCode.OK, """{"job":"abc-123","state":"completed","status":"error"}""");

            var status = await service.GetNeutronJobResultAsync("abc-123");

            Assert.Equal(SpanshJobState.Failed, status.State);
            Assert.Contains("error", status.FailureReason);
        }

        [Fact]
        public async Task GetNeutronJobResultAsync_NonSuccessHttpStatus_ReturnsFailed()
        {
            var (service, handler) = Create();
            handler.Respond = _ => (HttpStatusCode.NotFound, "");

            var status = await service.GetNeutronJobResultAsync("abc-123");

            Assert.Equal(SpanshJobState.Failed, status.State);
        }
    }
}
