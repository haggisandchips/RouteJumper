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
            ControlViewModel = new ControlViewModel();
        }

        public RouteViewModel RouteViewModel { get; }

        public ControlViewModel ControlViewModel { get; }
    }
}
