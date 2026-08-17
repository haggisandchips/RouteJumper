using System.Collections.ObjectModel;
using RouteJumper.Models;
using RouteJumper.Sequencing;
using RouteJumper.Services;
using RouteJumper.ViewModels;
using Xunit;

namespace RouteJumper.Tests.Services
{
    public class AutoPilotControllerTests
    {
        private static readonly DateTime NowUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static EliteInstanceViewModel Instance(int? carrierFuelLevel = null, DateTime? carrierLastDepositUtc = null) => new(
            processId: 1,
            commanderName: "Jameson",
            fid: "F1",
            journalFileName: "Journal.log",
            windowHandle: (IntPtr)1,
            windowPosition: "(0,0)",
            monitorInfo: "Monitor",
            cargoCapacity: null,
            currentCargo: null,
            currentTritium: null,
            currentSystem: null,
            currentStation: null,
            carrierName: null,
            carrierSystem: null,
            carrierBody: null,
            journalFilePath: null,
            carrierId: null,
            carrierFuelLevel: carrierFuelLevel,
            carrierLastDepositUtc: carrierLastDepositUtc);

        /// <summary>
        /// Exercises the real EvaluateAndMaybeTrigger/TriggerCaptainPlotAsync path end to end
        /// (not just the pure ComputeAnnounceDelay helper above) - the row starts blank with
        /// nothing pending on Cooldown, so the Captain's plot fires with no artificial delay,
        /// making this fast and deterministic enough to assert on directly rather than needing a
        /// timing-based wait.
        /// </summary>
        [Fact]
        public async Task Start_BlankRowWithCaptainReady_FiresPlottingEventBeforePlayingMacro()
        {
            var row = new RouteRowViewModel { SystemText = "Sol", Icon = RowIcon.InProgress };
            var rows = new ObservableCollection<RouteRowViewModel>(new[] { row });
            var trigger = new ManualRowEventTrigger();
            var received = new List<RowEvent>();
            trigger.RowTriggered += (_, e) => received.Add(e);

            var macro = new RecordedMacroViewModel(new RecordedMacro { Id = Guid.NewGuid(), Name = "M", ScriptText = "UP" });
            var instance = Instance();
            var playedMacros = new List<RecordedMacroViewModel>();

            var controller = new AutoPilotController(
                rows,
                () => macro,
                () => instance,
                () => null,
                () => null,
                () => 0,
                (m, _) => { playedMacros.Add(m); return Task.FromResult(true); },
                () => Task.CompletedTask,
                () => { },
                () => { },
                _ => { },
                _ => { },
                trigger);

            controller.Start();

            // The fire-and-forget chain (ScheduleEvaluation -> Task.Yield -> EvaluateAndMaybeTrigger
            // -> TriggerCaptainPlotAsync) needs a moment to actually run.
            for (var i = 0; i < 20 && playedMacros.Count == 0; i++)
            {
                await Task.Delay(20);
            }

            // TriggerCaptainPlotAsync fires Plotting and calls _playMacro back to back in the
            // same synchronous block, so both having happened is enough to know the order held.
            Assert.Contains(received, e => e.Kind == RowEventKind.Plotting && e.SystemName == "Sol");
            Assert.Single(playedMacros);
        }

        // ===================== Panic mode (SPEC §4.7) =====================

        /// <summary>A RouteSequencer attached to the same trigger AutoPilotController fires RowEventKind.Plotting through - without this, "Plotting" never actually lands on the row, the same way it wouldn't in production wiring (MainViewModel attaches one for real).</summary>
        private static ManualRowEventTrigger TriggerWithSequencerAttached(IReadOnlyList<RouteRowViewModel> rows)
        {
            var trigger = new ManualRowEventTrigger();
            var sequencer = new RouteSequencer();
            sequencer.SetRows(rows);
            sequencer.AttachRowTrigger(trigger);
            return trigger;
        }

        [Fact]
        public async Task CaptainPlot_MacroCompletesButRowNeverReachesPlotted_PanicsAndStopsAutoPilot()
        {
            var row = new RouteRowViewModel { SystemText = "Sol", Icon = RowIcon.InProgress };
            var rows = new ObservableCollection<RouteRowViewModel>(new[] { row });
            var macro = new RecordedMacroViewModel(new RecordedMacro { Id = Guid.NewGuid(), Name = "M", ScriptText = "UP" });
            var stopped = false;
            string? reportedError = null;

            var controller = new AutoPilotController(
                rows,
                () => macro,
                () => Instance(),
                () => null,
                () => null,
                () => 0,
                (_, _) => Task.FromResult(true), // the macro "completes" but never advances row.Status
                () => Task.CompletedTask,
                () => { },
                () => stopped = true,
                msg => reportedError = msg,
                _ => { },
                TriggerWithSequencerAttached(rows));

            controller.Start();

            for (var i = 0; i < 20 && !stopped; i++)
            {
                await Task.Delay(20);
            }

            Assert.True(stopped);
            Assert.NotNull(reportedError);
            Assert.Contains("Sol", reportedError);
        }

        [Fact]
        public async Task CaptainPlot_RowReachedPlottedBeforeMacroFinished_DoesNotPanic()
        {
            var row = new RouteRowViewModel { SystemText = "Sol", Icon = RowIcon.InProgress };
            var rows = new ObservableCollection<RouteRowViewModel>(new[] { row });
            var macro = new RecordedMacroViewModel(new RecordedMacro { Id = Guid.NewGuid(), Name = "M", ScriptText = "UP" });
            var stopped = false;

            var controller = new AutoPilotController(
                rows,
                () => macro,
                () => Instance(),
                () => null,
                () => null,
                () => 0,
                (_, _) =>
                {
                    // Simulates journal tracking (CarrierJumpRequest) catching up while the macro
                    // was still finishing its last few housekeeping steps.
                    row.Status = "Plotted";
                    return Task.FromResult(true);
                },
                () => Task.CompletedTask,
                () => { },
                () => stopped = true,
                _ => { },
                _ => { },
                TriggerWithSequencerAttached(rows));

            controller.Start();
            await Task.Delay(200); // give a wrongly-firing panic a chance to happen

            Assert.False(stopped);
        }

        [Fact]
        public async Task CaptainPlot_MacroDidNotRunToCompletion_PanicsAndStopsAutoPilot()
        {
            // Deliberately paranoid: a macro cut short for *any* reason (cancelled/superseded by
            // a different Auto Pilot trigger, an ordinary Stop mid-script, focus loss, ...) can
            // leave the game in front of an unknown panel with an unknown selection - there's no
            // safe assumption to make about that, so this panics immediately rather than waiting
            // to see whether the jump happened to get plotted anyway.
            var row = new RouteRowViewModel { SystemText = "Sol", Icon = RowIcon.InProgress };
            var rows = new ObservableCollection<RouteRowViewModel>(new[] { row });
            var macro = new RecordedMacroViewModel(new RecordedMacro { Id = Guid.NewGuid(), Name = "M", ScriptText = "UP" });
            var stopped = false;
            string? reportedError = null;

            var controller = new AutoPilotController(
                rows,
                () => macro,
                () => Instance(),
                () => null,
                () => null,
                () => 0,
                (_, _) => Task.FromResult(false), // never reached the end
                () => Task.CompletedTask,
                () => { },
                () => stopped = true,
                msg => reportedError = msg,
                _ => { },
                new ManualRowEventTrigger());

            controller.Start();

            for (var i = 0; i < 20 && !stopped; i++)
            {
                await Task.Delay(20);
            }

            Assert.True(stopped);
            Assert.NotNull(reportedError);
        }

        /// <summary>Builds a two-row route whose first row is already "Jumping" with a past PhaseEndUtc, so EvaluateAndMaybeTrigger schedules (and, with a zero Auto Pilot delay, immediately fires) the Engineer's refuel for it.</summary>
        private static ObservableCollection<RouteRowViewModel> RowsWithJumpingFirstRow() => new(new[]
        {
            new RouteRowViewModel
            {
                SystemText = "Sol",
                Icon = RowIcon.InProgress,
                Status = "Jumping",
                PhaseEndUtc = DateTime.UtcNow.AddSeconds(-10)
            },
            new RouteRowViewModel { SystemText = "Deciat" }
        });

        [Fact]
        public async Task EngineerRefuel_MacroCompletesButNoDepositObserved_PanicsAndStopsAutoPilot()
        {
            var rows = RowsWithJumpingFirstRow();
            var macro = new RecordedMacroViewModel(new RecordedMacro { Id = Guid.NewGuid(), Name = "M", ScriptText = "UP" });
            EliteInstanceViewModel currentEngineerInstance = Instance(carrierFuelLevel: 500);
            var refreshed = false;
            var stopped = false;
            string? reportedError = null;

            var controller = new AutoPilotController(
                rows,
                () => null,
                () => null,
                () => macro,
                () => currentEngineerInstance,
                () => 0,
                (_, _) => Task.FromResult(true),
                () =>
                {
                    refreshed = true;
                    currentEngineerInstance = Instance(carrierFuelLevel: 500); // rescanned - no fresh deposit observed, e.g. genuinely nothing was deposited
                    return Task.CompletedTask;
                },
                () => { },
                () => stopped = true,
                msg => reportedError = msg,
                _ => { },
                new ManualRowEventTrigger());

            controller.Start();

            for (var i = 0; i < 20 && !stopped; i++)
            {
                await Task.Delay(20);
            }

            Assert.True(refreshed);
            Assert.True(stopped);
            Assert.NotNull(reportedError);
            Assert.Contains("fuel depot", reportedError);
        }

        [Fact]
        public async Task EngineerRefuel_FreshDepositObservedAfterMacroStarted_DoesNotPanic()
        {
            var rows = RowsWithJumpingFirstRow();
            var macro = new RecordedMacroViewModel(new RecordedMacro { Id = Guid.NewGuid(), Name = "M", ScriptText = "UP" });
            EliteInstanceViewModel currentEngineerInstance = Instance(carrierFuelLevel: 500);
            var stopped = false;

            var controller = new AutoPilotController(
                rows,
                () => null,
                () => null,
                () => macro,
                () => currentEngineerInstance,
                () => 0,
                (_, _) => Task.FromResult(true),
                () =>
                {
                    // A genuine CarrierDepositFuel, timestamped now (after the macro started).
                    currentEngineerInstance = Instance(carrierFuelLevel: 620, carrierLastDepositUtc: DateTime.UtcNow);
                    return Task.CompletedTask;
                },
                () => { },
                () => stopped = true,
                _ => { },
                _ => { },
                new ManualRowEventTrigger());

            controller.Start();
            await Task.Delay(200);

            Assert.False(stopped);
        }

        [Fact]
        public async Task EngineerRefuel_DepositTimestampRefillsToAPreviouslyKnownCeiling_StillDoesNotPanic()
        {
            // The real-world bug this guards against: Elite's journal never logs the fuel a jump
            // itself consumes, so the last known CarrierFuelLevel can easily still read the
            // pre-jump ceiling (e.g. 1000, from before the very jump that made this refuel
            // necessary) - a depot genuinely refilled back to that exact same number must not be
            // mistaken for "nothing was deposited" just because the number itself didn't move.
            var rows = RowsWithJumpingFirstRow();
            var macro = new RecordedMacroViewModel(new RecordedMacro { Id = Guid.NewGuid(), Name = "M", ScriptText = "UP" });
            EliteInstanceViewModel currentEngineerInstance = Instance(carrierFuelLevel: 1000); // stale pre-jump ceiling
            var stopped = false;

            var controller = new AutoPilotController(
                rows,
                () => null,
                () => null,
                () => macro,
                () => currentEngineerInstance,
                () => 0,
                (_, _) => Task.FromResult(true),
                () =>
                {
                    // Refilled back to the same 1000 - but with a fresh deposit timestamp proving it's real.
                    currentEngineerInstance = Instance(carrierFuelLevel: 1000, carrierLastDepositUtc: DateTime.UtcNow);
                    return Task.CompletedTask;
                },
                () => { },
                () => stopped = true,
                _ => { },
                _ => { },
                new ManualRowEventTrigger());

            controller.Start();
            await Task.Delay(200);

            Assert.False(stopped);
        }

        [Fact]
        public async Task EngineerRefuel_DepotAlreadyFullBeforehand_StillPanicsBecauseNoFreshDepositObserved()
        {
            // Deliberately paranoid: a real jump always consumes some fuel, so a rescan showing
            // no fresh CarrierDepositFuel event at all for this carrier is itself the anomaly -
            // most likely evidence nothing was actually deposited - not a benign "nothing to do"
            // case to wave through, even though the depot happens to already read as full.
            var rows = RowsWithJumpingFirstRow();
            var macro = new RecordedMacroViewModel(new RecordedMacro { Id = Guid.NewGuid(), Name = "M", ScriptText = "UP" });
            var refreshed = false;
            var stopped = false;
            string? reportedError = null;

            var controller = new AutoPilotController(
                rows,
                () => null,
                () => null,
                () => macro,
                () => Instance(carrierFuelLevel: 1000), // already full, no deposit timestamp at all
                () => 0,
                (_, _) => Task.FromResult(true),
                () => { refreshed = true; return Task.CompletedTask; },
                () => { },
                () => stopped = true,
                msg => reportedError = msg,
                _ => { },
                new ManualRowEventTrigger());

            controller.Start();

            for (var i = 0; i < 20 && !stopped; i++)
            {
                await Task.Delay(20);
            }

            Assert.True(refreshed);
            Assert.True(stopped);
            Assert.NotNull(reportedError);
        }

        [Fact]
        public async Task EngineerRefuel_FuelLevelUnknownBeforehand_StillPanics()
        {
            var rows = RowsWithJumpingFirstRow();
            var macro = new RecordedMacroViewModel(new RecordedMacro { Id = Guid.NewGuid(), Name = "M", ScriptText = "UP" });
            var stopped = false;
            string? reportedError = null;

            var controller = new AutoPilotController(
                rows,
                () => null,
                () => null,
                () => macro,
                () => Instance(carrierFuelLevel: null), // never seen before
                () => 0,
                (_, _) => Task.FromResult(true),
                () => Task.CompletedTask,
                () => { },
                () => stopped = true,
                msg => reportedError = msg,
                _ => { },
                new ManualRowEventTrigger());

            controller.Start();

            for (var i = 0; i < 20 && !stopped; i++)
            {
                await Task.Delay(20);
            }

            Assert.True(stopped);
            Assert.NotNull(reportedError);
        }

        [Fact]
        public async Task EngineerRefuel_MacroDidNotRunToCompletion_PanicsWithoutEvenRescanning()
        {
            var rows = RowsWithJumpingFirstRow();
            var macro = new RecordedMacroViewModel(new RecordedMacro { Id = Guid.NewGuid(), Name = "M", ScriptText = "UP" });
            var refreshed = false;
            var stopped = false;

            var controller = new AutoPilotController(
                rows,
                () => null,
                () => null,
                () => macro,
                () => Instance(carrierFuelLevel: 500),
                () => 0,
                (_, _) => Task.FromResult(false), // never reached the end
                () => { refreshed = true; return Task.CompletedTask; },
                () => { },
                () => stopped = true,
                _ => { },
                _ => { },
                new ManualRowEventTrigger());

            controller.Start();

            for (var i = 0; i < 20 && !stopped; i++)
            {
                await Task.Delay(20);
            }

            Assert.True(stopped);
            Assert.False(refreshed); // no point rescanning - the game's state is already unknown
        }

        [Fact]
        public void ComputeAnnounceDelay_ReturnsThePositiveDelay_WhenDueTimeIsInTheFuture()
        {
            var delay = AutoPilotController.ComputeAnnounceDelay(NowUtc.AddSeconds(5), NowUtc, clampToImmediate: false);

            Assert.Equal(TimeSpan.FromSeconds(5), delay);
        }

        [Fact]
        public void ComputeAnnounceDelay_ReturnsNull_WhenDueTimeHasPassedAndNotClamped()
        {
            var delay = AutoPilotController.ComputeAnnounceDelay(NowUtc.AddSeconds(-1), NowUtc, clampToImmediate: false);

            Assert.Null(delay);
        }

        [Fact]
        public void ComputeAnnounceDelay_ReturnsZero_WhenDueTimeHasPassedAndClamped()
        {
            var delay = AutoPilotController.ComputeAnnounceDelay(NowUtc.AddSeconds(-1), NowUtc, clampToImmediate: true);

            Assert.Equal(TimeSpan.Zero, delay);
        }

        /// <summary>
        /// Regression test for the exact bug reported in production: with the default 5000ms
        /// Auto Pilot delay, the Engineer's "in 5 seconds" mark lands exactly at "now" (the
        /// instant Cooldown starts) - by the time this runs, real elapsed time has ticked microseconds
        /// past that instant, so whenUtc is ever-so-slightly in the past. Without clamping, that
        /// silently skipped the announcement on every single run rather than firing it.
        /// </summary>
        [Fact]
        public void ComputeAnnounceDelay_ReturnsZero_WhenDueTimeIsExactlyNowAndClamped()
        {
            var delay = AutoPilotController.ComputeAnnounceDelay(NowUtc, NowUtc, clampToImmediate: true);

            Assert.Equal(TimeSpan.Zero, delay);
        }

        [Fact]
        public void ComputeAnnounceDelay_StillReturnsThePositiveDelay_WhenClampedButNotYetDue()
        {
            var delay = AutoPilotController.ComputeAnnounceDelay(NowUtc.AddSeconds(2), NowUtc, clampToImmediate: true);

            Assert.Equal(TimeSpan.FromSeconds(2), delay);
        }
    }
}
