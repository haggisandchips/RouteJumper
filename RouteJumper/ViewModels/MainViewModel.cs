using System.Linq;
using RouteJumper.Common;
using RouteJumper.Models;
using RouteJumper.Sequencing;
using RouteJumper.Services;

namespace RouteJumper.ViewModels
{
    /// <summary>
    /// Top-level ViewModel for the main window: hosts the three tab ViewModels, and wires the
    /// shared row-event trigger between them (Roles' Captain journal watcher raises row events;
    /// Route's sequencer consumes them), plus RouteViewModel.RouteSaved ->
    /// RolesViewModel.RefreshRouteForCurrentCaptain, and a read-only closure over
    /// RouteViewModel.Rows so ControlsViewModel can resolve a macro's "next system" paste
    /// placeholder without needing a real reference to RouteViewModel - neither tab ViewModel
    /// references another directly; this class is the only place that does. Also owns the
    /// single AppSettingsStore both tabs persist to/restore from, and the single AppConfigStore
    /// both Roles' and Controls' own independent EliteInstanceScanner instances read the
    /// journal folder from.
    /// </summary>
    public class MainViewModel : ObservableObject
    {
        public MainViewModel()
        {
            var settings = new AppSettingsStore();
            var config = new AppConfigStore();
            var routeEventTrigger = new ManualRowEventTrigger();

            // The RolesViewModel/ControlsViewModel property dereferences below are guaranteed
            // safe despite still being unassigned at this exact statement - these closures are
            // only ever invoked later, once the whole constructor (and both assignments) has
            // completed; the nullable analyzer can't see that far ahead through a deferred
            // lambda, hence the null-forgiving operators.
            RouteViewModel = new RouteViewModel(settings, routeEventTrigger, () => RolesViewModel!.CanEngageAutoPilot);
            RolesViewModel = new RolesViewModel(
                routeEventTrigger,
                settings,
                new EliteInstanceScanner(config),
                () => ControlsViewModel!.Macros);
            ControlsViewModel = new ControlsViewModel(
                settings,
                new EliteInstanceScanner(config),
                () => RouteViewModel.Rows.FirstOrDefault(r => r.Icon == RowIcon.InProgress)?.SystemText);

            RouteViewModel.RouteSaved += (_, _) => RolesViewModel.RefreshRouteForCurrentCaptain();
            RolesViewModel.AutoPilotEligibilityChanged += (_, _) => RouteViewModel.RaiseAutoPilotEligibilityChanged();
            ControlsViewModel.MacroDeleted += (_, macro) => RolesViewModel.OnMacroDeleted(macro);

            // Must run after the RouteSaved wiring above - see RouteViewModel.RestoreFromSettings.
            RouteViewModel.RestoreFromSettings();
        }

        public RouteViewModel RouteViewModel { get; }

        public RolesViewModel RolesViewModel { get; }

        public ControlsViewModel ControlsViewModel { get; }
    }
}
