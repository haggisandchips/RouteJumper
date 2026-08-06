using System.Collections.ObjectModel;
using RouteJumper.Common;
using RouteJumper.Services;

namespace RouteJumper.ViewModels
{
    /// <summary>
    /// ViewModel for the "Control" tab: lists running EliteDangerous64.exe instances and lets
    /// the user re-scan for them.
    /// </summary>
    public class ControlViewModel : ObservableObject
    {
        private readonly EliteInstanceScanner _scanner = new();

        private bool _isRefreshing;
        private string _statusText = string.Empty;

        public ControlViewModel()
        {
            Instances = new ObservableCollection<EliteInstanceViewModel>();
            RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsRefreshing);

            _ = RefreshAsync();
        }

        public ObservableCollection<EliteInstanceViewModel> Instances { get; }

        public AsyncRelayCommand RefreshCommand { get; }

        public bool IsRefreshing
        {
            get => _isRefreshing;
            private set
            {
                if (SetProperty(ref _isRefreshing, value))
                {
                    RefreshCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>Empty-state / error message shown when there's nothing to list.</summary>
        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        private async Task RefreshAsync()
        {
            IsRefreshing = true;
            try
            {
                var results = await _scanner.ScanAsync();

                Instances.Clear();
                foreach (var instance in results)
                {
                    Instances.Add(instance);
                }

                StatusText = results.Count == 0
                    ? "No running Elite Dangerous instances found."
                    : string.Empty;
            }
            catch (Exception ex)
            {
                StatusText = $"Couldn't scan for Elite Dangerous instances: {ex.Message}";
            }
            finally
            {
                IsRefreshing = false;
            }
        }
    }
}
