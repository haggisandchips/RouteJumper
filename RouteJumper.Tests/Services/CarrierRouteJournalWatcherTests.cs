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
    }
}
