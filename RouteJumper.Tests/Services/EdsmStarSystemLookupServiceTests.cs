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
            var config = new AppConfigStore(dir.Path);
            var attemptStore = new EdsmLookupAttemptStore(dir.Path);
            var handler = new FakeHttpMessageHandler();
            var service = new EdsmStarSystemLookupService(settings, config, attemptStore, new HttpClient(handler));
            return (service, handler);
        }

        /// <summary>Same as Create, but reusing a caller-supplied AppConfigStore/EdsmLookupAttemptStore for the new service instance - simulates "the app restarted": a fresh EdsmStarSystemLookupService (no in-memory cache/session-unresolved-set of its own) against the same on-disk state, the same way SeedCoordinates_EventuallyPersistsToDatabase_VisibleFromAFreshServiceInstance already simulates a restart for the resolved-value cache.</summary>
        private static (EdsmStarSystemLookupService Service, FakeHttpMessageHandler Handler) CreateFresh(
            TempDirectory dir, AppConfigStore config, EdsmLookupAttemptStore attemptStore)
        {
            var settings = new AppSettingsStore(dir.Path);
            var handler = new FakeHttpMessageHandler();
            var service = new EdsmStarSystemLookupService(settings, config, attemptStore, new HttpClient(handler));
            return (service, handler);
        }

        [Fact]
        public async Task GetCoordinatesAsync_RequestsExpectedUrlWithArrayParamsShowCoordinatesAndShowPrimaryStar()
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
            Assert.Contains("showPrimaryStar=1", url);
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
        public async Task GetCoordinatesAsync_HttpFailure_IsRetriedOnNextCall_NotRememberedAsUnresolved()
        {
            // A transient HTTP-level failure carries no information about whether EDSM actually
            // has the data, unlike a genuinely successful response that simply omits the system -
            // so it must never be treated as "EDSM confirmed no record" and skipped next time.
            using var dir = new TempDirectory();
            var (service, handler) = Create(dir);
            handler.Respond = _ => (HttpStatusCode.InternalServerError, string.Empty);

            await service.GetCoordinatesAsync(new[] { "Sol" });
            await service.GetCoordinatesAsync(new[] { "Sol" });

            Assert.Equal(2, handler.RequestedUrls.Count);
        }

        [Fact]
        public async Task GetCoordinatesAsync_UnresolvedName_NotRetriedWithinSameSession()
        {
            using var dir = new TempDirectory();
            var (service, handler) = Create(dir);
            handler.Respond = _ => (HttpStatusCode.OK, "[]"); // EDSM has no record of "Sol"

            await service.GetCoordinatesAsync(new[] { "Sol" });
            await service.GetCoordinatesAsync(new[] { "Sol" });

            // EDSM already confirmed it has no record - not asked again this session.
            Assert.Single(handler.RequestedUrls);
        }

        [Fact]
        public async Task GetCoordinatesAsync_ResponseIncludesPrimaryStar_AlsoResolvesStarTypeWithoutFurtherHttpCall()
        {
            // The whole point of merging the two lookups (showCoordinates=1&showPrimaryStar=1 in
            // one request) - a coordinates fetch that happens to name a primary star type should
            // leave GetMainStarTypeAsync for the same system with nothing left to fetch.
            using var dir = new TempDirectory();
            var (service, handler) = Create(dir);
            handler.Respond = _ => (HttpStatusCode.OK,
                """[{"name":"Sol","coords":{"x":0,"y":0,"z":0},"primaryStar":{"type":"G (White-Yellow) Star"}}]""");

            var coords = await service.GetCoordinatesAsync(new[] { "Sol" });
            Assert.Single(handler.RequestedUrls);

            var starType = await service.GetMainStarTypeAsync("Sol");

            Assert.Equal(new GalacticCoordinates(0, 0, 0), coords["Sol"]);
            Assert.Equal("G (White-Yellow)", starType); // redundant "Star" word stripped
            Assert.Single(handler.RequestedUrls); // no separate star-type request was needed
        }

        [Fact]
        public async Task GetCoordinatesAsync_PersistedRecentUnresolvedAttempt_SkipsHttpCallEvenOnFreshInstance()
        {
            using var dir = new TempDirectory();
            var config = new AppConfigStore(dir.Path);
            var attemptStore = new EdsmLookupAttemptStore(dir.Path);
            attemptStore.SetLastAttemptUtc(EdsmStarSystemLookupService.CoordsKind, "SOL", DateTime.UtcNow.AddHours(-1));

            var (service, handler) = CreateFresh(dir, config, attemptStore);
            var result = await service.GetCoordinatesAsync(new[] { "Sol" });

            Assert.Null(result["Sol"]);
            Assert.Empty(handler.RequestedUrls);
        }

        [Fact]
        public async Task GetCoordinatesAsync_PersistedUnresolvedAttemptOlderThanRetryWindow_IsRetried()
        {
            using var dir = new TempDirectory();
            var config = new AppConfigStore(dir.Path);
            var attemptStore = new EdsmLookupAttemptStore(dir.Path);
            attemptStore.SetLastAttemptUtc(EdsmStarSystemLookupService.CoordsKind, "SOL", DateTime.UtcNow.AddHours(-13)); // past the default 12h cooldown

            var (service, handler) = CreateFresh(dir, config, attemptStore);
            handler.Respond = _ => (HttpStatusCode.OK, "[]");

            await service.GetCoordinatesAsync(new[] { "Sol" });

            Assert.Single(handler.RequestedUrls);
        }

        [Fact]
        public async Task GetCoordinatesAsync_UnresolvedName_PersistsAttemptTimestamp_VisibleFromAFreshAttemptStore()
        {
            using var dir = new TempDirectory();
            var (service, _) = Create(dir);

            await service.GetCoordinatesAsync(new[] { "Sol" });
            await service.WaitForPendingPersistAsync();

            var attemptStore = new EdsmLookupAttemptStore(dir.Path);
            var lastAttempt = attemptStore.GetLastAttemptUtc(EdsmStarSystemLookupService.CoordsKind, "SOL");

            Assert.NotNull(lastAttempt);
            Assert.True(DateTime.UtcNow - lastAttempt.Value < TimeSpan.FromMinutes(1));
        }

        [Fact]
        public async Task GetCoordinatesAsync_LaterResolvedViaSeed_ClearsThePersistedUnresolvedAttempt()
        {
            using var dir = new TempDirectory();
            var (service, _) = Create(dir);

            await service.GetCoordinatesAsync(new[] { "Sol" }); // confirmed unresolved, persisted
            await service.WaitForPendingPersistAsync();

            service.SeedCoordinates("Sol", new GalacticCoordinates(1, 2, 3));
            await service.WaitForPendingPersistAsync();

            var attemptStore = new EdsmLookupAttemptStore(dir.Path);
            Assert.Null(attemptStore.GetLastAttemptUtc(EdsmStarSystemLookupService.CoordsKind, "SOL"));
        }

        [Fact]
        public async Task GetMainStarTypeAsync_EdsmSubTypeEndsInStar_DropsTheRedundantWord()
        {
            // EDSM's own "type" always ends in a literal "Star" word - the Route tab's own
            // column is already headed "Star Type" (§4.2), so repeating it in every cell is
            // redundant and is stripped before caching/returning.
            using var dir = new TempDirectory();
            var (service, handler) = Create(dir);
            handler.Respond = _ => (HttpStatusCode.OK, """[{"name":"Sol","primaryStar":{"type":"K (Yellow-Orange) Star"}}]""");

            var result = await service.GetMainStarTypeAsync("Sol");

            Assert.Equal("K (Yellow-Orange)", result);
        }

        [Fact]
        public async Task GetMainStarTypeAsync_NoPrimaryStarInResponse_ReturnsNull()
        {
            using var dir = new TempDirectory();
            var (service, handler) = Create(dir);
            handler.Respond = _ => (HttpStatusCode.OK, """[{"name":"Sol","coords":{"x":0,"y":0,"z":0}}]""");

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
            handler.Respond = _ => (HttpStatusCode.OK, """[{"name":"Sol","primaryStar":{"type":"G (White-Yellow) Star"}}]""");

            await service.GetMainStarTypeAsync("Sol");
            Assert.Single(handler.RequestedUrls);

            var second = await service.GetMainStarTypeAsync("Sol");

            Assert.Single(handler.RequestedUrls);
            Assert.Equal("G (White-Yellow)", second);
        }

        [Fact]
        public async Task GetMainStarTypeAsync_UnresolvedName_NotRetriedWithinSameSession()
        {
            using var dir = new TempDirectory();
            var (service, handler) = Create(dir);
            handler.Respond = _ => (HttpStatusCode.OK, "[]");

            await service.GetMainStarTypeAsync("Sol");
            await service.GetMainStarTypeAsync("Sol");

            Assert.Single(handler.RequestedUrls);
        }

        [Fact]
        public async Task GetMainStarTypeAsync_PersistedRecentUnresolvedAttempt_SkipsHttpCallEvenOnFreshInstance()
        {
            using var dir = new TempDirectory();
            var config = new AppConfigStore(dir.Path);
            var attemptStore = new EdsmLookupAttemptStore(dir.Path);
            attemptStore.SetLastAttemptUtc(EdsmStarSystemLookupService.StarTypeKind, "SOL", DateTime.UtcNow.AddHours(-1));

            var (service, handler) = CreateFresh(dir, config, attemptStore);
            var result = await service.GetMainStarTypeAsync("Sol");

            Assert.Null(result);
            Assert.Empty(handler.RequestedUrls);
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

        [Fact]
        public void SeedSystemAddress_ThenTryGetCachedSystemAddress_ReturnsSeededValue()
        {
            using var dir = new TempDirectory();
            var (service, _) = Create(dir);

            service.SeedSystemAddress("Sol", 10477373803);

            Assert.True(service.TryGetCachedSystemAddress("Sol", out var systemAddress));
            Assert.Equal(10477373803, systemAddress);
        }

        [Fact]
        public void TryGetCachedSystemAddress_NeverSeeded_ReturnsFalse()
        {
            using var dir = new TempDirectory();
            var (service, _) = Create(dir);

            Assert.False(service.TryGetCachedSystemAddress("Sol", out var systemAddress));
            Assert.Null(systemAddress);
        }

        [Fact]
        public void SeedSystemAddress_CalledTwice_OverwritesWithLatestValue()
        {
            using var dir = new TempDirectory();
            var (service, _) = Create(dir);

            service.SeedSystemAddress("Sol", 1);
            service.SeedSystemAddress("Sol", 2);

            Assert.True(service.TryGetCachedSystemAddress("Sol", out var systemAddress));
            Assert.Equal(2, systemAddress);
        }

        [Fact]
        public void SeedSystemAddress_RaisesDataSeeded()
        {
            using var dir = new TempDirectory();
            var (service, _) = Create(dir);
            var raised = 0;
            service.DataSeeded += (_, _) => raised++;

            service.SeedSystemAddress("Sol", 10477373803);

            Assert.Equal(1, raised);
        }

        [Fact]
        public async Task SeedSystemAddress_EventuallyPersistsToDatabase_VisibleFromAFreshServiceInstance()
        {
            using var dir = new TempDirectory();
            var (service, _) = Create(dir);

            service.SeedSystemAddress("Sol", 10477373803);
            await service.WaitForPendingPersistAsync();

            var (freshService, _) = Create(dir);
            Assert.True(freshService.TryGetCachedSystemAddress("Sol", out var systemAddress));
            Assert.Equal(10477373803, systemAddress);
        }
    }
}
