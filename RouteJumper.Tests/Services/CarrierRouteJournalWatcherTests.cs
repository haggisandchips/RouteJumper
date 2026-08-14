using System.IO;
using RouteJumper.Models;
using RouteJumper.Sequencing;
using RouteJumper.Services;
using RouteJumper.Tests.TestSupport;
using Xunit;

namespace RouteJumper.Tests.Services
{
    /// <summary>
    /// Exercises CarrierRouteJournalWatcher's one-off historical catch-up (ComputeCatchUpState,
    /// reached via StartAsync) against real temp journal files. Real-time scheduling (Jumping
    /// fired minutes before a *future* DepartureTime, Cooldown clearing 4 minutes after a live
    /// arrival) is not exercised here - it would require either waiting in real time or injecting
    /// a fake clock, neither of which this class currently supports; every timestamp below is in
    /// the past, so any derived transition fires immediately/synchronously during StartAsync.
    /// </summary>
    public class CarrierRouteJournalWatcherTests
    {
        private const long CarrierId = 12345;

        private sealed record CapturedEvent(RowEventKind Kind, string SystemName, bool IsLive, DateTime? PhaseEndUtc);

        private static async Task<List<CapturedEvent>> RunCatchUpAsync(TempDirectory dir, JournalFile journal)
        {
            var path = dir.CombinePath("Journal.Test.01.log");
            journal.WriteTo(path);

            var events = new List<CapturedEvent>();
            var statsCount = 0;
            using var watcher = new CarrierRouteJournalWatcher(
                path,
                CarrierId,
                (kind, systemName, isLive, phaseEndUtc) => events.Add(new CapturedEvent(kind, systemName, isLive, phaseEndUtc)),
                () => statsCount++);

            await watcher.StartAsync();

            return events;
        }

        [Fact]
        public async Task StartAsync_OpenJumpRequestWithPastDepartureTime_FiresPlottedThenJumpingImmediately()
        {
            using var dir = new TempDirectory();
            var journal = new JournalFile()
                .CarrierJumpRequest(CarrierId, "Deciat", DateTime.UtcNow.AddMinutes(-10));

            var events = await RunCatchUpAsync(dir, journal);

            Assert.Contains(events, e => e.Kind == RowEventKind.Plotted && e.SystemName == "Deciat" && !e.IsLive);
            Assert.Contains(events, e => e.Kind == RowEventKind.Jumping && e.SystemName == "Deciat" && !e.IsLive);
        }

        [Fact]
        public async Task StartAsync_MostRecentLocationWinsOverEarlierRequest_FiresArrivedNotPlotted()
        {
            using var dir = new TempDirectory();
            var journal = new JournalFile()
                .CarrierJumpRequest(CarrierId, "Deciat", DateTime.UtcNow.AddMinutes(-30))
                .CarrierLocation(CarrierId, "Deciat");

            var events = await RunCatchUpAsync(dir, journal);

            Assert.Contains(events, e => e.Kind == RowEventKind.Arrived && e.SystemName == "Deciat" && !e.IsLive);
            Assert.DoesNotContain(events, e => e.Kind == RowEventKind.Plotted);
        }

        [Fact]
        public async Task StartAsync_CatchUp_NeverMarksCatchUpArrivalAsLive()
        {
            using var dir = new TempDirectory();
            var journal = new JournalFile().CarrierLocation(CarrierId, "Sol");

            var events = await RunCatchUpAsync(dir, journal);

            var arrived = Assert.Single(events, e => e.Kind == RowEventKind.Arrived);
            Assert.False(arrived.IsLive);
        }

        [Fact]
        public async Task StartAsync_CancelledRequestFollowedByNothing_ResolvesToRequestStillCancelled()
        {
            using var dir = new TempDirectory();
            var journal = new JournalFile()
                .CarrierJumpRequest(CarrierId, "Deciat", DateTime.UtcNow.AddMinutes(10))
                .CarrierJumpCancelled(CarrierId);

            var events = await RunCatchUpAsync(dir, journal);

            // A cancelled request resolves the "requestStillOpen" state - nothing plotted, and
            // there's no prior location either, so nothing should fire at all.
            Assert.Empty(events);
        }

        [Fact]
        public async Task StartAsync_RequestAfterCancellation_IsTheOneApplied()
        {
            using var dir = new TempDirectory();
            var journal = new JournalFile()
                .CarrierJumpRequest(CarrierId, "First", DateTime.UtcNow.AddMinutes(-20))
                .CarrierJumpCancelled(CarrierId)
                .CarrierJumpRequest(CarrierId, "Second", DateTime.UtcNow.AddMinutes(-10));

            var events = await RunCatchUpAsync(dir, journal);

            Assert.Contains(events, e => e.Kind == RowEventKind.Plotted && e.SystemName == "Second");
            Assert.DoesNotContain(events, e => e.SystemName == "First");
        }

        [Fact]
        public async Task StartAsync_SquadronCarrierEvents_AreIgnored()
        {
            using var dir = new TempDirectory();
            var journal = new JournalFile()
                .CarrierJumpRequest(CarrierId, "NotMine", DateTime.UtcNow.AddMinutes(-5), carrierType: "SquadronCarrier");

            var events = await RunCatchUpAsync(dir, journal);

            Assert.Empty(events);
        }

        [Fact]
        public async Task StartAsync_DifferentCarrierId_IsIgnored()
        {
            using var dir = new TempDirectory();
            var journal = new JournalFile()
                .CarrierJumpRequest(CarrierId + 1, "NotMine", DateTime.UtcNow.AddMinutes(-5));

            var events = await RunCatchUpAsync(dir, journal);

            Assert.Empty(events);
        }

        [Fact]
        public async Task StartAsync_EmptyJournal_FiresNoEvents()
        {
            using var dir = new TempDirectory();
            var events = await RunCatchUpAsync(dir, new JournalFile());

            Assert.Empty(events);
        }

        [Fact]
        public async Task StartAsync_RevisitedSystem_LatestLocationLineWins()
        {
            // The whole point of computing catch-up state from line order rather than replaying
            // line-by-line: a carrier that visited "Sol" twice must resolve to wherever it is
            // *now* (its last-seen location), not complete "Sol" on the first sighting.
            using var dir = new TempDirectory();
            var journal = new JournalFile()
                .CarrierLocation(CarrierId, "Sol")
                .CarrierLocation(CarrierId, "Deciat")
                .CarrierLocation(CarrierId, "Sol");

            var events = await RunCatchUpAsync(dir, journal);

            var arrived = Assert.Single(events, e => e.Kind == RowEventKind.Arrived);
            Assert.Equal("Sol", arrived.SystemName);
        }

        // ===================== FSDTarget-driven cache seeding (Captain's own ship) =====================

        private static (CarrierRouteJournalWatcher Watcher, List<CapturedEvent> Events) CreateLive(
            string journalPath = "unused", IStarSystemLookupService? starSystemLookupService = null)
        {
            var events = new List<CapturedEvent>();
            var watcher = new CarrierRouteJournalWatcher(
                journalPath,
                CarrierId,
                (kind, systemName, isLive, phaseEndUtc) => events.Add(new CapturedEvent(kind, systemName, isLive, phaseEndUtc)),
                () => { },
                starSystemLookupService);
            return (watcher, events);
        }

        [Fact]
        public async Task ProcessLine_LiveFsdTargetWithStarClass_SeedsStarTypeWithoutFiringAnyRowEvent()
        {
            // Unlike Ship mode's own FSDTarget handling, the Captain's own ship targeting a jump
            // is not itself route-relevant here (carrier progress is driven entirely by Carrier*
            // events) - only the opportunistic cache seed should happen, no RowEvent at all.
            var fake = new FakeStarSystemLookupService();
            var (watcher, events) = CreateLive(starSystemLookupService: fake);
            using var _w = watcher;

            watcher.ProcessLine(
                "{\"timestamp\":\"" + JournalFile.TimestampOf(DateTime.UtcNow) + "\",\"event\":\"FSDTarget\",\"Name\":\"Deciat\",\"StarClass\":\"K\"}",
                isLive: true);
            // Cache seeding is deliberately backgrounded (see ProcessLine's own comment) so it
            // never delays processing the next journal line - tests wait for it explicitly instead
            // of racing a fire-and-forget Task.Run.
            await watcher.WaitForPendingCacheSeedAsync();

            Assert.Equal("K (Yellow-Orange) Star", fake.StarTypes["Deciat"]);
            Assert.Empty(events);
        }

        [Fact]
        public async Task ProcessLine_LiveFsdTargetWithRemainingJumpsInRoute_SeedsFromNavRouteJson()
        {
            using var dir = new TempDirectory();
            var journalPath = dir.CombinePath("Journal.Test.01.log");
            File.WriteAllText(dir.CombinePath("NavRoute.json"), """
                {
                    "timestamp": "2026-01-01T00:00:00Z",
                    "event": "NavRoute",
                    "Route": [
                        { "StarSystem": "Sol", "SystemAddress": 10477373803, "StarPos": [0.0, 0.0, 0.0], "StarClass": "G" },
                        { "StarSystem": "Deciat", "SystemAddress": 1, "StarPos": [3.0, 4.0, 0.0], "StarClass": "K" }
                    ]
                }
                """);
            var fake = new FakeStarSystemLookupService();
            var (watcher, _) = CreateLive(journalPath, fake);
            using var _w = watcher;

            watcher.ProcessLine(
                "{\"timestamp\":\"" + JournalFile.TimestampOf(DateTime.UtcNow) + "\",\"event\":\"FSDTarget\",\"Name\":\"Sol\",\"RemainingJumpsInRoute\":2}",
                isLive: true);
            await watcher.WaitForPendingCacheSeedAsync();

            Assert.Equal(new GalacticCoordinates(0, 0, 0), fake.Coordinates["Sol"]);
            Assert.Equal(new GalacticCoordinates(3, 4, 0), fake.Coordinates["Deciat"]);
        }

        [Fact]
        public async Task ProcessLine_LiveFsdTargetWithNoLookupServiceSupplied_DoesNotThrow()
        {
            var (watcher, events) = CreateLive(); // starSystemLookupService intentionally omitted

            watcher.ProcessLine(
                "{\"timestamp\":\"" + JournalFile.TimestampOf(DateTime.UtcNow) + "\",\"event\":\"FSDTarget\",\"Name\":\"Sol\",\"StarClass\":\"G\",\"RemainingJumpsInRoute\":2}",
                isLive: true);
            await watcher.WaitForPendingCacheSeedAsync();

            Assert.Empty(events);
        }

        [Fact]
        public async Task ProcessLine_LiveNavRouteEvent_SeedsFromNavRouteJsonWithoutFiringAnyRowEvent()
        {
            using var dir = new TempDirectory();
            var journalPath = dir.CombinePath("Journal.Test.01.log");
            File.WriteAllText(dir.CombinePath("NavRoute.json"), """
                {
                    "timestamp": "2026-01-01T00:00:00Z",
                    "event": "NavRoute",
                    "Route": [
                        { "StarSystem": "Sol", "SystemAddress": 10477373803, "StarPos": [0.0, 0.0, 0.0], "StarClass": "G" },
                        { "StarSystem": "Deciat", "SystemAddress": 1, "StarPos": [3.0, 4.0, 0.0], "StarClass": "K" }
                    ]
                }
                """);
            var fake = new FakeStarSystemLookupService();
            var (watcher, events) = CreateLive(journalPath, fake);
            using var _w = watcher;

            watcher.ProcessLine(
                "{\"timestamp\":\"" + JournalFile.TimestampOf(DateTime.UtcNow) + "\",\"event\":\"NavRoute\"}",
                isLive: true);
            await watcher.WaitForPendingCacheSeedAsync();

            Assert.Equal(new GalacticCoordinates(0, 0, 0), fake.Coordinates["Sol"]);
            Assert.Equal(new GalacticCoordinates(3, 4, 0), fake.Coordinates["Deciat"]);
            Assert.Empty(events);
        }

        [Fact]
        public async Task ProcessLine_LiveNavRouteEventWithNoLookupServiceSupplied_DoesNotThrow()
        {
            var (watcher, events) = CreateLive(); // starSystemLookupService intentionally omitted

            watcher.ProcessLine(
                "{\"timestamp\":\"" + JournalFile.TimestampOf(DateTime.UtcNow) + "\",\"event\":\"NavRoute\"}",
                isLive: true);
            await watcher.WaitForPendingCacheSeedAsync();

            Assert.Empty(events);
        }

        [Fact]
        public void ProcessLine_LiveNavRouteClear_FiresTargetCleared()
        {
            // The Captain's own ship explicitly clearing its plotted route - carries no
            // CarrierID/CarrierType, same as FSDTarget/NavRoute above.
            var (watcher, events) = CreateLive();
            using var _w = watcher;

            watcher.ProcessLine(
                "{\"timestamp\":\"" + JournalFile.TimestampOf(DateTime.UtcNow) + "\",\"event\":\"NavRouteClear\"}",
                isLive: true);

            var single = Assert.Single(events);
            Assert.Equal(RowEventKind.TargetCleared, single.Kind);
            Assert.True(single.IsLive);
        }
    }
}
