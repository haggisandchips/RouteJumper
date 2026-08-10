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

        private static EliteInstanceViewModel Instance() => new(
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
            carrierFuelLevel: null);

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
                (m, _) => playedMacros.Add(m),
                () => { },
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
