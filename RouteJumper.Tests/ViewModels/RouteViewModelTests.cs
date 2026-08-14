using System.Windows.Threading;
using RouteJumper.Models;
using RouteJumper.Sequencing;
using RouteJumper.Services;
using RouteJumper.Tests.TestSupport;
using RouteJumper.ViewModels;
using Xunit;

namespace RouteJumper.Tests.ViewModels
{
    public class RouteViewModelTests
    {
        // Defaults to a FakeStarSystemLookupService (never the real EDSM one) so every test in
        // this file - including ones that don't care about Distance/Star Type at all - stays
        // offline/hermetic and fast; Save() fires enrichment off in the background regardless of
        // whether a test is looking at it.
        private static RouteViewModel Create(
            TempDirectory dir,
            IRowEventTrigger? trigger = null,
            Func<bool>? canEngageAutoPilot = null,
            IStarSystemLookupService? starSystemLookupService = null,
            Func<string?>? getOriginSystemName = null) =>
            new(new AppSettingsStore(dir.Path), trigger, canEngageAutoPilot,
                starSystemLookupService ?? new FakeStarSystemLookupService(),
                getOriginSystemName);

        [Fact]
        public void SaveCommand_DisabledForBlankText()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);

            Assert.False(vm.SaveCommand.CanExecute(null));
            vm.RouteText = "   ";
            Assert.False(vm.SaveCommand.CanExecute(null));
            vm.RouteText = "Sol";
            Assert.True(vm.SaveCommand.CanExecute(null));
        }

        [Fact]
        public void Save_TrimsLinesAndDropsBlankLines()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            vm.RouteText = "  Sol  \n\n   \nAlpha Centauri\r\nWolf 359\n";

            vm.SaveCommand.Execute(null);

            Assert.Equal(3, vm.Rows.Count);
            Assert.Equal("Sol", vm.Rows[0].SystemText);
            Assert.Equal(1, vm.Rows[0].Number);
            Assert.Equal("Alpha Centauri", vm.Rows[1].SystemText);
            Assert.Equal(2, vm.Rows[1].Number);
            Assert.Equal("Wolf 359", vm.Rows[2].SystemText);
            Assert.Equal(3, vm.Rows[2].Number);
        }

        [Fact]
        public void Save_FirstRowBecomesInProgress()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            vm.RouteText = "Sol\nAlpha Centauri";

            vm.SaveCommand.Execute(null);

            Assert.Equal(RowIcon.InProgress, vm.Rows[0].Icon);
            Assert.Equal(RowIcon.None, vm.Rows[1].Icon);
        }

        [Fact]
        public void Save_SwitchesToSavedStateAndRaisesRouteSaved()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            vm.RouteText = "Sol";
            var raised = false;
            vm.RouteSaved += (_, _) => raised = true;

            vm.SaveCommand.Execute(null);

            Assert.True(vm.IsSaved);
            Assert.True(raised);
        }

        [Fact]
        public void Save_AlwaysProducesFreshRows_DiscardingPriorProgress()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            vm.RouteText = "Sol\nAlpha Centauri";
            vm.SaveCommand.Execute(null);
            vm.Rows[0].Icon = RowIcon.Complete;
            vm.Rows[1].Icon = RowIcon.InProgress;
            vm.Rows[1].Status = "Plotted";

            vm.EditCommand.Execute(null);
            vm.SaveCommand.Execute(null); // re-save identical text

            Assert.Equal(RowIcon.InProgress, vm.Rows[0].Icon);
            Assert.Equal(RowIcon.None, vm.Rows[1].Icon);
            Assert.Equal(string.Empty, vm.Rows[1].Status);
        }

        [Fact]
        public void Cancel_NeverSaved_ClearsTextAndStaysInEditState()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            vm.RouteText = "Sol";

            vm.CancelCommand.Execute(null);

            Assert.Equal(string.Empty, vm.RouteText);
            Assert.False(vm.IsSaved);
        }

        [Fact]
        public void Cancel_AfterSave_RestoresLastSavedTextAndReturnsToTableState()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            vm.RouteText = "Sol";
            vm.SaveCommand.Execute(null);
            vm.EditCommand.Execute(null);
            vm.RouteText = "Sol\nUnsaved Extra Line";

            vm.CancelCommand.Execute(null);

            Assert.Equal("Sol", vm.RouteText);
            Assert.True(vm.IsSaved);
        }

        [Fact]
        public void Cancel_AfterSave_LeavesExistingTableProgressUntouched()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            vm.RouteText = "Sol\nAlpha Centauri";
            vm.SaveCommand.Execute(null);
            vm.Rows[0].Icon = RowIcon.Complete;
            vm.Rows[1].Status = "Cooldown";
            vm.EditCommand.Execute(null);

            vm.CancelCommand.Execute(null);

            Assert.Equal(RowIcon.Complete, vm.Rows[0].Icon);
            Assert.Equal("Cooldown", vm.Rows[1].Status);
        }

        [Fact]
        public void Edit_ReturnsToEditStateWithoutClearingText()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            vm.RouteText = "Sol";
            vm.SaveCommand.Execute(null);

            vm.EditCommand.Execute(null);

            Assert.False(vm.IsSaved);
            Assert.Equal("Sol", vm.RouteText);
        }

        [Fact]
        public void EditCommand_DisabledUntilSaved()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            Assert.False(vm.EditCommand.CanExecute(null));

            vm.RouteText = "Sol";
            vm.SaveCommand.Execute(null);
            Assert.True(vm.EditCommand.CanExecute(null));
        }

        [Fact]
        public void AutoPilotCommand_DisabledWithoutSavedNonEmptyRoute()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir, canEngageAutoPilot: () => true);

            Assert.False(vm.AutoPilotCommand.CanExecute(null));

            vm.RouteText = "Sol";
            vm.SaveCommand.Execute(null);

            Assert.True(vm.AutoPilotCommand.CanExecute(null));
        }

        [Fact]
        public void AutoPilotCommand_DisabledWhenRolesRequirementsNotMet()
        {
            using var dir = new TempDirectory();
            var canEngage = false;
            var vm = Create(dir, canEngageAutoPilot: () => canEngage);
            vm.RouteText = "Sol";
            vm.SaveCommand.Execute(null);

            Assert.False(vm.AutoPilotCommand.CanExecute(null));

            canEngage = true;
            Assert.True(vm.AutoPilotCommand.CanExecute(null));
        }

        [Fact]
        public void ToggleAutoPilot_FlipsIsAutoPilotRunningAndFiresEvent()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir, canEngageAutoPilot: () => true);
            vm.RouteText = "Sol";
            vm.SaveCommand.Execute(null);

            var seen = new List<bool>();
            vm.AutoPilotRunningChanged += (_, running) => seen.Add(running);

            vm.AutoPilotCommand.Execute(null);
            Assert.True(vm.IsAutoPilotRunning);

            vm.AutoPilotCommand.Execute(null);
            Assert.False(vm.IsAutoPilotRunning);

            Assert.Equal(new[] { true, false }, seen);
        }

        [Fact]
        public void SetShipMode_True_HidesButtonAndDisablesCommand()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir, canEngageAutoPilot: () => true);
            vm.RouteText = "Sol";
            vm.SaveCommand.Execute(null);
            Assert.True(vm.AutoPilotCommand.CanExecute(null));

            vm.SetShipMode(true);

            Assert.False(vm.ShowAutoPilotButton);
            Assert.False(vm.AutoPilotCommand.CanExecute(null));
        }

        [Fact]
        public void SetShipMode_True_ForciblyStopsAnAlreadyRunningAutoPilot()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir, canEngageAutoPilot: () => true);
            vm.RouteText = "Sol";
            vm.SaveCommand.Execute(null);
            vm.AutoPilotCommand.Execute(null);
            Assert.True(vm.IsAutoPilotRunning);

            vm.SetShipMode(true);

            Assert.False(vm.IsAutoPilotRunning);
        }

        [Fact]
        public void SetShipMode_False_ShowsButtonAgain()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir, canEngageAutoPilot: () => true);
            vm.SetShipMode(true);

            vm.SetShipMode(false);

            Assert.True(vm.ShowAutoPilotButton);
        }

        [Fact]
        public void StopAutoPilot_WhileRunning_SetsIsAutoPilotRunningFalse()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir, canEngageAutoPilot: () => true);
            vm.RouteText = "Sol";
            vm.SaveCommand.Execute(null);
            vm.AutoPilotCommand.Execute(null);

            vm.StopAutoPilot();

            Assert.False(vm.IsAutoPilotRunning);
        }

        [Fact]
        public void Save_ResetsIsAutoPilotRunning()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir, canEngageAutoPilot: () => true);
            vm.RouteText = "Sol";
            vm.SaveCommand.Execute(null);
            vm.AutoPilotCommand.Execute(null);
            Assert.True(vm.IsAutoPilotRunning);

            vm.SaveCommand.Execute(null); // re-save (still has non-blank text)

            Assert.False(vm.IsAutoPilotRunning);
        }

        [Fact]
        public void SetNextSystem_MarksEarlierRowsCompleteTargetInProgressLaterRowsNone()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            vm.RouteText = "Sol\nAlpha Centauri\nWolf 359\nDeciat";
            vm.SaveCommand.Execute(null);
            vm.Rows[0].Icon = RowIcon.None; // arbitrary prior state, should be overridden regardless
            vm.Rows[3].Icon = RowIcon.Complete;
            vm.Rows[3].Status = "Cooldown";

            vm.SetNextSystemCommand.Execute(vm.Rows[2]);

            Assert.Equal(RowIcon.Complete, vm.Rows[0].Icon);
            Assert.Equal(RowIcon.Complete, vm.Rows[1].Icon);
            Assert.Equal(RowIcon.InProgress, vm.Rows[2].Icon);
            Assert.Equal(RowIcon.None, vm.Rows[3].Icon);
            Assert.Equal(string.Empty, vm.Rows[3].Status);
        }

        [Fact]
        public void SetNextSystem_RowNotInRoute_IsNoOp()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            vm.RouteText = "Sol";
            vm.SaveCommand.Execute(null);
            var foreign = new RouteRowViewModel { SystemText = "Nowhere" };

            vm.SetNextSystemCommand.Execute(foreign);

            Assert.Equal(RowIcon.InProgress, vm.Rows[0].Icon);
        }

        [Fact]
        public void RestoreFromSettings_NoSavedRoute_IsNoOp()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);

            vm.RestoreFromSettings();

            Assert.False(vm.IsSaved);
            Assert.Empty(vm.Rows);
        }

        [Fact]
        public void RestoreFromSettings_PreviouslySavedRoute_RebuildsTable()
        {
            using var dir = new TempDirectory();
            var original = Create(dir);
            original.RouteText = "Sol\nAlpha Centauri";
            original.SaveCommand.Execute(null);

            var restored = Create(dir);
            restored.RestoreFromSettings();

            Assert.True(restored.IsSaved);
            Assert.Equal(2, restored.Rows.Count);
            Assert.Equal("Sol", restored.Rows[0].SystemText);
        }

        [Fact]
        public void RowEventTrigger_PlottedEvent_UpdatesMatchingRowThroughSequencer()
        {
            using var dir = new TempDirectory();
            var trigger = new ManualRowEventTrigger();
            var vm = Create(dir, trigger);
            vm.RouteText = "Sol\nAlpha Centauri";
            vm.SaveCommand.Execute(null);

            trigger.Fire(RowEventKind.Plotted, "Sol");

            Assert.Equal("Plotted", vm.Rows[0].Status);
        }

        [Fact]
        public void CopySystemCommand_NullRow_DoesNotThrow()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            StaThread.Run(() => vm.CopySystemCommand.Execute(null));
        }

        [Fact]
        public void CopySystemCommand_MarksRowAsClipboardSource()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            vm.RouteText = "Sol";
            vm.SaveCommand.Execute(null);

            StaThread.Run(() => vm.CopySystemCommand.Execute(vm.Rows[0]));

            Assert.True(vm.Rows[0].IsCopiedToClipboard);
        }

        [Fact]
        public void CopySystemCommand_DifferentRow_MovesClipboardSourceIcon()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            vm.RouteText = "Sol\nAlpha Centauri";
            vm.SaveCommand.Execute(null);

            StaThread.Run(() =>
            {
                vm.CopySystemCommand.Execute(vm.Rows[0]);
                vm.CopySystemCommand.Execute(vm.Rows[1]);
            });

            Assert.False(vm.Rows[0].IsCopiedToClipboard);
            Assert.True(vm.Rows[1].IsCopiedToClipboard);
        }

        [Fact]
        public void AutoCopyToClipboardEnabled_TurnedOn_CopiesCurrentInProgressRow()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            vm.RouteText = "Sol\nAlpha Centauri";
            vm.SaveCommand.Execute(null);

            StaThread.Run(() => vm.AutoCopyToClipboardEnabled = true);

            Assert.True(vm.Rows[0].IsCopiedToClipboard);
        }

        [Fact]
        public void AutoCopyToClipboardEnabled_NoInProgressRow_IsNoOp()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);

            StaThread.Run(() => vm.AutoCopyToClipboardEnabled = true);

            Assert.True(vm.AutoCopyToClipboardEnabled);
        }

        [Fact]
        public void AutoCopyToClipboardEnabled_DefaultsOff()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            Assert.False(vm.AutoCopyToClipboardEnabled);
        }

        [Fact]
        public void LiveCarrierLocationEvent_WhenAutoCopyEnabled_CopiesNextRow()
        {
            using var dir = new TempDirectory();
            var trigger = new ManualRowEventTrigger();
            var vm = Create(dir, trigger);
            vm.RouteText = "Sol\nAlpha Centauri";
            vm.SaveCommand.Execute(null);
            StaThread.Run(() => vm.AutoCopyToClipboardEnabled = true);

            StaThread.Run(() => trigger.Fire(RowEventKind.LiveCarrierLocation, "Sol", isLive: true));

            Assert.True(vm.Rows[1].IsCopiedToClipboard);
        }

        [Fact]
        public void LiveCarrierLocationEvent_WhenAutoCopyDisabled_DoesNothing()
        {
            using var dir = new TempDirectory();
            var trigger = new ManualRowEventTrigger();
            var vm = Create(dir, trigger);
            vm.RouteText = "Sol\nAlpha Centauri";
            vm.SaveCommand.Execute(null);

            trigger.Fire(RowEventKind.LiveCarrierLocation, "Sol", isLive: true);

            Assert.False(vm.Rows[1].IsCopiedToClipboard);
        }

        [Fact]
        public void LiveCarrierLocationEvent_RepeatedSystemName_CopiesRowAfterCurrentVisitNotFirstOccurrence()
        {
            // Regression test: matching purely by name (without skipping an already-Complete
            // earlier occurrence) would always resolve to the row after the *first* visit to a
            // repeated system, even on a later revisit - copying the wrong "next" system every
            // time the route revisits that name.
            using var dir = new TempDirectory();
            var trigger = new ManualRowEventTrigger();
            var vm = Create(dir, trigger);
            vm.RouteText = "Sol\nDeciat\nSol\nWolf 359";
            vm.SaveCommand.Execute(null);
            vm.Rows[0].Icon = RowIcon.Complete; // first (earlier) visit to Sol, already finished
            vm.Rows[1].Icon = RowIcon.Complete; // Deciat, also done
            StaThread.Run(() => vm.AutoCopyToClipboardEnabled = true);

            // The *second* Sol (index 2) is the current visit - not yet Complete at the moment
            // LiveCarrierLocation fires (it's ahead of the delayed Arrived transition).
            StaThread.Run(() => trigger.Fire(RowEventKind.LiveCarrierLocation, "Sol", isLive: true));

            Assert.True(vm.Rows[3].IsCopiedToClipboard); // Wolf 359, not Deciat
            Assert.False(vm.Rows[1].IsCopiedToClipboard);
        }

        [Fact]
        public void LiveCarrierLocationEvent_ArrivedAtLastRow_IsNoOp()
        {
            using var dir = new TempDirectory();
            var trigger = new ManualRowEventTrigger();
            var vm = Create(dir, trigger);
            vm.RouteText = "Sol";
            vm.SaveCommand.Execute(null);
            StaThread.Run(() => vm.AutoCopyToClipboardEnabled = true);

            StaThread.Run(() => trigger.Fire(RowEventKind.LiveCarrierLocation, "Sol", isLive: true));

            // No next row to copy - the initial "turn it on" copy of the in-progress row is the
            // only thing that should have happened.
            Assert.True(vm.Rows[0].IsCopiedToClipboard);
        }

        [Fact]
        public void Save_ReturnsImmediately_WhileEnrichmentLookupStillGated()
        {
            using var dir = new TempDirectory();
            var fake = new FakeStarSystemLookupService { Gate = new TaskCompletionSource() };
            var vm = Create(dir, starSystemLookupService: fake);
            vm.RouteText = "Sol";

            vm.SaveCommand.Execute(null); // must not block on the still-gated fake lookup

            Assert.True(vm.IsSaved);
            Assert.Single(vm.Rows);
            Assert.Null(vm.Rows[0].Distance);
            Assert.Null(vm.Rows[0].StarType);
        }

        [Fact]
        public async Task Save_RowsPopulate_OnceEnrichmentLookupCompletes()
        {
            using var dir = new TempDirectory();
            var fake = new FakeStarSystemLookupService { Gate = new TaskCompletionSource() };
            fake.StarTypes["Sol"] = "G (White-Yellow) Star";
            var vm = Create(dir, starSystemLookupService: fake);
            vm.RouteText = "Sol";

            vm.SaveCommand.Execute(null);
            fake.Gate.SetResult();
            await WaitUntilAsync(() => vm.Rows[0].StarType != null);

            Assert.Equal("G (White-Yellow) Star", vm.Rows[0].StarType);
        }

        [Fact]
        public async Task Save_OriginClosure_DrivesRow1Distance()
        {
            using var dir = new TempDirectory();
            var fake = new FakeStarSystemLookupService();
            fake.Coordinates["Origin System"] = new GalacticCoordinates(0, 0, 0);
            fake.Coordinates["Sol"] = new GalacticCoordinates(3, 4, 0);
            var vm = Create(dir, starSystemLookupService: fake, getOriginSystemName: () => "Origin System");
            vm.RouteText = "Sol";

            vm.SaveCommand.Execute(null);
            await WaitUntilAsync(() => vm.Rows[0].Distance != null);

            Assert.Equal(5.0, vm.Rows[0].Distance!.Value, precision: 6);
        }

        [Fact]
        public async Task Save_BeforePriorEnrichmentCompletes_CancelsTheStaleRun()
        {
            using var dir = new TempDirectory();
            var fake = new FakeStarSystemLookupService { Gate = new TaskCompletionSource() };
            fake.StarTypes["Alpha Centauri"] = "G (White-Yellow) Star";
            var vm = Create(dir, starSystemLookupService: fake);
            vm.RouteText = "Sol";
            vm.SaveCommand.Execute(null); // starts enrichment for "Sol", gated - never completes

            vm.RouteText = "Alpha Centauri";
            vm.SaveCommand.Execute(null); // supersedes the still-pending "Sol" run
            fake.Gate.SetResult();
            await WaitUntilAsync(() => vm.Rows[0].StarType != null);

            Assert.Equal("Alpha Centauri", vm.Rows[0].SystemText);
            Assert.Equal("G (White-Yellow) Star", vm.Rows[0].StarType);
        }

        [Fact]
        public void DataSeeded_AfterSaveAlreadyRendered_LiveRefreshesAlreadyDisplayedRowWithoutRestart()
        {
            // Regression test for the reported bug: a system resolved *after* Save's own
            // enrichment pass already ran (e.g. a live FSDTarget/NavRoute.json seed arriving later
            // in the session) used to only ever show up at the next Save/restore (an app restart)
            // - RouteViewModel's DataSeeded subscription (its debounce timer) is what fixes that.
            // Needs a real, pumped Dispatcher (DispatcherTimer.Tick only fires against one) - see
            // PumpDispatcherUntil - so this is deliberately the one real-time-dependent test for
            // this behaviour, mirroring the same accepted real-time-Timer testing gap
            // CarrierRouteJournalWatcherTests/ShipRouteJournalWatcherTests already document for
            // themselves, just paid for directly here instead of skipped, since this test exists
            // specifically to prove the live-refresh bug fix end to end.
            StaThread.Run(() =>
            {
                using var dir = new TempDirectory();
                var fake = new FakeStarSystemLookupService();
                var vm = Create(dir, starSystemLookupService: fake);
                vm.RouteText = "Sol";
                vm.SaveCommand.Execute(null);
                Assert.Null(vm.Rows[0].StarType); // "Sol" wasn't resolvable yet at Save time

                fake.SeedStarType("Sol", "G (White-Yellow) Star"); // e.g. a live FSDTarget seed

                PumpDispatcherUntil(() => vm.Rows[0].StarType != null, timeoutMs: 2000);

                Assert.Equal("G (White-Yellow) Star", vm.Rows[0].StarType);
            });
        }

        [Fact]
        public void DataSeeded_BurstOfSeeds_CollapsesIntoOneRefreshNotOnePerSeed()
        {
            StaThread.Run(() =>
            {
                using var dir = new TempDirectory();
                var fake = new FakeStarSystemLookupService();
                var vm = Create(dir, starSystemLookupService: fake);
                vm.RouteText = "Sol";
                vm.SaveCommand.Execute(null);
                var callCountBeforeBurst = fake.StarTypeCallOrder.Count;

                // A burst of seeds in a tight loop (e.g. one NavRoute.json read seeding many
                // systems) should debounce into a single refresh, not one per seed.
                for (var i = 0; i < 5; i++)
                {
                    fake.SeedStarType("Sol", "G (White-Yellow) Star");
                }

                PumpDispatcherUntil(() => fake.StarTypeCallOrder.Count > callCountBeforeBurst, timeoutMs: 2000);

                // Exactly one further GetMainStarTypeAsync call (the single debounced refresh),
                // not five.
                Assert.Equal(callCountBeforeBurst + 1, fake.StarTypeCallOrder.Count);
            });
        }

        /// <summary>
        /// Runs a nested Dispatcher message loop on the calling thread (which must already have
        /// one - see StaThread.Run + a DispatcherTimer constructed on this same thread, as
        /// RouteViewModel's own debounce timer is) until <paramref name="condition"/> is true or
        /// <paramref name="timeoutMs"/> elapses - the only way a DispatcherTimer's Tick (background
        /// priority) ever actually fires, since a plain synchronous test method has no message
        /// pump of its own.
        /// </summary>
        private static void PumpDispatcherUntil(Func<bool> condition, int timeoutMs)
        {
            var frame = new DispatcherFrame();
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            var pollTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
            pollTimer.Tick += (_, _) =>
            {
                if (condition() || DateTime.UtcNow > deadline)
                {
                    frame.Continue = false;
                }
            };
            pollTimer.Start();
            Dispatcher.PushFrame(frame);
            pollTimer.Stop();

            if (!condition())
            {
                throw new TimeoutException("Condition was not met within the timeout.");
            }
        }

        private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("Condition was not met within the timeout.");
                }

                await Task.Delay(10);
            }
        }
    }
}
