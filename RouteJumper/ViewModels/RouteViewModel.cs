using System.Collections.ObjectModel;
using System.Linq;
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
        private bool _isAutoPilotRunning;
        private bool _autoCopyToClipboardEnabled;
        private RouteRowViewModel? _clipboardSourceRow;
        private uint _expectedClipboardSequenceNumber;

        public RouteViewModel(AppSettingsStore settings, IRowEventTrigger? rowEventTrigger = null)
        {
            _settings = settings;
            Rows = new ObservableCollection<RouteRowViewModel>();

            // Row-addressable events (from the Roles tab's Captain journal watcher) apply
            // directly to Rows.
            _sequencer = new RouteSequencer();
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
            EditCommand = new RelayCommand(Edit, () => IsSaved && !IsAutoPilotRunning);
            AutoPilotCommand = new RelayCommand(ToggleAutoPilot, () => IsSaved && Rows.Count > 0);
            CopySystemCommand = new RelayCommand<RouteRowViewModel>(CopySystemToClipboard);
            SetNextSystemCommand = new RelayCommand<RouteRowViewModel>(SetNextSystem);
        }

        /// <summary>
        /// Raised at the end of every Save (first time or after Edit) - lets MainViewModel tell
        /// the Roles tab to re-derive the freshly-(re)built Rows from the currently-assigned
        /// Captain's journal, if any. RouteViewModel deliberately has no reference to
        /// RolesViewModel itself - this event is the only coupling, same decoupling principle
        /// as the shared IRowEventTrigger.
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
                    AutoPilotCommand.RaiseCanExecuteChanged();
                    EditCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// "Auto Copy To Clipboard": when on, a live-observed CarrierLocation event
        /// (see RowEventKind.LiveCarrierLocation) copies the *next* row's System text to the
        /// clipboard automatically. Deliberately session-only - a plain in-memory flag, never
        /// read from or written to AppSettingsStore, so it always starts off on a fresh app
        /// launch but survives Edit/Save cycles within the same run (this ViewModel instance
        /// lives for the app's lifetime; only Rows gets rebuilt on Save).
        /// Turning it on also immediately copies whichever row is currently the "next system"
        /// (the in-progress row - see CopyCurrentInProgressSystemToClipboard), rather than
        /// waiting for the next live CarrierLocation event to eventually supply one.
        /// </summary>
        public bool AutoCopyToClipboardEnabled
        {
            get => _autoCopyToClipboardEnabled;
            set
            {
                if (SetProperty(ref _autoCopyToClipboardEnabled, value) && value)
                {
                    CopyCurrentInProgressSystemToClipboard();
                }
            }
        }

        /// <summary>
        /// Drives the Auto Pilot button's label ("Auto Pilot" when false, "Stop" when true) and
        /// disables Edit while engaged. Toggling it has no other effect - a placeholder for
        /// future automation, not a real running process.
        /// </summary>
        public bool IsAutoPilotRunning
        {
            get => _isAutoPilotRunning;
            private set
            {
                if (SetProperty(ref _isAutoPilotRunning, value))
                {
                    EditCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public RelayCommand SaveCommand { get; }

        public RelayCommand CancelCommand { get; }

        /// <summary>Returns to the text box (with its contents unchanged) to revise the route.</summary>
        public RelayCommand EditCommand { get; }

        /// <summary>Toggles IsAutoPilotRunning, flipping the button's label between "Auto Pilot" and "Stop".</summary>
        public RelayCommand AutoPilotCommand { get; }

        /// <summary>Copies a row's System text to the clipboard and plays a confirmation ping.</summary>
        public RelayCommand<RouteRowViewModel> CopySystemCommand { get; }

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
            // The old Rows' clipboard-source instance (if any) is about to be discarded along
            // with everything else Save rebuilds - nothing currently displayed should be
            // treated as the clipboard's source until a fresh copy action says otherwise.
            _clipboardSourceRow = null;

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

            IsAutoPilotRunning = false;
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

        private void ToggleAutoPilot()
        {
            IsAutoPilotRunning = !IsAutoPilotRunning;
        }

        private void CopySystemToClipboard(RouteRowViewModel? row)
        {
            if (row is null || string.IsNullOrEmpty(row.SystemText))
            {
                return;
            }

            ClipboardCopyHelper.CopyWithPing(row.SystemText);
            MarkRowAsClipboardSource(row);
        }

        /// <summary>
        /// Records which row's text was just copied to the clipboard: clears the icon off
        /// whichever row previously held it (if any, and if different),
        /// sets it on this one, and snapshots the Win32 clipboard sequence number so
        /// <see cref="OnSystemClipboardChanged"/> can tell "this WM_CLIPBOARDUPDATE is just
        /// confirming the write this call itself just made" apart from a genuinely different
        /// change - without that, the format-listener notification our own SetText call
        /// triggers would immediately clear the icon we just set.
        /// </summary>
        private void MarkRowAsClipboardSource(RouteRowViewModel row)
        {
            if (_clipboardSourceRow != null && _clipboardSourceRow != row)
            {
                _clipboardSourceRow.IsCopiedToClipboard = false;
            }

            _clipboardSourceRow = row;
            row.IsCopiedToClipboard = true;
            _expectedClipboardSequenceNumber = ClipboardMonitor.GetSequenceNumber();
        }

        /// <summary>
        /// Called (via MainWindow's WM_CLIPBOARDUPDATE hook) whenever the system clipboard's
        /// contents change, from any source. If the change doesn't match
        /// what this ViewModel itself just wrote (see <see cref="MarkRowAsClipboardSource"/>),
        /// the currently-shown clipboard icon is cleared - covers both an external app
        /// overwriting the clipboard and this app doing something else with it later that
        /// doesn't go through the tracked copy paths.
        /// </summary>
        public void OnSystemClipboardChanged()
        {
            if (_clipboardSourceRow is null)
            {
                return;
            }

            if (ClipboardMonitor.GetSequenceNumber() == _expectedClipboardSequenceNumber)
            {
                return;
            }

            _clipboardSourceRow.IsCopiedToClipboard = false;
            _clipboardSourceRow = null;
        }

        /// <summary>
        /// Drives "Auto Copy To Clipboard". Ignores every RowEvent kind except
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

            var nextRow = Rows[arrivedIndex + 1];
            Clipboard.SetText(nextRow.SystemText);
            MarkRowAsClipboardSource(nextRow);
        }

        /// <summary>
        /// "The next system" outside of a live arrival event (see AutoCopyToClipboardEnabled's
        /// setter) is whichever row is currently the route's one in-progress row - the row
        /// already displayed as "next" via its icon, whether that's row 1 of a freshly
        /// Saved route, or wherever a Captain's journal / manual "Set next system" override has
        /// since moved it to. A no-op if there is no in-progress row at all (an empty route, or
        /// one that's already fully Complete).
        /// </summary>
        private void CopyCurrentInProgressSystemToClipboard()
        {
            var currentRow = Rows.FirstOrDefault(r => r.Icon == RowIcon.InProgress);
            if (currentRow != null)
            {
                Clipboard.SetText(currentRow.SystemText);
                MarkRowAsClipboardSource(currentRow);
            }
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
