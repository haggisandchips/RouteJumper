using System.IO;
using RouteJumper.Models;
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

        private static (ShipRouteJournalWatcher Watcher, List<CapturedEvent> Events) CreateLive(
            string journalPath = "unused", IStarSystemLookupService? starSystemLookupService = null)
        {
            var events = new List<CapturedEvent>();
            var watcher = new ShipRouteJournalWatcher(
                journalPath,
                (kind, systemName, isLive, phaseEndUtc) => events.Add(new CapturedEvent(kind, systemName, isLive, phaseEndUtc)),
                starSystemLookupService);
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
        public async Task StartAsync_TargetSetAfterArrival_FiresBothArrivedAndTargeted()
        {
            // Position (Location/FSDJump) and Targeted (FSDTarget) are independent results, not a
            // single "whichever came last wins" tie-break - see ComputeCatchUpState's own doc
            // comment. Both apply: the ship really did arrive at Deciat, *and* it has since
            // locked a fresh target (Sol) that hasn't been jumped to yet.
            using var dir = new TempDirectory();
            var journal = new JournalFile()
                .FSDJump("Deciat", DateTime.UtcNow.AddMinutes(-2))
                .FSDTarget("Sol", DateTime.UtcNow.AddMinutes(-1));

            var events = await RunCatchUpAsync(dir, journal);

            Assert.Equal(2, events.Count);
            var arrived = Assert.Single(events, e => e.Kind == RowEventKind.Arrived);
            Assert.Equal("Deciat", arrived.SystemName);
            Assert.False(arrived.IsLive);
            var targeted = Assert.Single(events, e => e.Kind == RowEventKind.Targeted);
            Assert.Equal("Sol", targeted.SystemName);
            Assert.False(targeted.IsLive);
        }

        [Fact]
        public async Task StartAsync_JumpStartedAfterArrival_FiresArrivedThenJumping()
        {
            using var dir = new TempDirectory();
            var journal = new JournalFile()
                .FSDJump("Deciat", DateTime.UtcNow.AddMinutes(-2))
                .StartJump("Hyperspace", "Sol", DateTime.UtcNow.AddMinutes(-1));

            var events = await RunCatchUpAsync(dir, journal);

            Assert.Equal(2, events.Count);
            Assert.Single(events, e => e.Kind == RowEventKind.Arrived && e.SystemName == "Deciat");
            Assert.Single(events, e => e.Kind == RowEventKind.Jumping && e.SystemName == "Sol");
        }

        [Fact]
        public async Task StartAsync_TargetThenStartJump_TargetIsSupersededSoOnlyJumpingFires()
        {
            // Once a lock-on has actually been acted on (a real StartJump), the stale target no
            // longer describes anything relevant - mirrors how live processing naturally
            // supersedes one Status with another as the ship's real state moves on.
            using var dir = new TempDirectory();
            var journal = new JournalFile()
                .FSDTarget("Deciat", DateTime.UtcNow.AddMinutes(-2))
                .StartJump("Hyperspace", "Deciat", DateTime.UtcNow.AddMinutes(-1));

            var events = await RunCatchUpAsync(dir, journal);

            var single = Assert.Single(events);
            Assert.Equal(RowEventKind.Jumping, single.Kind);
        }

        [Fact]
        public async Task StartAsync_TargetThenNavRouteClear_FiresNoTargetedResult()
        {
            using var dir = new TempDirectory();
            var journal = new JournalFile()
                .FSDTarget("Deciat", DateTime.UtcNow.AddMinutes(-2))
                .NavRouteClear(DateTime.UtcNow.AddMinutes(-1));

            var events = await RunCatchUpAsync(dir, journal);

            Assert.Empty(events);
        }

        [Fact]
        public async Task StartAsync_LocationAloneEstablishesPosition_FiresArrived()
        {
            using var dir = new TempDirectory();
            var journal = new JournalFile().Location("Sol", DateTime.UtcNow.AddMinutes(-1));

            var events = await RunCatchUpAsync(dir, journal);

            var arrived = Assert.Single(events, e => e.Kind == RowEventKind.Arrived);
            Assert.Equal("Sol", arrived.SystemName);
            Assert.False(arrived.IsLive);
        }

        [Fact]
        public async Task StartAsync_LocationAfterFsdJump_LocationWins()
        {
            using var dir = new TempDirectory();
            var journal = new JournalFile()
                .FSDJump("Deciat", DateTime.UtcNow.AddMinutes(-2))
                .Location("Sol", DateTime.UtcNow.AddMinutes(-1));

            var events = await RunCatchUpAsync(dir, journal);

            var arrived = Assert.Single(events, e => e.Kind == RowEventKind.Arrived);
            Assert.Equal("Sol", arrived.SystemName);
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

        [Fact]
        public void ProcessLine_LiveLocation_FiresLiveCarrierLocationThenArrivedWithNoPhaseEnd()
        {
            var (watcher, events) = CreateLive();
            using var _ = watcher;

            watcher.ProcessLine(
                "{\"timestamp\":\"" + JournalFile.TimestampOf(DateTime.UtcNow) + "\",\"event\":\"Location\",\"StarSystem\":\"Sol\"}",
                isLive: true);

            Assert.Equal(2, events.Count);
            Assert.Single(events, e => e.Kind == RowEventKind.LiveCarrierLocation && e.SystemName == "Sol");
            var arrived = Assert.Single(events, e => e.Kind == RowEventKind.Arrived);
            Assert.Equal("Sol", arrived.SystemName);
            Assert.True(arrived.IsLive);
            // Never a jump-completion signal in its own right - must never imply a cooldown is
            // now counting down (RouteSequencer's own Arrived Cooldown gate needs both IsLive and
            // a real PhaseEndUtc).
            Assert.Null(arrived.PhaseEndUtc);
        }

        [Fact]
        public void ProcessLine_CatchUpLocation_FiresArrivedNotLive()
        {
            var (watcher, events) = CreateLive();
            using var _ = watcher;

            watcher.ProcessLine(
                "{\"timestamp\":\"" + JournalFile.TimestampOf(DateTime.UtcNow) + "\",\"event\":\"Location\",\"StarSystem\":\"Sol\"}",
                isLive: false);

            var arrived = Assert.Single(events, e => e.Kind == RowEventKind.Arrived);
            Assert.False(arrived.IsLive);
            Assert.DoesNotContain(events, e => e.Kind == RowEventKind.LiveCarrierLocation);
        }

        [Fact]
        public void ProcessLine_LiveLocation_SupersedesAPendingFsdJumpArrivalAwaitingMusic()
        {
            // A fresh Location snapshot (e.g. a relog) is more recent and more authoritative than
            // a stale pending arrival still awaiting Music confirmation - the pending arrival must
            // never fire once superseded.
            var (watcher, events) = CreateLive();
            using var _ = watcher;

            watcher.ProcessLine(
                "{\"timestamp\":\"" + JournalFile.TimestampOf(DateTime.UtcNow) + "\",\"event\":\"FSDJump\",\"StarSystem\":\"Deciat\"}",
                isLive: true);
            watcher.ProcessLine(
                "{\"timestamp\":\"" + JournalFile.TimestampOf(DateTime.UtcNow) + "\",\"event\":\"Location\",\"StarSystem\":\"Sol\"}",
                isLive: true);
            watcher.ProcessLine(
                "{\"timestamp\":\"" + JournalFile.TimestampOf(DateTime.UtcNow) + "\",\"event\":\"Music\",\"MusicTrack\":\"Supercruise\"}",
                isLive: true);

            // Only Location's own Arrived (Sol) - the superseded FSDJump pending arrival (Deciat)
            // never fires, even though a qualifying Music track followed.
            Assert.Single(events, e => e.Kind == RowEventKind.Arrived);
            Assert.DoesNotContain(events, e => e.Kind == RowEventKind.Arrived && e.SystemName == "Deciat");
        }

        [Fact]
        public void ProcessLine_LiveNavRouteClear_FiresTargetCleared()
        {
            var (watcher, events) = CreateLive();
            using var _ = watcher;

            watcher.ProcessLine(
                "{\"timestamp\":\"" + JournalFile.TimestampOf(DateTime.UtcNow) + "\",\"event\":\"NavRouteClear\"}",
                isLive: true);

            var single = Assert.Single(events);
            Assert.Equal(RowEventKind.TargetCleared, single.Kind);
            Assert.True(single.IsLive);
        }

        // ===================== FSDTarget-driven cache seeding (StarClass/NavRoute.json) =====================

        [Fact]
        public async Task ProcessLine_LiveFsdTargetWithStarClass_SeedsStarType()
        {
            var fake = new FakeStarSystemLookupService();
            var (watcher, _) = CreateLive(starSystemLookupService: fake);
            using var _w = watcher;

            watcher.ProcessLine(
                "{\"timestamp\":\"" + JournalFile.TimestampOf(DateTime.UtcNow) + "\",\"event\":\"FSDTarget\",\"Name\":\"Deciat\",\"StarClass\":\"K\"}",
                isLive: true);
            // Cache seeding is deliberately backgrounded (see ProcessLine's own comment) so it
            // never delays processing the next journal line - tests wait for it explicitly instead
            // of racing a fire-and-forget Task.Run.
            await watcher.WaitForPendingCacheSeedAsync();

            Assert.Equal("K (Yellow-Orange)", fake.StarTypes["Deciat"]);
        }

        [Fact]
        public async Task ProcessLine_LiveFsdTargetWithoutStarClass_DoesNotSeedStarType()
        {
            var fake = new FakeStarSystemLookupService();
            var (watcher, _) = CreateLive(starSystemLookupService: fake);
            using var _w = watcher;

            watcher.ProcessLine(
                "{\"timestamp\":\"" + JournalFile.TimestampOf(DateTime.UtcNow) + "\",\"event\":\"FSDTarget\",\"Name\":\"Deciat\"}",
                isLive: true);
            await watcher.WaitForPendingCacheSeedAsync();

            Assert.Empty(fake.StarTypes);
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
            Assert.Equal("G (White-Yellow)", fake.StarTypes["Sol"]);
            Assert.Equal("K (Yellow-Orange)", fake.StarTypes["Deciat"]);
            Assert.Equal(10477373803, fake.SystemAddresses["Sol"]);
            Assert.Equal(1, fake.SystemAddresses["Deciat"]);
        }

        [Fact]
        public async Task ProcessLine_LiveFsdTargetWithSystemAddress_SeedsSystemAddress()
        {
            var fake = new FakeStarSystemLookupService();
            var (watcher, _) = CreateLive(starSystemLookupService: fake);
            using var _w = watcher;

            watcher.ProcessLine(
                "{\"timestamp\":\"" + JournalFile.TimestampOf(DateTime.UtcNow) + "\",\"event\":\"FSDTarget\",\"Name\":\"Deciat\",\"SystemAddress\":1}",
                isLive: true);
            await watcher.WaitForPendingCacheSeedAsync();

            Assert.Equal(1, fake.SystemAddresses["Deciat"]);
        }

        [Fact]
        public async Task ProcessLine_LiveFsdTargetWithoutRemainingJumpsInRoute_DoesNotReadNavRoute()
        {
            using var dir = new TempDirectory();
            var journalPath = dir.CombinePath("Journal.Test.01.log");
            File.WriteAllText(dir.CombinePath("NavRoute.json"), """
                { "Route": [ { "StarSystem": "Sol", "StarPos": [0.0, 0.0, 0.0], "StarClass": "G" } ] }
                """);
            var fake = new FakeStarSystemLookupService();
            var (watcher, _) = CreateLive(journalPath, fake);
            using var _w = watcher;

            watcher.ProcessLine(
                "{\"timestamp\":\"" + JournalFile.TimestampOf(DateTime.UtcNow) + "\",\"event\":\"FSDTarget\",\"Name\":\"Sol\"}",
                isLive: true);
            await watcher.WaitForPendingCacheSeedAsync();

            Assert.Empty(fake.Coordinates);
        }

        [Fact]
        public async Task ProcessLine_LiveFsdTargetWithRemainingJumpsInRoute_MissingNavRouteFile_DoesNotThrow()
        {
            using var dir = new TempDirectory();
            var journalPath = dir.CombinePath("Journal.Test.01.log");
            // No NavRoute.json written at all - a plausible real-world race (RemainingJumpsInRoute
            // present, but the file hasn't been (re)written yet) that must degrade gracefully.
            var fake = new FakeStarSystemLookupService();
            var (watcher, events) = CreateLive(journalPath, fake);
            using var _w = watcher;

            watcher.ProcessLine(
                "{\"timestamp\":\"" + JournalFile.TimestampOf(DateTime.UtcNow) + "\",\"event\":\"FSDTarget\",\"Name\":\"Sol\",\"RemainingJumpsInRoute\":2}",
                isLive: true);
            await watcher.WaitForPendingCacheSeedAsync();

            Assert.Empty(fake.Coordinates);
            Assert.Single(events, e => e.Kind == RowEventKind.Targeted); // the row event itself still fired fine
        }

        [Fact]
        public async Task ProcessLine_LiveNavRouteEvent_SeedsFromNavRouteJson()
        {
            // The NavRoute journal event itself - not just FSDTarget's own RemainingJumpsInRoute
            // field - is the primary signal that NavRoute.json was just (re)written, e.g. plotting
            // or re-plotting a route via the galaxy map, which can happen well before the CMDR
            // ever actually targets/locks the next jump.
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
            Assert.Equal("G (White-Yellow)", fake.StarTypes["Sol"]);
            Assert.Equal("K (Yellow-Orange)", fake.StarTypes["Deciat"]);
            Assert.Empty(events); // no row-progress event - purely opportunistic cache seeding
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
        public async Task ProcessLine_LiveFsdTargetWithNoLookupServiceSupplied_DoesNotThrow()
        {
            var (watcher, events) = CreateLive(); // starSystemLookupService intentionally omitted
            using var _w = watcher;

            watcher.ProcessLine(
                "{\"timestamp\":\"" + JournalFile.TimestampOf(DateTime.UtcNow) + "\",\"event\":\"FSDTarget\",\"Name\":\"Sol\",\"StarClass\":\"G\",\"RemainingJumpsInRoute\":2}",
                isLive: true);
            await watcher.WaitForPendingCacheSeedAsync();

            Assert.Single(events, e => e.Kind == RowEventKind.Targeted);
        }
    }
}
