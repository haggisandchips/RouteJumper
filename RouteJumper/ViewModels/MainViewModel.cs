using RouteJumper.Common;
using RouteJumper.Sequencing;

namespace RouteJumper.ViewModels
{
    /// <summary>
    /// Top-level ViewModel for the main window: hosts the three tab ViewModels, and wires the
    /// shared row-event trigger between them (Roles' Captain journal watcher raises row events;
    /// Route's sequencer consumes them - see SPEC §11.5/§13.1), plus RouteViewModel.RouteSaved ->
    /// RolesViewModel.RefreshRouteForCurrentCaptain (see SPEC §4.5's Update) - neither tab
    /// ViewModel references the other directly; this class is the only place that does.
    /// </summary>
    public class MainViewModel : ObservableObject
    {
        public MainViewModel()
        {
            var routeEventTrigger = new ManualRowEventTrigger();

            RouteViewModel = new RouteViewModel(routeEventTrigger);
            RolesViewModel = new RolesViewModel(routeEventTrigger);
            ControlsViewModel = new ControlsViewModel();

            RouteViewModel.RouteSaved += (_, _) => RolesViewModel.RefreshRouteForCurrentCaptain();
        }

        public RouteViewModel RouteViewModel { get; }

        public RolesViewModel RolesViewModel { get; }

        public ControlsViewModel ControlsViewModel { get; }
    }
}
