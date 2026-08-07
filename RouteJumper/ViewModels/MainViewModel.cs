using RouteJumper.Common;

namespace RouteJumper.ViewModels
{
    /// <summary>
    /// Top-level ViewModel for the main window: just hosts the two tab ViewModels.
    /// </summary>
    public class MainViewModel : ObservableObject
    {
        public MainViewModel()
        {
            RouteViewModel = new RouteViewModel();
            RolesViewModel = new RolesViewModel();
            ControlsViewModel = new ControlsViewModel();
        }

        public RouteViewModel RouteViewModel { get; }

        public RolesViewModel RolesViewModel { get; }

        public ControlsViewModel ControlsViewModel { get; }
    }
}
