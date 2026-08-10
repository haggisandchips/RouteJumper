using System.Linq;
using RouteJumper.Models;
using RouteJumper.Services;
using RouteJumper.Tests.TestSupport;
using RouteJumper.ViewModels;
using Xunit;

namespace RouteJumper.Tests.ViewModels
{
    public class ControlsViewModelTests
    {
        // AppSettingsStore/AppConfigStore's public constructors reach real per-user AppData;
        // RouteJumper.Tests has InternalsVisibleTo access to their directory-scoped test
        // constructors instead, pointed at an isolated temp directory per test.
        private static ControlsViewModel Create(TempDirectory dir) => new(
            new AppSettingsStore(dir.Path),
            new EliteInstanceScanner(new AppConfigStore(dir.Path)),
            () => null,
            () => null,
            () => Task.CompletedTask);

        private static EliteInstanceViewModel FakeInstance(IntPtr handle) => new(
            processId: 1,
            commanderName: "Jameson",
            fid: "F1",
            journalFileName: "Journal.log",
            windowHandle: handle,
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

        [Fact]
        public void Constructor_LoadsDefaultKeyBindings()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);

            Assert.Equal(9, vm.KeyBindings.Count);
            var up = vm.KeyBindings.Single(b => b.Action == ControlAction.Up);
            Assert.Equal("Up Arrow", up.DisplayString);
        }

        [Fact]
        public void Constructor_Defaults_AutoPilotDelayAndAutoWait()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);

            Assert.Equal(5000, vm.AutoPilotDelayMs);
            Assert.Equal(300, vm.AutoWaitMs);
        }

        [Fact]
        public void AutoPilotDelayMs_Setter_ClampsToNonNegative()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);

            vm.AutoPilotDelayMs = -100;

            Assert.Equal(0, vm.AutoPilotDelayMs);
        }

        [Fact]
        public void AutoWaitMs_PersistsAndIsRestoredByAFreshInstance()
        {
            using var dir = new TempDirectory();
            Create(dir).AutoWaitMs = 750;

            var restored = Create(dir);

            Assert.Equal(750, restored.AutoWaitMs);
        }

        [Fact]
        public void AutoPilotDelayMs_PersistsAndIsRestoredByAFreshInstance()
        {
            using var dir = new TempDirectory();
            Create(dir).AutoPilotDelayMs = 9999;

            var restored = Create(dir);

            Assert.Equal(9999, restored.AutoPilotDelayMs);
        }

        [Fact]
        public void CompleteCapture_UpdatesBindingAndPersists()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            var binding = vm.KeyBindings.Single(b => b.Action == ControlAction.Up);
            binding.IsCapturing = true;

            vm.CompleteCapture(binding, System.Windows.Input.Key.J, System.Windows.Input.ModifierKeys.Control);

            Assert.False(binding.IsCapturing);
            Assert.Equal("Control+J", binding.StorageString);

            var restored = Create(dir);
            Assert.Equal("Control+J", restored.KeyBindings.Single(b => b.Action == ControlAction.Up).StorageString);
        }

        [Fact]
        public void CompleteCapture_Escape_LeavesBindingUnchanged()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            var binding = vm.KeyBindings.Single(b => b.Action == ControlAction.Up);
            var original = binding.StorageString;
            binding.IsCapturing = true;

            vm.CompleteCapture(binding, System.Windows.Input.Key.Escape, System.Windows.Input.ModifierKeys.None);

            Assert.False(binding.IsCapturing);
            Assert.Equal(original, binding.StorageString);
        }

        [Fact]
        public void CompleteCapture_BareModifierKey_LeavesCaptureModeActive()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            var binding = vm.KeyBindings.Single(b => b.Action == ControlAction.Up);
            binding.IsCapturing = true;

            vm.CompleteCapture(binding, System.Windows.Input.Key.LeftCtrl, System.Windows.Input.ModifierKeys.Control);

            Assert.True(binding.IsCapturing);
        }

        [Fact]
        public void SelectedInstance_NullOrNoWindowHandle_DisablesPlayAndStep()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);

            Assert.False(vm.PlayCommand.CanExecute(null));
            Assert.False(vm.StepCommand.CanExecute(null));
        }

        [Fact]
        public void CanPlay_RequiresInstanceAndMacroAndNonBlankTestFields()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            vm.SelectInstanceCommand.Execute(FakeInstance((IntPtr)1));

            Assert.False(vm.PlayCommand.CanExecute(null)); // no macro selected yet

            var macroVm = new RecordedMacroViewModel(new RecordedMacro { Id = Guid.NewGuid(), Name = "M", ScriptText = "UP" });
            vm.Macros.Add(macroVm);
            vm.SelectMacroCommand.Execute(macroVm);

            Assert.True(vm.PlayCommand.CanExecute(null));

            vm.NextSystemTestOverride = string.Empty;
            Assert.False(vm.PlayCommand.CanExecute(null));
            vm.NextSystemTestOverride = "Sol";

            vm.TritiumLoopsTestOverride = "   ";
            Assert.False(vm.PlayCommand.CanExecute(null));
        }

        [Fact]
        public void RecordCommand_DisabledWithoutSelectedInstanceWithWindowHandle()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);

            Assert.False(vm.RecordCommand.CanExecute(null));

            vm.SelectInstanceCommand.Execute(FakeInstance(IntPtr.Zero));
            Assert.False(vm.RecordCommand.CanExecute(null));

            vm.SelectInstanceCommand.Execute(FakeInstance((IntPtr)1));
            Assert.True(vm.RecordCommand.CanExecute(null));
        }

        [Fact]
        public void NewMacroCommand_CreatesEmptyMacroAndOpensEditor()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            var before = vm.Macros.Count;

            vm.NewMacroCommand.Execute(null);

            var created = Assert.Single(vm.Macros.Skip(before));
            Assert.Equal(string.Empty, created.ScriptText);
            Assert.Equal(string.Empty, created.SourceCommanderName);
            Assert.Equal(0, created.SourceProcessId);
            Assert.Same(created, vm.EditingMacro);
            Assert.Same(created, vm.SelectedMacro);
            Assert.True(vm.IsEditingMacro);
        }

        [Fact]
        public void NewMacroCommand_DoesNotRequireASelectedInstance()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);

            Assert.True(vm.NewMacroCommand.CanExecute(null));
        }

        [Fact]
        public void NewMacroCommand_CreatedMacroCanBeEditedAndPersists()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);

            vm.NewMacroCommand.Execute(null);
            vm.EditingMacro!.Name = "Hand-written";
            vm.EditingMacro!.ScriptText = "UP\nDOWN";

            var restored = Create(dir);
            var restoredMacro = Assert.Single(restored.Macros);
            Assert.Equal("Hand-written", restoredMacro.Name);
            Assert.Equal("UP\nDOWN", restoredMacro.ScriptText);
        }

        [Fact]
        public void EditMacroCommand_SelectsAndOpensEditor()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            var macroVm = new RecordedMacroViewModel(new RecordedMacro { Id = Guid.NewGuid(), Name = "M", ScriptText = "UP" });
            vm.Macros.Add(macroVm);

            vm.EditMacroCommand.Execute(macroVm);

            Assert.Same(macroVm, vm.EditingMacro);
            Assert.Same(macroVm, vm.SelectedMacro);
            Assert.True(vm.IsEditingMacro);
        }

        [Fact]
        public void CloseMacroEditorCommand_ClearsEditingMacro()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            var macroVm = new RecordedMacroViewModel(new RecordedMacro { Id = Guid.NewGuid(), Name = "M", ScriptText = "UP" });
            vm.Macros.Add(macroVm);
            vm.EditMacroCommand.Execute(macroVm);

            vm.CloseMacroEditorCommand.Execute(null);

            Assert.Null(vm.EditingMacro);
            Assert.False(vm.IsEditingMacro);
        }

        [Fact]
        public void DeleteMacro_RemovesFromListAndClearsSelectionAndEditingState()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            var macroVm = new RecordedMacroViewModel(new RecordedMacro { Id = Guid.NewGuid(), Name = "M", ScriptText = "UP" });
            vm.Macros.Add(macroVm);
            vm.SelectMacroCommand.Execute(macroVm);
            vm.EditMacroCommand.Execute(macroVm);

            RecordedMacroViewModel? deletedEventArg = null;
            vm.MacroDeleted += (_, m) => deletedEventArg = m;

            vm.DeleteMacroCommand.Execute(macroVm);

            Assert.DoesNotContain(macroVm, vm.Macros);
            Assert.Null(vm.SelectedMacro);
            Assert.Null(vm.EditingMacro);
            Assert.Same(macroVm, deletedEventArg);
        }

        [Fact]
        public void Macros_PersistAcrossInstances()
        {
            // WatchForMacroEdits (the PropertyChanged -> SaveMacros wiring) is only ever attached
            // by the constructor's LoadMacros pass or by StopRecording - not by adding directly
            // to the Macros collection - so this seeds a macro through AppSettingsStore first (as
            // a prior recording/save would have left it) and loads it via the constructor, the
            // same path a real previously-recorded macro takes.
            using var dir = new TempDirectory();
            var settings = new AppSettingsStore(dir.Path);
            settings.SetString("RecordedMacros", System.Text.Json.JsonSerializer.Serialize(new[]
            {
                new RecordedMacro { Id = Guid.NewGuid(), Name = "Refuel", ScriptText = "RIGHT_PANEL\nSELECT" }
            }));

            var vm = new ControlsViewModel(settings, new EliteInstanceScanner(new AppConfigStore(dir.Path)), () => null, () => null, () => Task.CompletedTask);
            var loadedMacro = Assert.Single(vm.Macros);

            loadedMacro.Name = "Refuel Renamed"; // triggers SaveMacros via WatchForMacroEdits

            var restored = Create(dir);

            var restoredMacro = Assert.Single(restored.Macros);
            Assert.Equal("Refuel Renamed", restoredMacro.Name);
            Assert.Equal("RIGHT_PANEL\nSELECT", restoredMacro.ScriptText);
        }

        [Fact]
        public void StepStatusText_NoEditingMacro_IsEmpty()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            Assert.Equal(string.Empty, vm.StepStatusText);
        }

        [Fact]
        public void StepStatusText_DescribesNextInstructionOfEditingMacro()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            var macroVm = new RecordedMacroViewModel(new RecordedMacro { Id = Guid.NewGuid(), Name = "M", ScriptText = "UP\nDOWN" });
            vm.Macros.Add(macroVm);

            vm.EditMacroCommand.Execute(macroVm);

            Assert.Equal("Next: UP", vm.StepStatusText);
        }

        [Fact]
        public void CanStep_RequiresEditingMacroWithAtLeastOneInstruction()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            vm.SelectInstanceCommand.Execute(FakeInstance((IntPtr)1));

            Assert.False(vm.StepCommand.CanExecute(null)); // no macro being edited

            var emptyMacro = new RecordedMacroViewModel(new RecordedMacro { Id = Guid.NewGuid(), Name = "Empty", ScriptText = "# just a comment" });
            vm.Macros.Add(emptyMacro);
            vm.EditMacroCommand.Execute(emptyMacro);
            Assert.False(vm.StepCommand.CanExecute(null)); // no leaf instructions

            var realMacro = new RecordedMacroViewModel(new RecordedMacro { Id = Guid.NewGuid(), Name = "Real", ScriptText = "UP" });
            vm.Macros.Add(realMacro);
            vm.EditMacroCommand.Execute(realMacro);
            Assert.True(vm.StepCommand.CanExecute(null));
        }

        [Fact]
        public void DismissPlaybackErrorCommand_ClearsMessage()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);

            vm.DismissPlaybackErrorCommand.Execute(null);

            Assert.Null(vm.PlaybackErrorMessage);
        }
    }
}
