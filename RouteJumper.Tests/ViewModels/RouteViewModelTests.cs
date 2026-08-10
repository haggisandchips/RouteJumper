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
        private static RouteViewModel Create(TempDirectory dir, IRowEventTrigger? trigger = null, Func<bool>? canEngageAutoPilot = null) =>
            new(new AppSettingsStore(dir.Path), trigger, canEngageAutoPilot);

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
    }
}
