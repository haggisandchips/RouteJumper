using RouteJumper.Sequencing;
using RouteJumper.Services;
using RouteJumper.Tests.TestSupport;
using RouteJumper.ViewModels;
using Xunit;

namespace RouteJumper.Tests.ViewModels
{
    /// <summary>
    /// Mirrors RolesViewModelTests' own conventions: a FakeEliteInstanceScanner is seeded with
    /// fabricated instances *before* constructing the ViewModel, so its constructor's own
    /// RefreshAsync() (and any later self-triggered rescan) deterministically sees them instead of
    /// racing a real (always-empty, in this environment) process scan. Instances are built with a
    /// null JournalFilePath so StartWatch becomes a no-op, safe without a real Application.Current.
    /// </summary>
    public class TrackViewModelTests
    {
        private static (TrackViewModel Vm, FakeEliteInstanceScanner Scanner) Create(
            TempDirectory dir,
            IReadOnlyList<EliteInstanceViewModel>? instances = null,
            ManualRowEventTrigger? trigger = null,
            bool active = true)
        {
            var scanner = new FakeEliteInstanceScanner { Results = instances ?? Array.Empty<EliteInstanceViewModel>() };
            var vm = new TrackViewModel(trigger ?? new ManualRowEventTrigger(), new AppSettingsStore(dir.Path), scanner);
            vm.SetActive(active);
            return (vm, scanner);
        }

        private static EliteInstanceViewModel Instance(int processId, string fid = "F1") => new(
            processId: processId,
            commanderName: "Jameson",
            fid: fid,
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
            journalFilePath: null, // no journal path -> StartWatch becomes a no-op, safe without Application.Current
            carrierId: null,
            carrierFuelLevel: null);

        [Fact]
        public void ToggleTracked_AssigningToNewInstance_UnassignsPreviousHolder()
        {
            using var dir = new TempDirectory();
            var first = Instance(1);
            var second = Instance(2);
            var (vm, _) = Create(dir, new[] { first, second });

            vm.ToggleTrackedCommand.Execute(first);
            vm.ToggleTrackedCommand.Execute(second);

            Assert.False(first.IsTracked);
            Assert.True(second.IsTracked);
        }

        [Fact]
        public void ToggleTracked_CanBeUnassigned()
        {
            using var dir = new TempDirectory();
            var instance = Instance(1);
            var (vm, _) = Create(dir, new[] { instance });
            vm.ToggleTrackedCommand.Execute(instance);

            vm.ToggleTrackedCommand.Execute(instance);

            Assert.False(instance.IsTracked);
        }

        [Fact]
        public void ToggleTracked_WhileActive_FiresRouteResetEvent()
        {
            using var dir = new TempDirectory();
            var trigger = new ManualRowEventTrigger();
            var instance = Instance(1);
            var (vm, _) = Create(dir, new[] { instance }, trigger, active: true);

            RowEvent? received = null;
            trigger.RowTriggered += (_, e) => received ??= e;

            vm.ToggleTrackedCommand.Execute(instance);

            Assert.NotNull(received);
            Assert.Equal(RowEventKind.Reset, received!.Kind);
        }

        [Fact]
        public void ToggleTracked_WhileInactive_DoesNotFireRouteResetEvent()
        {
            using var dir = new TempDirectory();
            var trigger = new ManualRowEventTrigger();
            var instance = Instance(1);
            var (vm, _) = Create(dir, new[] { instance }, trigger, active: false);

            var received = false;
            trigger.RowTriggered += (_, _) => received = true;

            vm.ToggleTrackedCommand.Execute(instance);

            Assert.False(received);
            Assert.True(instance.IsTracked); // assignment itself still happens while inactive
        }

        [Fact]
        public async Task TrackedAssignment_PersistsAndIsRestoredByFidOnNextRefresh()
        {
            using var dir = new TempDirectory();
            var instance = Instance(1, fid: "F999");
            var (vm, scanner) = Create(dir, new[] { instance });
            vm.ToggleTrackedCommand.Execute(instance);

            var settings = new AppSettingsStore(dir.Path);
            Assert.Equal("F999", settings.GetString("TrackedFid"));

            // Simulate the instance disappearing (process closed) then reappearing with a new
            // ProcessId but the same FID - the next refresh should re-match it by FID.
            var restartedInstance = Instance(2, fid: "F999");
            scanner.Results = new[] { restartedInstance };
            await vm.RefreshAsync();

            Assert.True(restartedInstance.IsTracked);
        }

        [Fact]
        public void ToggleTracked_UnassignsAndClearsPersistedFid()
        {
            using var dir = new TempDirectory();
            var instance = Instance(1, fid: "F999");
            var (vm, _) = Create(dir, new[] { instance });
            vm.ToggleTrackedCommand.Execute(instance);

            vm.ToggleTrackedCommand.Execute(instance); // unassign

            var settings = new AppSettingsStore(dir.Path);
            Assert.Equal(string.Empty, settings.GetString("TrackedFid"));
        }

        [Fact]
        public void SetActive_False_StopsWithoutClearingAssignment()
        {
            using var dir = new TempDirectory();
            var instance = Instance(1);
            var (vm, _) = Create(dir, new[] { instance }, active: true);
            vm.ToggleTrackedCommand.Execute(instance);

            vm.SetActive(false);

            // Assignment (in memory) is untouched by deactivation - only the watcher stops.
            Assert.True(instance.IsTracked);
        }

        [Fact]
        public void SetActive_TrueWithExistingAssignment_FiresFreshResetEvent()
        {
            using var dir = new TempDirectory();
            var trigger = new ManualRowEventTrigger();
            var instance = Instance(1);
            var (vm, _) = Create(dir, new[] { instance }, trigger, active: false);
            vm.ToggleTrackedCommand.Execute(instance); // assignment persists even while inactive

            var received = false;
            trigger.RowTriggered += (_, e) => received = e.Kind == RowEventKind.Reset;

            vm.SetActive(true);

            Assert.True(received);
        }

        [Fact]
        public void RefreshRouteForCurrentTrackedInstance_WhileInactive_IsNoOp()
        {
            // Guards against a Save on the Route tab (which raises this via MainViewModel
            // wiring, regardless of which mode is currently active) waking an inactive watcher.
            using var dir = new TempDirectory();
            var trigger = new ManualRowEventTrigger();
            var instance = Instance(1);
            var (vm, _) = Create(dir, new[] { instance }, trigger, active: true);
            vm.ToggleTrackedCommand.Execute(instance);
            vm.SetActive(false);

            var received = false;
            trigger.RowTriggered += (_, _) => received = true;

            vm.RefreshRouteForCurrentTrackedInstance();

            Assert.False(received);
        }
    }
}
