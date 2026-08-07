using System.Collections.ObjectModel;
using System.Media;
using System.Windows;
using RouteJumper.Common;
using RouteJumper.Models;
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

        public RouteViewModel(IRowEventTrigger? rowEventTrigger = null)
        {
            Rows = new ObservableCollection<RouteRowViewModel>();

            // The default pacing trigger: fires every 2 seconds. Additional triggers
            // (e.g. a ManualSequenceTrigger tied to some other UI event) can be attached
            // with _sequencer.AttachTrigger(...) without changing anything else here.
            _sequencer = new RouteSequencer();
            _sequencer.AttachTrigger(new TimerSequenceTrigger(TimeSpan.FromSeconds(2)));
            _sequencer.Completed += (_, _) => IsRunning = false;

            // Row-addressable events (e.g. from the Roles tab's Captain journal watcher) apply
            // directly to Rows, independently of the timer-paced Start/Stop sequence above.
            _sequencer.SetRows(Rows);
            if (rowEventTrigger != null)
            {
                _sequencer.AttachRowTrigger(rowEventTrigger);
            }

            SaveCommand = new RelayCommand(Save, () => !string.IsNullOrWhiteSpace(RouteText));
            CancelCommand = new RelayCommand(Cancel);
            EditCommand = new RelayCommand(Edit, () => IsSaved && !IsRunning);
            StartCommand = new RelayCommand(Start, () => IsSaved && !IsRunning && Rows.Count > 0);
            StopCommand = new RelayCommand(Stop, () => IsRunning);
            CopySystemCommand = new RelayCommand<string>(CopySystemToClipboard);
            SetNextSystemCommand = new RelayCommand<RouteRowViewModel>(SetNextSystem);
        }

        /// <summary>
        /// Raised at the end of every Save (first time or after Edit) - lets MainViewModel tell
        /// the Roles tab to re-derive the freshly-(re)built Rows from the currently-assigned
        /// Captain's journal, if any (see SPEC §4.5's Update). RouteViewModel deliberately has
        /// no reference to RolesViewModel itself - this event is the only coupling, same
        /// decoupling principle as the shared IRowEventTrigger.
        /// </summary>
        public event EventHandler? RouteSaved;

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
                    EditCommand.RaiseCanExecuteChanged();
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
                    EditCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public RelayCommand SaveCommand { get; }

        public RelayCommand CancelCommand { get; }

        /// <summary>Returns to the text box (with its contents unchanged) to revise the route.</summary>
        public RelayCommand EditCommand { get; }

        public RelayCommand StartCommand { get; }

        public RelayCommand StopCommand { get; }

        /// <summary>Copies a row's System text to the clipboard and plays a confirmation ping.</summary>
        public RelayCommand<string> CopySystemCommand { get; }

        /// <summary>
        /// Manual override (right-click a row -> "Set next system") for when automatic
        /// detection from a Captain's journal gets it wrong, or the carrier is off-route
        /// entirely: every row before the chosen one is marked Complete, the chosen row becomes
        /// the current (in-progress) row, and every row after it is reset to not-yet-started -
        /// regardless of whatever state they were previously in.
        /// </summary>
        public RelayCommand<RouteRowViewModel> SetNextSystemCommand { get; }

        private void Save()
        {
            // Each line is trimmed, and blank lines (including a trailing one caused by a
            // final newline, and any interior ones from pasted/malformed input) are dropped
            // entirely rather than becoming empty rows.
            var lines = RouteText
                .Replace("\r\n", "\n")
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToList();

            // Always a fresh set of rows - even if the text is identical to what was there
            // before, Save (first time or after Edit) never carries over old progress. If
            // RouteSaved ends up wired to a currently-assigned Captain, that progress gets
            // re-derived properly right after this; if not, row 1 defaults to "next" (below)
            // rather than the table looking inert.
            Rows.Clear();
            for (var i = 0; i < lines.Count; i++)
            {
                Rows.Add(new RouteRowViewModel
                {
                    Number = i + 1,
                    SystemText = lines[i]
                });
            }

            if (Rows.Count > 0)
            {
                Rows[0].Icon = RowIcon.InProgress;
            }

            IsSaved = true;
            RouteSaved?.Invoke(this, EventArgs.Empty);
        }

        private void Cancel()
        {
            RouteText = string.Empty;
        }

        private void Edit()
        {
            IsSaved = false;
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

        private static void CopySystemToClipboard(string? systemText)
        {
            if (string.IsNullOrEmpty(systemText))
            {
                return;
            }

            Clipboard.SetText(systemText);
            SystemSounds.Asterisk.Play();
        }

        private void SetNextSystem(RouteRowViewModel? targetRow)
        {
            if (targetRow is null)
            {
                return;
            }

            var targetIndex = Rows.IndexOf(targetRow);
            if (targetIndex < 0)
            {
                return;
            }

            for (var i = 0; i < Rows.Count; i++)
            {
                var row = Rows[i];
                row.Status = string.Empty;
                row.Icon = i < targetIndex ? RowIcon.Complete
                    : i == targetIndex ? RowIcon.InProgress
                    : RowIcon.None;
            }
        }
    }
}
