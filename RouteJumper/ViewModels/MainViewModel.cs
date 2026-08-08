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

            RouteViewModel = new RouteViewModel(settings, routeEventTrigger);
            RolesViewModel = new RolesViewModel(routeEventTrigger, settings, new EliteInstanceScanner(config));
            ControlsViewModel = new ControlsViewModel(
                settings,
                new EliteInstanceScanner(config),
                () => RouteViewModel.Rows.FirstOrDefault(r => r.Icon == RowIcon.InProgress)?.SystemText);

            RouteViewModel.RouteSaved += (_, _) => RolesViewModel.RefreshRouteForCurrentCaptain();

            // Must run after the RouteSaved wiring above - see RouteViewModel.RestoreFromSettings.
            RouteViewModel.RestoreFromSettings();
        }

        public RouteViewModel RouteViewModel { get; }

        public RolesViewModel RolesViewModel { get; }

        public ControlsViewModel ControlsViewModel { get; }
    }
}
