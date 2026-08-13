using System.Collections.ObjectModel;
using System.Windows;
using RouteJumper.Common;
using RouteJumper.Sequencing;
using RouteJumper.Services;

namespace RouteJumper.ViewModels
{
    /// <summary>
    /// ViewModel for the "Track" tab (Ship mode only): lists running EliteDangerous64.exe
    /// instances, lets the user re-scan for them, and lets the user pick a single instance to
    /// track - a slimmed-down RolesViewModel with one role instead of two, no macros, and no
    /// cargo-capacity gating, since Ship mode has no Auto Pilot to feed. (Re)assigning the tracked
    /// instance mirrors RolesViewModel.ToggleCaptain's reset-then-replay flow exactly, but against
    /// a ShipRouteJournalWatcher (StartJump/FSDJump) instead of a CarrierRouteJournalWatcher.
    ///
    /// Only ever active while Ship mode is the current TrackingMode - see <see cref="SetActive"/>,
    /// called by MainViewModel whenever the mode changes, mirroring RolesViewModel's own SetActive.
    /// This ViewModel's assignment (in memory and persisted) is untouched by activity state - only
    /// whether a watcher is actually running depends on it - so switching modes and back resumes
    /// tracking exactly where it left off.
    /// </summary>
    public class TrackViewModel : ObservableObject
    {
        private const string TrackedFidSettingKey = "TrackedFid";

        private readonly IEliteInstanceScanner _scanner;
        private readonly ManualRowEventTrigger _routeEventTrigger;
        private readonly AppSettingsStore _settings;

        private bool _isRefreshing;
        private string _statusText = string.Empty;
        private int? _trackedProcessId;
        private ShipRouteJournalWatcher? _shipWatcher;
        private bool _isActive;

        public TrackViewModel(
            ManualRowEventTrigger routeEventTrigger,
            AppSettingsStore settings,
            IEliteInstanceScanner scanner)
        {
            _routeEventTrigger = routeEventTrigger;
            _settings = settings;
            _scanner = scanner;

            Instances = new ObservableCollection<EliteInstanceViewModel>();
            RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsRefreshing);
            ToggleTrackedCommand = new RelayCommand<EliteInstanceViewModel>(ToggleTracked);
            CopyJournalFileNameCommand = new RelayCommand<string>(ClipboardCopyHelper.CopyWithPing);

            _ = RefreshAsync();
        }

        public ObservableCollection<EliteInstanceViewModel> Instances { get; }

        public AsyncRelayCommand RefreshCommand { get; }

        /// <summary>Assigns/unassigns the tracked instance to a card's instance.</summary>
        public RelayCommand<EliteInstanceViewModel> ToggleTrackedCommand { get; }

        /// <summary>Copies a card's journal filename to the clipboard and plays a confirmation ping - same simple, icon-free copy action as the Roles tab's equivalent.</summary>
        public RelayCommand<string> CopyJournalFileNameCommand { get; }

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

        /// <summary>
        /// Enables/disables this ViewModel's own watcher without touching the tracked
        /// assignment (in memory or persisted) - called by MainViewModel whenever TrackingMode
        /// changes. Turning active back on with an already-resolved tracked instance resumes it
        /// with a fresh Reset+catch-up, exactly as if it had just been assigned - the same
        /// "(re)assign = reset + catch-up" rule a fresh ToggleTracked assignment follows.
        /// Turning active off just stops the watcher; the route table is left exactly as
        /// displayed.
        /// </summary>
        public void SetActive(bool active)
        {
            if (_isActive == active)
            {
                return;
            }

            _isActive = active;

            if (!active)
            {
                StopWatch();
                return;
            }

            var instance = Instances.FirstOrDefault(i => i.ProcessId == _trackedProcessId);
            if (instance != null)
            {
                _routeEventTrigger.Fire(RowEventKind.Reset, string.Empty);
                StartWatch(instance);
            }
        }

        /// <summary>
        /// Public so ControlsView-style callers (or MainViewModel) can await a fresh Track-tab
        /// scan if ever needed - RefreshCommand still wraps this same method for the tab's own
        /// manual Refresh button.
        /// </summary>
        public async Task RefreshAsync()
        {
            IsRefreshing = true;
            try
            {
                var results = await _scanner.ScanAsync();

                Instances.Clear();
                foreach (var instance in results)
                {
                    instance.IsTracked = instance.ProcessId == _trackedProcessId;
                    Instances.Add(instance);
                }

                // A tracked instance that's no longer running loses the assignment in memory -
                // there's nothing left to watch. Only the in-memory ProcessId is cleared, not the
                // persisted FID (see RestoreTrackedInstanceFromSettings) - the process
                // disappearing shouldn't make the app forget who was tracked for next time.
                if (_trackedProcessId.HasValue && Instances.All(i => i.ProcessId != _trackedProcessId))
                {
                    _trackedProcessId = null;
                    StopWatch();
                }

                RestoreTrackedInstanceFromSettings(results);

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

        /// <summary>
        /// Called (via MainViewModel wiring RouteViewModel.RouteSaved) whenever the route is
        /// (re)built by Save - mirrors RolesViewModel.RefreshRouteForCurrentCaptain exactly,
        /// including the same "only while active" guard SetActive relies on: a Save can happen on
        /// the Route tab regardless of which mode is currently active, and must never wake a
        /// watcher for the inactive mode.
        /// </summary>
        public void RefreshRouteForCurrentTrackedInstance()
        {
            if (!_isActive || _trackedProcessId is null)
            {
                return;
            }

            var instance = Instances.FirstOrDefault(i => i.ProcessId == _trackedProcessId);
            if (instance != null)
            {
                StartWatch(instance);
            }
        }

        /// <summary>
        /// Re-assigns tracking to whichever scanned instance's FID matches what was persisted
        /// last time it was explicitly assigned - mirrors RolesViewModel.RestoreRolesFromSettings
        /// exactly, including matching by FID (stable across a game restart) rather than
        /// ProcessId, and only while currently unassigned in memory. The watch is only actually
        /// (re)started while this ViewModel is active - see SetActive - so restoring the
        /// assignment while Fleet Carrier mode is current doesn't spin up a Ship-mode watcher
        /// underneath it.
        /// </summary>
        private void RestoreTrackedInstanceFromSettings(IReadOnlyList<EliteInstanceViewModel> results)
        {
            if (_trackedProcessId is null &&
                _settings.GetString(TrackedFidSettingKey) is { } fid && IsRealFid(fid) &&
                results.FirstOrDefault(i => i.Fid == fid) is { } match)
            {
                match.IsTracked = true;
                _trackedProcessId = match.ProcessId;

                if (_isActive)
                {
                    _routeEventTrigger.Fire(RowEventKind.Reset, string.Empty);
                    StartWatch(match);
                }
            }
        }

        /// <summary>
        /// False for null/empty and for the literal "Unknown" EliteInstanceScanner falls back to
        /// when a Commander event hasn't been read yet - same rationale as RolesViewModel's own
        /// IsRealFid.
        /// </summary>
        private static bool IsRealFid(string? fid) => !string.IsNullOrEmpty(fid) && fid != "Unknown";

        private void ToggleTracked(EliteInstanceViewModel? instance)
        {
            if (instance is null)
            {
                return;
            }

            if (instance.IsTracked)
            {
                instance.IsTracked = false;
                _trackedProcessId = null;
                _settings.SetString(TrackedFidSettingKey, string.Empty);
                StopWatch();
                return;
            }

            foreach (var other in Instances)
            {
                if (other.IsTracked)
                {
                    other.IsTracked = false;
                }
            }

            instance.IsTracked = true;
            _trackedProcessId = instance.ProcessId;
            if (IsRealFid(instance.Fid))
            {
                _settings.SetString(TrackedFidSettingKey, instance.Fid);
            }

            if (_isActive)
            {
                // Same "reset before replay" rationale as RolesViewModel.ToggleCaptain: a
                // previous tracked instance's leftover progress must not linger. Fired
                // synchronously (already on the UI thread), so it's guaranteed to apply before
                // any of the new watcher's replayed events, which are always queued via the
                // dispatcher (see StartWatch).
                _routeEventTrigger.Fire(RowEventKind.Reset, string.Empty);
                StartWatch(instance);
            }

            if (RefreshCommand.CanExecute(null))
            {
                RefreshCommand.Execute(null);
            }
        }

        private void StartWatch(EliteInstanceViewModel instance)
        {
            StopWatch();

            if (instance.JournalFilePath is null)
            {
                // No matched journal - nothing to watch until the next reassignment/refresh
                // resolves one.
                return;
            }

            var dispatcher = Application.Current.Dispatcher;
            _shipWatcher = new ShipRouteJournalWatcher(
                instance.JournalFilePath,
                (kind, systemName, isLive, phaseEndUtc) => dispatcher.BeginInvoke(() => _routeEventTrigger.Fire(kind, systemName, isLive, phaseEndUtc)));

            _ = _shipWatcher.StartAsync();
        }

        private void StopWatch()
        {
            _shipWatcher?.Dispose();
            _shipWatcher = null;
        }
    }
}
