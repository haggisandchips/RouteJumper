using RouteJumper.Common;
using RouteJumper.Sequencing;
using RouteJumper.Services;

namespace RouteJumper.ViewModels
{
    /// <summary>
    /// Top-level ViewModel for the main window: hosts the three tab ViewModels, and wires the
    /// shared row-event trigger between them (Roles' Captain journal watcher raises row events;
    /// Route's sequencer consumes them), plus RouteViewModel.RouteSaved ->
    /// RolesViewModel.RefreshRouteForCurrentCaptain - neither tab ViewModel references the other
    /// directly; this class is the only place that does. Also owns the single AppSettingsStore
    /// both tabs persist to/restore from.
    /// </summary>
    public class MainViewModel : ObservableObject
    {
        public MainViewModel()
        {
            var settings = new AppSettingsStore();
            var routeEventTrigger = new ManualRowEventTrigger();

            RouteViewModel = new RouteViewModel(settings, routeEventTrigger);
            RolesViewModel = new RolesViewModel(routeEventTrigger, settings);
            ControlsViewModel = new ControlsViewModel();

            RouteViewModel.RouteSaved += (_, _) => RolesViewModel.RefreshRouteForCurrentCaptain();

            // Must run after the RouteSaved wiring above - see RouteViewModel.RestoreFromSettings.
            RouteViewModel.RestoreFromSettings();
        }

        public RouteViewModel RouteViewModel { get; }

        public RolesViewModel RolesViewModel { get; }

        public ControlsViewModel ControlsViewModel { get; }
    }
}
