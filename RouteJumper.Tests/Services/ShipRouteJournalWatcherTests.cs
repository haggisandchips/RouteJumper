using RouteJumper.Sequencing;
using RouteJumper.Services;
using RouteJumper.Tests.TestSupport;
using Xunit;

namespace RouteJumper.Tests.Services
{
    /// <summary>
    /// Exercises ShipRouteJournalWatcher's one-off historical catch-up (ComputeCatchUpState,
    /// reached via StartAsync) against real temp journal files, plus its live-tailed event
    /// mapping via direct ProcessLine(isLive: true) calls - real FileSystemWatcher timing isn't
    /// exercised, same scope limitation CarrierRouteJournalWatcherTests already documents for
    /// itself. CooldownElapsed's and the ArrivalSettleTimeout fallback's own real-time Timer
    /// scheduling likewise aren't exercised here for the same reason (no injectable clock); every
    /// timestamp below is in the past, so any derived catch-up transition fires
    /// immediately/synchronously.
    /// </summary>
    public class ShipRouteJournalWatcherTests
    {
        private sealed record CapturedEvent(RowEventKind Kind, string SystemName, bool IsLive, DateTime? PhaseEndUtc);

        private static async Task<List<CapturedEvent>> RunCatchUpAsync(TempDirectory dir, JournalFile journal)
        {
            var path = dir.CombinePath("Journal.Test.01.log");
            journal.WriteTo(path);

            var events = new List<CapturedEvent>();
            using var watcher = new ShipRouteJournalWatcher(
                path,
                (kind, systemName, isLive, phaseEndUtc) => events.Add(new CapturedEvent(kind, systemName, isLive, phaseEndUtc)));

            await watcher.StartAsync();

            return events;
        }

        private static (ShipRouteJournalWatcher Watcher, List<CapturedEvent> Events) CreateLive()
        {
            var events = new List<CapturedEvent>();
            var watcher = new ShipRouteJournalWatcher(
                "unused",
                (kind, systemName, isLive, phaseEndUtc) => events.Add(new CapturedEvent(kind, systemName, isLive, phaseEndUtc)));
            return (watcher, events);
        }

        // ===================== Catch-up (StartAsync) =====================

        [Fact]
        public async Task StartAsync_OpenTargetOnly_FiresTargeted()
        {
            using var dir = new TempDirectory();
            var journal = new JournalFile().FSDTarget("Deciat", DateTime.UtcNow.AddMinutes(-1));

            var events = await RunCatchUpAsync(dir, journal);

            var targeted = Assert.Single(events, e => e.Kind == RowEventKind.Targeted);
            Assert.Equal("Deciat", targeted.SystemName);
            Assert.False(targeted.IsLive);
        }

        [Fact]
        public async Task StartAsync_OpenHyperspaceJumpNotYetArrived_FiresJumpingOnly()
        {
            using var dir = new TempDirectory();
            var journal = new JournalFile()
                .FSDTarget("Deciat", DateTime.UtcNow.AddMinutes(-2))
                .StartJump("Hyperspace", "Deciat", DateTime.UtcNow.AddMinutes(-1));

            var events = await RunCatchUpAsync(dir, journal);

            var single = Assert.Single(events);
            Assert.Equal(RowEventKind.Jumping, single.Kind);
            Assert.Equal("Deciat", single.SystemName);
            Assert.False(single.IsLive);
        }

        [Fact]
        public async Task StartAsync_MatchingFsdJumpAfterStartJump_FiresArrivedOnly()
        {
            // Catch-up applies Arrived directly off FSDJump, unlike live processing - it only
            // needs "where is the ship right now," not the Music-confirmed settle timing that
            // matters for a live Cooldown transition (see ProcessLine's own tests below).
            using var dir = new TempDirectory();
            var journal = new JournalFile()
                .FSDTarget("Deciat", DateTime.UtcNow.AddMinutes(-3))
                .StartJump("Hyperspace", "Deciat", DateTime.UtcNow.AddMinutes(-2))
                .FSDJump("Deciat", DateTime.UtcNow.AddMinutes(-1));

            var events = await RunCatchUpAsync(dir, journal);

            var single = Assert.Single(events);
            Assert.Equal(RowEventKind.Arrived, single.Kind);
            Assert.Equal("Deciat", single.SystemName);
            Assert.False(single.IsLive);
        }

        [Fact]
        public async Task StartAsync_TargetSetAfterArrival_FiresTargetedForNewSystem()
        {
            // Whichever of the three relevant events came last in the file wins, regardless of
            // kind - here a fresh FSDTarget for the *next* hop is the latest word.
            using var dir = new TempDirectory();
            var journal = new JournalFile()
                .FSDJump("Deciat", DateTime.UtcNow.AddMinutes(-2))
                .FSDTarget("Sol", DateTime.UtcNow.AddMinutes(-1));

            var events = await RunCatchUpAsync(dir, journal);

            var single = Assert.Single(events);
            Assert.Equal(RowEventKind.Targeted, single.Kind);
            Assert.Equal("Sol", single.SystemName);
        }

        [Fact]
        public async Task StartAsync_CatchUp_NeverMarksCatchUpArrivalAsLive()
        {
            using var dir = new TempDirectory();
            var journal = new JournalFile().FSDJump("Sol");

            var events = await RunCatchUpAsync(dir, journal);

            var arrived = Assert.Single(events, e => e.Kind == RowEventKind.Arrived);
            Assert.False(arrived.IsLive);
        }

        [Fact]
        public async Task StartAsync_SuperchargeJumpType_IsIgnored()
        {
            using var dir = new TempDirectory();
            var journal = new JournalFile()
                .StartJump("Supercharge", null, DateTime.UtcNow.AddMinutes(-1));

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
        public async Task StartAsync_RevisitedSystem_LatestArrivalLineWins()
        {
            // Same rationale as CarrierRouteJournalWatcherTests' equivalent: a commander's real
            // journal may show the same system more than once in a session (e.g. a scooping
            // stop) - catch-up must resolve to the *last* seen arrival, not the first.
            using var dir = new TempDirectory();
            var journal = new JournalFile()
                .FSDJump("Sol")
                .FSDJump("Deciat")
                .FSDJump("Sol");

            var events = await RunCatchUpAsync(dir, journal);

            var arrived = Assert.Single(events, e => e.Kind == RowEventKind.Arrived);
            Assert.Equal("Sol", arrived.SystemName);
        }

        // ===================== Live (ProcessLine) =====================

        [Fact]
        public void ProcessLine_LiveFsdTarget_FiresTargeted()
        {
            var (watcher, events) = CreateLive();
            using var _ = watcher;

            watcher.ProcessLine(
                "{\"timestamp\":\"" + JournalFile.TimestampOf(DateTime.UtcNow) + "\",\"event\":\"FSDTarget\",\"Name\":\"Deciat\"}",
                isLive: true);

            var targeted = Assert.Single(events);
            Assert.Equal(RowEventKind.Targeted, targeted.Kind);
            Assert.Equal("Deciat", targeted.SystemName);
            Assert.True(targeted.IsLive);
            Assert.Null(targeted.PhaseEndUtc);
        }

        [Fact]
        public void ProcessLine_LiveHyperspaceStartJump_FiresJumpingOnlyWithNoPhaseEnd()
        {
            var (watcher, events) = CreateLive();
            using var _ = watcher;

            watcher.ProcessLine(
                "{\"timestamp\":\"" + JournalFile.TimestampOf(DateTime.UtcNow) + "\",\"event\":\"StartJump\",\"JumpType\":\"Hyperspace\",\"StarSystem\":\"Deciat\"}",
                isLive: true);

            var single = Assert.Single(events);
            Assert.Equal(RowEventKind.Jumping, single.Kind);
            Assert.Equal("Deciat", single.SystemName);
            Assert.True(single.IsLive);
            Assert.Null(single.PhaseEndUtc);
        }

        [Fact]
        public void ProcessLine_LiveSuperchargeStartJump_FiresNothing()
        {
            var (watcher, events) = CreateLive();
            using var _ = watcher;

            watcher.ProcessLine(
                "{\"timestamp\":\"" + JournalFile.TimestampOf(DateTime.UtcNow) + "\",\"event\":\"StartJump\",\"JumpType\":\"Supercharge\"}",
                isLive: true);

            Assert.Empty(events);
        }

        [Fact]
        public void ProcessLine_LiveFsdJumpAlone_FiresLiveCarrierLocationButNotArrivedYet()
        {
            // FSDJump alone is deliberately *not* enough to fire Arrived - real journal data
            // shows it fires while the game is still mid-transition. Only LiveCarrierLocation
            // (driving Auto Copy To Clipboard) fires immediately; Arrived awaits Music
            // confirmation - see the tests below.
            var (watcher, events) = CreateLive();
            using var _ = watcher;

            watcher.ProcessLine(
                "{\"timestamp\":\"" + JournalFile.TimestampOf(DateTime.UtcNow) + "\",\"event\":\"FSDJump\",\"StarSystem\":\"Deciat\"}",
                isLive: true);

            var single = Assert.Single(events);
            Assert.Equal(RowEventKind.LiveCarrierLocation, single.Kind);
            Assert.Equal("Deciat", single.SystemName);
        }

        [Theory]
        [InlineData("DestinationFromHyperspace")]
        [InlineData("Supercruise")]
        public void ProcessLine_FsdJumpThenQualifyingMusicTrack_FiresArrivedAndSchedulesCooldown(string musicTrack)
        {
            var (watcher, events) = CreateLive();
            using var _ = watcher;

            watcher.ProcessLine(
                "{\"timestamp\":\"" + JournalFile.TimestampOf(DateTime.UtcNow) + "\",\"event\":\"FSDJump\",\"StarSystem\":\"Deciat\"}",
                isLive: true);
            watcher.ProcessLine(
                "{\"timestamp\":\"" + JournalFile.TimestampOf(DateTime.UtcNow) + "\",\"event\":\"Music\",\"MusicTrack\":\"" + musicTrack + "\"}",
                isLive: true);

            var arrived = Assert.Single(events, e => e.Kind == RowEventKind.Arrived);
            Assert.Equal("Deciat", arrived.SystemName);
            Assert.True(arrived.IsLive);
            Assert.NotNull(arrived.PhaseEndUtc);
        }

        [Fact]
        public void ProcessLine_FsdJumpThenNonQualifyingMusicTrack_DoesNotFireArrived()
        {
            var (watcher, events) = CreateLive();
            using var _ = watcher;

            watcher.ProcessLine(
                "{\"timestamp\":\"" + JournalFile.TimestampOf(DateTime.UtcNow) + "\",\"event\":\"FSDJump\",\"StarSystem\":\"Deciat\"}",
                isLive: true);
            watcher.ProcessLine(
                "{\"timestamp\":\"" + JournalFile.TimestampOf(DateTime.UtcNow) + "\",\"event\":\"Music\",\"MusicTrack\":\"Exploration\"}",
                isLive: true);

            Assert.DoesNotContain(events, e => e.Kind == RowEventKind.Arrived);
        }

        [Fact]
        public void ProcessLine_MusicWithNoPendingArrival_FiresNothing()
        {
            var (watcher, events) = CreateLive();
            using var _ = watcher;

            watcher.ProcessLine(
                "{\"timestamp\":\"" + JournalFile.TimestampOf(DateTime.UtcNow) + "\",\"event\":\"Music\",\"MusicTrack\":\"DestinationFromHyperspace\"}",
                isLive: true);

            Assert.Empty(events);
        }
    }
}
