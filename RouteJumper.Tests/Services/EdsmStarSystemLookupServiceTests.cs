using System.Net;
using System.Net.Http;
using RouteJumper.Models;
using RouteJumper.Services;
using RouteJumper.Tests.TestSupport;
using Xunit;

namespace RouteJumper.Tests.Services
{
    public class EdsmStarSystemLookupServiceTests
    {
        private static (EdsmStarSystemLookupService Service, FakeHttpMessageHandler Handler) Create(TempDirectory dir)
        {
            var settings = new AppSettingsStore(dir.Path);
            var handler = new FakeHttpMessageHandler();
            var service = new EdsmStarSystemLookupService(settings, new HttpClient(handler));
            return (service, handler);
        }

        [Fact]
        public async Task GetCoordinatesAsync_RequestsExpectedUrlWithArrayParamsAndShowCoordinates()
        {
            using var dir = new TempDirectory();
            var (service, handler) = Create(dir);
            handler.Respond = _ => (HttpStatusCode.OK, "[]");

            await service.GetCoordinatesAsync(new[] { "Sol", "Alpha Centauri" });

            var url = Assert.Single(handler.RequestedUrls);
            Assert.Contains("/api-v1/systems?", url);
            Assert.Contains("systemName[]=Sol", url);
            Assert.Contains("systemName[]=Alpha%20Centauri", url);
            Assert.Contains("showCoordinates=1", url);
        }

        [Fact]
        public async Task GetCoordinatesAsync_ResolvedName_ReturnsCoordinates()
        {
            using var dir = new TempDirectory();
            var (service, handler) = Create(dir);
            handler.Respond = _ => (HttpStatusCode.OK, """[{"name":"Sol","coords":{"x":0,"y":0,"z":0},"coordsLocked":true}]""");

            var result = await service.GetCoordinatesAsync(new[] { "Sol" });

            Assert.Equal(new GalacticCoordinates(0, 0, 0), result["Sol"]);
        }

        [Fact]
        public async Task GetCoordinatesAsync_NameAbsentFromResponse_ResolvesToNullNotException()
        {
            using var dir = new TempDirectory();
            var (service, handler) = Create(dir);
            // EDSM omits unresolvable names entirely rather than returning a null/empty entry.
            handler.Respond = _ => (HttpStatusCode.OK, """[{"name":"Sol","coords":{"x":0,"y":0,"z":0}}]""");

            var result = await service.GetCoordinatesAsync(new[] { "Sol", "ThisSystemDoesNotExistXYZ" });

            Assert.Equal(new GalacticCoordinates(0, 0, 0), result["Sol"]);
            Assert.Null(result["ThisSystemDoesNotExistXYZ"]);
        }

        [Fact]
        public async Task GetCoordinatesAsync_MoreNamesThanBatchSize_ChunksIntoMultipleRequests()
        {
            using var dir = new TempDirectory();
            var (service, handler) = Create(dir);
            handler.Respond = _ => (HttpStatusCode.OK, "[]");

            var names = Enumerable.Range(1, EdsmStarSystemLookupService.CoordinatesBatchSize + 3)
                .Select(i => $"System {i}")
                .ToList();

            await service.GetCoordinatesAsync(names);

            Assert.Equal(2, handler.RequestedUrls.Count);
        }

        [Fact]
        public async Task GetCoordinatesAsync_SecondCallForCachedName_MakesNoFurtherHttpCall()
        {
            using var dir = new TempDirectory();
            var (service, handler) = Create(dir);
            handler.Respond = _ => (HttpStatusCode.OK, """[{"name":"Sol","coords":{"x":0,"y":0,"z":0}}]""");

            await service.GetCoordinatesAsync(new[] { "Sol" });
            Assert.Single(handler.RequestedUrls);

            var result = await service.GetCoordinatesAsync(new[] { "Sol" });

            Assert.Single(handler.RequestedUrls); // still just the one request from the first call
            Assert.Equal(new GalacticCoordinates(0, 0, 0), result["Sol"]);
        }

        [Fact]
        public async Task GetCoordinatesAsync_HttpFailure_DegradesToUnresolvedWithoutThrowing()
        {
            using var dir = new TempDirectory();
            var (service, handler) = Create(dir);
            handler.Respond = _ => (HttpStatusCode.InternalServerError, string.Empty);

            var result = await service.GetCoordinatesAsync(new[] { "Sol" });

            Assert.Null(result["Sol"]);
        }

        [Fact]
        public async Task GetCoordinatesAsync_UnresolvedName_IsNotCached()
        {
            using var dir = new TempDirectory();
            var (service, handler) = Create(dir);
            handler.Respond = _ => (HttpStatusCode.OK, "[]"); // "Sol" never resolved

            await service.GetCoordinatesAsync(new[] { "Sol" });
            await service.GetCoordinatesAsync(new[] { "Sol" });

            // Retried both times, rather than a miss being permanently remembered as unknown.
            Assert.Equal(2, handler.RequestedUrls.Count);
        }

        [Fact]
        public async Task GetMainStarTypeAsync_PrefersMainStarOverOtherStars()
        {
            using var dir = new TempDirectory();
            var (service, handler) = Create(dir);
            handler.Respond = _ => (HttpStatusCode.OK, """
                {"name":"Sol","bodies":[
                    {"name":"Sol B","type":"Star","subType":"M (Red dwarf) Star","isMainStar":false},
                    {"name":"Sol","type":"Star","subType":"G (White-Yellow) Star","isMainStar":true}
                ]}
                """);

            var result = await service.GetMainStarTypeAsync("Sol");

            Assert.Equal("G (White-Yellow)", result);
        }

        [Fact]
        public async Task GetMainStarTypeAsync_NoStarFlaggedMain_FallsBackToFirstStar()
        {
            using var dir = new TempDirectory();
            var (service, handler) = Create(dir);
            handler.Respond = _ => (HttpStatusCode.OK, """
                {"name":"Sol","bodies":[
                    {"name":"Sol","type":"Star","subType":"G (White-Yellow) Star","isMainStar":false},
                    {"name":"Earth","type":"Planet","subType":"Earthlike body"}
                ]}
                """);

            var result = await service.GetMainStarTypeAsync("Sol");

            Assert.Equal("G (White-Yellow)", result);
        }

        [Fact]
        public async Task GetMainStarTypeAsync_EdsmSubTypeEndsInStar_DropsTheRedundantWord()
        {
            // EDSM's own "subType" always ends in a literal "Star" word - the Route tab's own
            // column is already headed "Star Type" (§4.2), so repeating it in every cell is
            // redundant and is stripped before caching/returning.
            using var dir = new TempDirectory();
            var (service, handler) = Create(dir);
            handler.Respond = _ => (HttpStatusCode.OK, """{"name":"Sol","bodies":[{"type":"Star","subType":"K (Yellow-Orange) Star","isMainStar":true}]}""");

            var result = await service.GetMainStarTypeAsync("Sol");

            Assert.Equal("K (Yellow-Orange)", result);
        }

        [Fact]
        public async Task GetMainStarTypeAsync_NoStarsAtAll_ReturnsNull()
        {
            using var dir = new TempDirectory();
            var (service, handler) = Create(dir);
            handler.Respond = _ => (HttpStatusCode.OK, """{"name":"Sol","bodies":[]}""");

            Assert.Null(await service.GetMainStarTypeAsync("Sol"));
        }

        [Fact]
        public async Task GetMainStarTypeAsync_SystemNotFound_ReturnsNull()
        {
            using var dir = new TempDirectory();
            var (service, handler) = Create(dir);
            handler.Respond = _ => (HttpStatusCode.NotFound, string.Empty);

            Assert.Null(await service.GetMainStarTypeAsync("Unknown System"));
        }

        [Fact]
        public async Task GetMainStarTypeAsync_ResolvedResult_IsCachedForSecondCall()
        {
            using var dir = new TempDirectory();
            var (service, handler) = Create(dir);
            handler.Respond = _ => (HttpStatusCode.OK, """{"name":"Sol","bodies":[{"type":"Star","subType":"G (White-Yellow) Star","isMainStar":true}]}""");

            await service.GetMainStarTypeAsync("Sol");
            Assert.Single(handler.RequestedUrls);

            var second = await service.GetMainStarTypeAsync("Sol");

            Assert.Single(handler.RequestedUrls);
            Assert.Equal("G (White-Yellow)", second);
        }

        [Fact]
        public async Task SeedCoordinates_ThenGetCoordinatesAsync_ReturnsSeededValueWithoutHttpCall()
        {
            using var dir = new TempDirectory();
            var (service, handler) = Create(dir);

            service.SeedCoordinates("Sol", new GalacticCoordinates(1, 2, 3));
            var result = await service.GetCoordinatesAsync(new[] { "Sol" });

            Assert.Equal(new GalacticCoordinates(1, 2, 3), result["Sol"]);
            Assert.Empty(handler.RequestedUrls);
        }

        [Fact]
        public async Task SeedStarType_ThenGetMainStarTypeAsync_ReturnsSeededValueWithoutHttpCall()
        {
            using var dir = new TempDirectory();
            var (service, handler) = Create(dir);

            service.SeedStarType("Sol", "G (White-Yellow) Star");
            var result = await service.GetMainStarTypeAsync("Sol");

            Assert.Equal("G (White-Yellow) Star", result);
            Assert.Empty(handler.RequestedUrls);
        }

        [Fact]
        public async Task SeedCoordinates_CalledTwice_OverwritesWithLatestValue()
        {
            using var dir = new TempDirectory();
            var (service, _) = Create(dir);
            service.SeedCoordinates("Sol", new GalacticCoordinates(1, 1, 1));

            service.SeedCoordinates("Sol", new GalacticCoordinates(2, 2, 2));
            var result = await service.GetCoordinatesAsync(new[] { "Sol" });

            Assert.Equal(new GalacticCoordinates(2, 2, 2), result["Sol"]);
        }

        [Fact]
        public void SeedCoordinates_RaisesDataSeeded()
        {
            using var dir = new TempDirectory();
            var (service, _) = Create(dir);
            var raised = 0;
            service.DataSeeded += (_, _) => raised++;

            service.SeedCoordinates("Sol", new GalacticCoordinates(1, 1, 1));

            Assert.Equal(1, raised);
        }

        [Fact]
        public void SeedStarType_RaisesDataSeeded()
        {
            using var dir = new TempDirectory();
            var (service, _) = Create(dir);
            var raised = 0;
            service.DataSeeded += (_, _) => raised++;

            service.SeedStarType("Sol", "G (White-Yellow) Star");

            Assert.Equal(1, raised);
        }

        [Fact]
        public void SeedCoordinates_DataSeededFiresBeforeWaitingForPersist_ProvingItIsNotBlockedOnTheDbWrite()
        {
            // The whole point of decoupling persistence (EnqueuePersist) from the in-memory
            // cache update + notification: DataSeeded must be observable *before* anything awaits
            // the persist queue draining - if the write were still inline, this ordering wouldn't
            // be distinguishable from the write itself being what's slow.
            using var dir = new TempDirectory();
            var (service, _) = Create(dir);
            var raised = 0;
            service.DataSeeded += (_, _) => raised++;

            service.SeedCoordinates("Sol", new GalacticCoordinates(1, 2, 3));

            Assert.Equal(1, raised); // already fired - no persist was awaited
        }

        [Fact]
        public async Task SeedCoordinates_EventuallyPersistsToDatabase_VisibleFromAFreshServiceInstance()
        {
            // Proves the deferred write (EnqueuePersist) actually reaches disk, not just memory -
            // a second service instance against the same directory has no in-memory cache of its
            // own, so it can only see the value if it was genuinely persisted (mirrors a real app
            // restart, SPEC §7's "restored... indefinitely" cache row).
            using var dir = new TempDirectory();
            var (service, _) = Create(dir);

            service.SeedCoordinates("Sol", new GalacticCoordinates(1, 2, 3));
            await service.WaitForPendingPersistAsync();

            var (freshService, freshHandler) = Create(dir);
            var result = await freshService.GetCoordinatesAsync(new[] { "Sol" });

            Assert.Equal(new GalacticCoordinates(1, 2, 3), result["Sol"]);
            Assert.Empty(freshHandler.RequestedUrls); // resolved from disk, no network call needed
        }

        [Fact]
        public async Task SeedStarType_EventuallyPersistsToDatabase_VisibleFromAFreshServiceInstance()
        {
            using var dir = new TempDirectory();
            var (service, _) = Create(dir);

            service.SeedStarType("Sol", "G (White-Yellow) Star");
            await service.WaitForPendingPersistAsync();

            var (freshService, freshHandler) = Create(dir);
            var result = await freshService.GetMainStarTypeAsync("Sol");

            Assert.Equal("G (White-Yellow) Star", result);
            Assert.Empty(freshHandler.RequestedUrls);
        }
    }
}
