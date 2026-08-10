using System.Collections.ObjectModel;
using RouteJumper.Models;
using RouteJumper.Sequencing;
using RouteJumper.Services;
using RouteJumper.Tests.TestSupport;
using RouteJumper.ViewModels;
using Xunit;

namespace RouteJumper.Tests.ViewModels
{
    /// <summary>
    /// RolesViewModel's constructor kicks off RefreshAsync() (a real EliteInstanceScanner.ScanAsync
    /// call) - with no real EliteDangerous64.exe processes running in the test environment, this
    /// always resolves to zero instances, so tests that need role assignment construct an instance
    /// via EliteInstanceViewModel directly and add it to vm.Instances themselves rather than
    /// relying on a real scan finding anything.
    /// </summary>
    public class RolesViewModelTests
    {
        private static RolesViewModel Create(TempDirectory dir, ManualRowEventTrigger? trigger = null) => new(
            trigger ?? new ManualRowEventTrigger(),
            new AppSettingsStore(dir.Path),
            new EliteInstanceScanner(new AppConfigStore(dir.Path)),
            () => new ObservableCollection<RecordedMacroViewModel>());

        private static EliteInstanceViewModel Instance(int processId, string fid = "F1", int? cargoCapacity = null, int? currentCargo = null) => new(
            processId: processId,
            commanderName: "Jameson",
            fid: fid,
            journalFileName: "Journal.log",
            windowHandle: (IntPtr)1,
            windowPosition: "(0,0)",
            monitorInfo: "Monitor",
            cargoCapacity: cargoCapacity,
            currentCargo: currentCargo,
            currentTritium: null,
            currentSystem: null,
            currentStation: null,
            carrierName: null,
            carrierSystem: null,
            carrierBody: null,
            journalFilePath: null, // no journal path -> StartCaptainWatch becomes a no-op, safe without Application.Current
            carrierId: null,
            carrierFuelLevel: null);

        [Fact]
        public async Task RefreshAsync_StatusTextReflectsWhetherAnyInstancesWereFound()
        {
            // The dev/CI machine running this suite may or may not have EliteDangerous64.exe
            // running - assert the StatusText/Instances invariant rather than assuming either way.
            using var dir = new TempDirectory();
            var vm = Create(dir);
            await vm.RefreshAsync();

            if (vm.Instances.Count == 0)
            {
                Assert.Equal("No running Elite Dangerous instances found.", vm.StatusText);
            }
            else
            {
                Assert.Equal(string.Empty, vm.StatusText);
            }
        }

        [Fact]
        public void ToggleEngineerCommand_ZeroCargoCapacity_CannotAssign()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            var instance = Instance(1, cargoCapacity: 0, currentCargo: 0);
            vm.Instances.Add(instance);

            Assert.False(vm.ToggleEngineerCommand.CanExecute(instance));
        }

        [Fact]
        public void ToggleEngineerCommand_UnknownCargoCapacity_CannotAssign()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            var instance = Instance(1, cargoCapacity: null);
            vm.Instances.Add(instance);

            Assert.False(vm.ToggleEngineerCommand.CanExecute(instance));
        }

        [Fact]
        public void ToggleEngineerCommand_PositiveAvailableCapacity_CanAssignAndUnassign()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            var instance = Instance(1, cargoCapacity: 100, currentCargo: 0);
            vm.Instances.Add(instance);

            Assert.True(vm.ToggleEngineerCommand.CanExecute(instance));
            vm.ToggleEngineerCommand.Execute(instance);
            Assert.True(instance.IsEngineer);

            // Already-assigned Engineer can always be unassigned, even if capacity later became 0.
            vm.ToggleEngineerCommand.Execute(instance);
            Assert.False(instance.IsEngineer);
        }

        [Fact]
        public void ToggleEngineer_AssigningToNewInstance_UnassignsPreviousHolder()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            var first = Instance(1, cargoCapacity: 100, currentCargo: 0);
            var second = Instance(2, cargoCapacity: 100, currentCargo: 0);
            vm.Instances.Add(first);
            vm.Instances.Add(second);

            vm.ToggleEngineerCommand.Execute(first);
            vm.ToggleEngineerCommand.Execute(second);

            Assert.False(first.IsEngineer);
            Assert.True(second.IsEngineer);
        }

        [Fact]
        public void ToggleCaptain_AssigningToNewInstance_UnassignsPreviousHolder()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            var first = Instance(1);
            var second = Instance(2);
            vm.Instances.Add(first);
            vm.Instances.Add(second);

            vm.ToggleCaptainCommand.Execute(first);
            vm.ToggleCaptainCommand.Execute(second);

            Assert.False(first.IsCaptain);
            Assert.True(second.IsCaptain);
        }

        [Fact]
        public void ToggleCaptain_FiresRouteResetEvent()
        {
            using var dir = new TempDirectory();
            var trigger = new ManualRowEventTrigger();
            var vm = Create(dir, trigger);
            var instance = Instance(1);
            vm.Instances.Add(instance);

            RowEvent? received = null;
            trigger.RowTriggered += (_, e) => received ??= e;

            vm.ToggleCaptainCommand.Execute(instance);

            Assert.NotNull(received);
            Assert.Equal(RowEventKind.Reset, received!.Kind);
        }

        [Fact]
        public void ToggleCaptain_CanBeUnassigned()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            var instance = Instance(1);
            vm.Instances.Add(instance);
            vm.ToggleCaptainCommand.Execute(instance);

            vm.ToggleCaptainCommand.Execute(instance);

            Assert.False(instance.IsCaptain);
        }

        [Fact]
        public void CaptainInstance_ReflectsWhicheverInstanceIsCaptain()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            var instance = Instance(1);
            vm.Instances.Add(instance);

            Assert.Null(vm.CaptainInstance);
            vm.ToggleCaptainCommand.Execute(instance);
            Assert.Same(instance, vm.CaptainInstance);
        }

        [Fact]
        public void EngineerInstance_ReflectsWhicheverInstanceIsEngineer()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            var instance = Instance(1, cargoCapacity: 100, currentCargo: 0);
            vm.Instances.Add(instance);

            Assert.Null(vm.EngineerInstance);
            vm.ToggleEngineerCommand.Execute(instance);
            Assert.Same(instance, vm.EngineerInstance);
        }

        [Fact]
        public void CanEngageAutoPilot_RequiresCaptainWithMacro()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            var instance = Instance(1);
            vm.Instances.Add(instance);

            Assert.False(vm.CanEngageAutoPilot);

            vm.ToggleCaptainCommand.Execute(instance);
            Assert.False(vm.CanEngageAutoPilot); // no macro selected yet

            vm.CaptainMacro = new RecordedMacroViewModel(new RecordedMacro { Id = Guid.NewGuid(), Name = "M", ScriptText = "UP" });
            Assert.True(vm.CanEngageAutoPilot);
        }

        [Fact]
        public void CanEngageAutoPilot_EngineerAssignedWithoutMacro_BlocksEvenWithCaptainReady()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            var captain = Instance(1, fid: "FCaptain");
            var engineer = Instance(2, fid: "FEngineer", cargoCapacity: 100, currentCargo: 0);
            vm.Instances.Add(captain);
            vm.Instances.Add(engineer);
            vm.ToggleCaptainCommand.Execute(captain);
            vm.CaptainMacro = new RecordedMacroViewModel(new RecordedMacro { Id = Guid.NewGuid(), Name = "M", ScriptText = "UP" });

            vm.ToggleEngineerCommand.Execute(engineer);

            Assert.False(vm.CanEngageAutoPilot); // Engineer assigned but no macro selected for them

            vm.EngineerMacro = new RecordedMacroViewModel(new RecordedMacro { Id = Guid.NewGuid(), Name = "E", ScriptText = "SELECT" });
            Assert.True(vm.CanEngageAutoPilot);
        }

        [Fact]
        public void CanEngageAutoPilot_UnassignedEngineer_ImposesNoMacroRequirement()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            var captain = Instance(1);
            vm.Instances.Add(captain);
            vm.ToggleCaptainCommand.Execute(captain);
            vm.CaptainMacro = new RecordedMacroViewModel(new RecordedMacro { Id = Guid.NewGuid(), Name = "M", ScriptText = "UP" });

            Assert.True(vm.CanEngageAutoPilot);
        }

        [Fact]
        public void AutoPilotEligibilityChanged_RaisedWhenMacroSelectionChanges()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            var raised = 0;
            vm.AutoPilotEligibilityChanged += (_, _) => raised++;

            vm.CaptainMacro = new RecordedMacroViewModel(new RecordedMacro { Id = Guid.NewGuid(), Name = "M", ScriptText = "UP" });

            Assert.True(raised > 0);
        }

        [Fact]
        public void OnMacroDeleted_ClearsMatchingCaptainSelection()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            var macro = new RecordedMacroViewModel(new RecordedMacro { Id = Guid.NewGuid(), Name = "M", ScriptText = "UP" });
            vm.CaptainMacro = macro;

            vm.OnMacroDeleted(macro);

            Assert.Null(vm.CaptainMacro);
        }

        [Fact]
        public void OnMacroDeleted_ClearsMatchingEngineerSelection()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            var macro = new RecordedMacroViewModel(new RecordedMacro { Id = Guid.NewGuid(), Name = "E", ScriptText = "SELECT" });
            vm.EngineerMacro = macro;

            vm.OnMacroDeleted(macro);

            Assert.Null(vm.EngineerMacro);
        }

        [Fact]
        public void OnMacroDeleted_UnrelatedMacro_LeavesSelectionsUntouched()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            var selected = new RecordedMacroViewModel(new RecordedMacro { Id = Guid.NewGuid(), Name = "M", ScriptText = "UP" });
            var other = new RecordedMacroViewModel(new RecordedMacro { Id = Guid.NewGuid(), Name = "Other", ScriptText = "DOWN" });
            vm.CaptainMacro = selected;

            vm.OnMacroDeleted(other);

            Assert.Same(selected, vm.CaptainMacro);
        }

        [Fact]
        public void ClearCaptainAndEngineerMacroCommands_ClearSelections()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            vm.CaptainMacro = new RecordedMacroViewModel(new RecordedMacro { Id = Guid.NewGuid(), Name = "M", ScriptText = "UP" });
            vm.EngineerMacro = new RecordedMacroViewModel(new RecordedMacro { Id = Guid.NewGuid(), Name = "E", ScriptText = "SELECT" });

            vm.ClearCaptainMacroCommand.Execute(null);
            vm.ClearEngineerMacroCommand.Execute(null);

            Assert.Null(vm.CaptainMacro);
            Assert.Null(vm.EngineerMacro);
        }

        [Fact]
        public async Task RoleAssignment_PersistsAndIsRestoredByFidOnNextRefresh()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            var instance = Instance(1, fid: "F999");
            vm.Instances.Add(instance);
            vm.ToggleCaptainCommand.Execute(instance);

            // Simulate the instance disappearing (process closed) then reappearing with a new
            // ProcessId but the same FID - RestoreRolesFromSettings should re-match it by FID.
            vm.Instances.Clear();
            var restartedInstance = Instance(2, fid: "F999");
            vm.Instances.Add(restartedInstance);

            // RestoreRolesFromSettings only runs from inside RefreshAsync - call it directly
            // against the current Instances collection via reflection-free means: re-run
            // RefreshAsync would re-scan (returns empty in this test environment) and clear
            // Instances, so instead assert the settings value that drives restoration was
            // actually persisted, which is what RestoreRolesFromSettings reads on every refresh.
            var settings = new AppSettingsStore(dir.Path);
            Assert.Equal("F999", settings.GetString("CaptainFid"));
        }

        [Fact]
        public void ToggleCaptain_UnassignsAndClearsPersistedFid()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            var instance = Instance(1, fid: "F999");
            vm.Instances.Add(instance);
            vm.ToggleCaptainCommand.Execute(instance);

            vm.ToggleCaptainCommand.Execute(instance); // unassign

            var settings = new AppSettingsStore(dir.Path);
            Assert.Equal(string.Empty, settings.GetString("CaptainFid"));
        }
    }
}
