using System.Collections.ObjectModel;
using RouteJumper.Common;
using RouteJumper.Sequencing;

namespace RouteJumper.ViewModels
{
    /// <summary>
    /// ViewModel for the "Route" tab.
    /// </summary>
    public class RouteViewModel : ObservableObject
    {
        private readonly RouteSequencer _sequencer;

        private string _routeText = string.Empty;
        private bool _isSaved;
        private bool _isRunning;

        public RouteViewModel()
        {
            Rows = new ObservableCollection<RouteRowViewModel>();

            // The default pacing trigger: fires every 2 seconds. Additional triggers
            // (e.g. a ManualSequenceTrigger tied to some other UI event) can be attached
            // with _sequencer.AttachTrigger(...) without changing anything else here.
            _sequencer = new RouteSequencer();
            _sequencer.AttachTrigger(new TimerSequenceTrigger(TimeSpan.FromSeconds(2)));
            _sequencer.Completed += (_, _) => IsRunning = false;

            SaveCommand = new RelayCommand(Save, () => !string.IsNullOrWhiteSpace(RouteText));
            CancelCommand = new RelayCommand(Cancel);
            StartCommand = new RelayCommand(Start, () => IsSaved && !IsRunning && Rows.Count > 0);
            StopCommand = new RelayCommand(Stop, () => IsRunning);
        }

        public ObservableCollection<RouteRowViewModel> Rows { get; }

        public string RouteText
        {
            get => _routeText;
            set
            {
                if (SetProperty(ref _routeText, value))
                {
                    SaveCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>True once Save has been clicked - swaps the text box for the table.</summary>
        public bool IsSaved
        {
            get => _isSaved;
            private set
            {
                if (SetProperty(ref _isSaved, value))
                {
                    StartCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>True while the Start/Stop sequence is actively running.</summary>
        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (SetProperty(ref _isRunning, value))
                {
                    StartCommand.RaiseCanExecuteChanged();
                    StopCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public RelayCommand SaveCommand { get; }

        public RelayCommand CancelCommand { get; }

        public RelayCommand StartCommand { get; }

        public RelayCommand StopCommand { get; }

        private void Save()
        {
            var lines = RouteText
                .Replace("\r\n", "\n")
                .Split('\n')
                .ToList();

            // Drop a single trailing blank line caused by a final newline in the text box.
            if (lines.Count > 0 && lines[^1].Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            Rows.Clear();
            for (var i = 0; i < lines.Count; i++)
            {
                Rows.Add(new RouteRowViewModel
                {
                    Number = i + 1,
                    SystemText = lines[i]
                });
            }

            IsSaved = true;
        }

        private void Cancel()
        {
            RouteText = string.Empty;
        }

        private void Start()
        {
            IsRunning = true;
            _sequencer.Start(Rows);
        }

        private void Stop()
        {
            _sequencer.Stop();
            IsRunning = false;
        }
    }
}
