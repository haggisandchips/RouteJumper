using System.Collections.ObjectModel;
using System.Media;
using System.Windows;
using RouteJumper.Common;
using RouteJumper.Models;
using RouteJumper.Sequencing;
using RouteJumper.Services;

namespace RouteJumper.ViewModels
{
    /// <summary>
    /// ViewModel for the "Route" tab.
    /// </summary>
    public class RouteViewModel : ObservableObject
    {
        private const string RouteTextSettingKey = "RouteText";

        private readonly RouteSequencer _sequencer;
        private readonly AppSettingsStore _settings;

        private string _routeText = string.Empty;
        private bool _isSaved;
        private bool _isRunning;
        private bool _autoCopyToClipboardEnabled;

        public RouteViewModel(AppSettingsStore settings, IRowEventTrigger? rowEventTrigger = null)
        {
            _settings = settings;
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

                // A second, independent subscription to the same shared trigger - not routed
                // through RouteSequencer, since this isn't a route-status mutation (see
                // RowEventKind.LiveCarrierLocation). Both subscribers receive every event; each
                // simply ignores the kinds it doesn't care about.
                rowEventTrigger.RowTriggered += OnLiveCarrierLocation;
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

        /// <summary>
        /// "Auto Copy To Clipboard" (SPEC §5.6): when on, a live-observed CarrierLocation event
        /// (see RowEventKind.LiveCarrierLocation) copies the *next* row's System text to the
        /// clipboard automatically. Deliberately session-only - a plain in-memory flag, never
        /// read from or written to AppSettingsStore, so it always starts off on a fresh app
        /// launch but survives Edit/Save cycles within the same run (this ViewModel instance
        /// lives for the app's lifetime; only Rows gets rebuilt on Save).
        /// </summary>
        public bool AutoCopyToClipboardEnabled
        {
            get => _autoCopyToClipboardEnabled;
            set => SetProperty(ref _autoCopyToClipboardEnabled, value);
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
            _settings.SetString(RouteTextSettingKey, RouteText);
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

        /// <summary>
        /// Restores a previously-saved route from persistent storage on startup, if there is
        /// one - reuses Save() itself (rather than duplicating its row-building logic) so a
        /// restored route goes through exactly the same "fresh table, row 1 defaults to next"
        /// path a real Save does. Must be called after MainViewModel has wired RouteSaved (see
        /// its constructor) - a Captain restored moments later (RolesViewModel's own async
        /// startup scan) re-derives real progress on top of these rows via the same shared
        /// IRowEventTrigger regardless of whether RouteSaved had a subscriber yet at this exact
        /// point, so there's no strict ordering requirement on that front - but callers should
        /// still wire it first, for the (rare) case Refresh already completed by the time this
        /// runs.
        /// </summary>
        public void RestoreFromSettings()
        {
            var savedRouteText = _settings.GetString(RouteTextSettingKey);
            if (string.IsNullOrWhiteSpace(savedRouteText))
            {
                return;
            }

            RouteText = savedRouteText;
            Save();
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

        /// <summary>
        /// Drives "Auto Copy To Clipboard" (SPEC §5.6). Ignores every RowEvent kind except
        /// LiveCarrierLocation. Finds the row named by the event (the system the carrier just
        /// arrived at - matched by name alone, not by current Icon/Status, since this fires
        /// ahead of the delayed Arrived transition and so can't rely on that row already
        /// showing Complete) and, if a next row exists, copies *that* row's System text - no
        /// confirmation sound, unlike the manual click-to-copy, since this fires unattended.
        /// </summary>
        private void OnLiveCarrierLocation(object? sender, RowEvent e)
        {
            if (e.Kind != RowEventKind.LiveCarrierLocation || !AutoCopyToClipboardEnabled)
            {
                return;
            }

            var arrivedIndex = -1;
            for (var i = 0; i < Rows.Count; i++)
            {
                if (string.Equals(Rows[i].SystemText, e.SystemName, StringComparison.OrdinalIgnoreCase))
                {
                    arrivedIndex = i;
                    break;
                }
            }

            if (arrivedIndex < 0 || arrivedIndex + 1 >= Rows.Count)
            {
                return;
            }

            Clipboard.SetText(Rows[arrivedIndex + 1].SystemText);
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
