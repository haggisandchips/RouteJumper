using RouteJumper.Common;
using RouteJumper.Models;
using RouteJumper.Services;

namespace RouteJumper.ViewModels
{
    /// <summary>
    /// One row of the Controls tab's key-binding list (SPEC §6.1): a fixed action, and the
    /// (re)bindable key/modifier combination currently assigned to it.
    /// </summary>
    public class KeyBindingViewModel : ObservableObject
    {
        private string _storageString;
        private bool _isCapturing;

        public KeyBindingViewModel(ControlAction action, string storageString)
        {
            Action = action;
            _storageString = storageString;
        }

        public ControlAction Action { get; }

        public string ActionName => Action.ToActionName();

        /// <summary>Canonical, parseable form (e.g. "Control+Shift+J") - what's persisted.</summary>
        public string StorageString
        {
            get => _storageString;
            set
            {
                if (SetProperty(ref _storageString, value))
                {
                    OnPropertyChanged(nameof(DisplayString));
                }
            }
        }

        /// <summary>Friendly form shown in the UI (e.g. "Ctrl+Shift+J").</summary>
        public string DisplayString => KeyBindingFormatter.ToDisplayString(StorageString);

        /// <summary>
        /// True while this row is waiting for the next keypress to capture as its new binding -
        /// set by ControlsViewModel.StartCaptureCommand, watched by ControlsView's code-behind
        /// PreviewKeyDown handler (view-layer input glue, same carve-out as RouteView.xaml.cs's
        /// focus handling), and cleared once a key is captured or capture is cancelled.
        /// </summary>
        public bool IsCapturing
        {
            get => _isCapturing;
            set => SetProperty(ref _isCapturing, value);
        }
    }
}
