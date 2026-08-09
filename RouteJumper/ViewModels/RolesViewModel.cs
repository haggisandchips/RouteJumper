using System.Collections.ObjectModel;
using System.Windows;
using RouteJumper.Common;
using RouteJumper.Sequencing;
using RouteJumper.Services;

namespace RouteJumper.ViewModels
{
    /// <summary>
    /// ViewModel for the "Roles" tab: lists running EliteDangerous64.exe instances, lets the
    /// user re-scan for them, and lets the user assign the Captain/Engineer roles.
    /// </summary>
    public class RolesViewModel : ObservableObject
    {
        private const string CaptainFidSettingKey = "CaptainFid";
        private const string EngineerFidSettingKey = "EngineerFid";
        private const string CaptainMacroIdSettingKey = "CaptainMacroId";
        private const string EngineerMacroIdSettingKey = "EngineerMacroId";

        private readonly EliteInstanceScanner _scanner;
        private readonly ManualRowEventTrigger _routeEventTrigger;
        private readonly AppSettingsStore _settings;
        private readonly Func<ObservableCollection<RecordedMacroViewModel>> _getMacros;

        private bool _isRefreshing;
        private string _statusText = string.Empty;
        private int? _captainProcessId;
        private int? _engineerProcessId;
        private CarrierRouteJournalWatcher? _captainWatcher;
        private RecordedMacroViewModel? _captainMacro;
        private RecordedMacroViewModel? _engineerMacro;

        /// <summary>
        /// <paramref name="getMacros"/> resolves the Controls tab's live macro list (SPEC §6.4) -
        /// supplied by MainViewModel as a closure over ControlsViewModel.Macros, the same
        /// one-way, event-free bridging pattern already used to reach RouteViewModel.Rows from
        /// ControlsViewModel, since this ViewModel has no reference to ControlsViewModel itself.
        /// </summary>
        public RolesViewModel(
            ManualRowEventTrigger routeEventTrigger,
            AppSettingsStore settings,
            EliteInstanceScanner scanner,
            Func<ObservableCollection<RecordedMacroViewModel>> getMacros)
        {
            _routeEventTrigger = routeEventTrigger;
            _settings = settings;
            _scanner = scanner;
            _getMacros = getMacros;

            Instances = new ObservableCollection<EliteInstanceViewModel>();
            RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsRefreshing);
            ToggleCaptainCommand = new RelayCommand<EliteInstanceViewModel>(ToggleCaptain);
            ToggleEngineerCommand = new RelayCommand<EliteInstanceViewModel>(ToggleEngineer, CanToggleEngineer);
            CopyJournalFileNameCommand = new RelayCommand<string>(ClipboardCopyHelper.CopyWithPing);
            ClearCaptainMacroCommand = new RelayCommand(() => CaptainMacro = null);
            ClearEngineerMacroCommand = new RelayCommand(() => EngineerMacro = null);

            _ = RefreshAsync();
        }

        /// <summary>
        /// Raised whenever anything CanEngageAutoPilot depends on changes - lets MainViewModel
        /// tell the Route tab's Auto Pilot button to re-check whether it should be enabled,
        /// without RolesViewModel needing a reference to RouteViewModel itself (same decoupling
        /// principle as RouteViewModel.RouteSaved, just in the other direction).
        /// </summary>
        public event EventHandler? AutoPilotEligibilityChanged;

        public ObservableCollection<EliteInstanceViewModel> Instances { get; }

        /// <summary>The Controls tab's live recorded-macro list, for the Captain/Engineer macro pickers below.</summary>
        public ObservableCollection<RecordedMacroViewModel> AvailableMacros => _getMacros();

        /// <summary>The instance currently holding the Captain role, if any and still running - used by AutoPilotController (Route tab's Auto Pilot) to know which window to plot into.</summary>
        public EliteInstanceViewModel? CaptainInstance => Instances.FirstOrDefault(i => i.IsCaptain);

        /// <summary>The instance currently holding the Engineer role, if any and still running - used by ControlsViewModel to resolve the TRITIUM_LOOPS macro placeholder against the Engineer's own cargo/carrier data.</summary>
        public EliteInstanceViewModel? EngineerInstance => Instances.FirstOrDefault(i => i.IsEngineer);

        public AsyncRelayCommand RefreshCommand { get; }

        /// <summary>Assigns/unassigns the Captain role to a card's instance.</summary>
        public RelayCommand<EliteInstanceViewModel> ToggleCaptainCommand { get; }

        /// <summary>Assigns/unassigns the Engineer role to a card's instance.</summary>
        public RelayCommand<EliteInstanceViewModel> ToggleEngineerCommand { get; }

        /// <summary>
        /// Copies a card's journal filename to the clipboard and plays a confirmation ping - no
        /// visual indicator afterward, unlike the Route tab's equivalent (SPEC §4.6); this is a
        /// simpler, icon-free copy action.
        /// </summary>
        public RelayCommand<string> CopyJournalFileNameCommand { get; }

        /// <summary>Clears CaptainMacro back to unselected.</summary>
        public RelayCommand ClearCaptainMacroCommand { get; }

        /// <summary>Clears EngineerMacro back to unselected.</summary>
        public RelayCommand ClearEngineerMacroCommand { get; }

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

        /// <summary>The macro Auto Pilot uses to have the Captain plot the next route - see CanEngageAutoPilot.</summary>
        public RecordedMacroViewModel? CaptainMacro
        {
            get => _captainMacro;
            set
            {
                if (SetProperty(ref _captainMacro, value))
                {
                    _settings.SetString(CaptainMacroIdSettingKey, value?.Id.ToString() ?? string.Empty);
                    NotifyAutoPilotEligibilityChanged();
                }
            }
        }

        /// <summary>The macro Auto Pilot uses to have the Engineer deposit fuel - see CanEngageAutoPilot.</summary>
        public RecordedMacroViewModel? EngineerMacro
        {
            get => _engineerMacro;
            set
            {
                if (SetProperty(ref _engineerMacro, value))
                {
                    _settings.SetString(EngineerMacroIdSettingKey, value?.Id.ToString() ?? string.Empty);
                    NotifyAutoPilotEligibilityChanged();
                }
            }
        }

        /// <summary>
        /// Auto Pilot (Route tab) requires a Captain to be assigned with a macro selected for
        /// them, and - only if Engineer is *also* currently assigned - a macro selected for
        /// Engineer too. An unassigned Engineer imposes no macro requirement of its own.
        /// </summary>
        public bool CanEngageAutoPilot =>
            _captainProcessId.HasValue && CaptainMacro != null &&
            (!_engineerProcessId.HasValue || EngineerMacro != null);

        private void NotifyAutoPilotEligibilityChanged()
        {
            OnPropertyChanged(nameof(CanEngageAutoPilot));
            AutoPilotEligibilityChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Called (via MainViewModel wiring ControlsViewModel.MacroDeleted) whenever a macro is
        /// deleted on the Controls tab - clears it from whichever role selection (if any) still
        /// referenced it, rather than leaving a dangling selection the ComboBox can no longer
        /// display.
        /// </summary>
        public void OnMacroDeleted(RecordedMacroViewModel macro)
        {
            if (ReferenceEquals(CaptainMacro, macro))
            {
                CaptainMacro = null;
            }

            if (ReferenceEquals(EngineerMacro, macro))
            {
                EngineerMacro = null;
            }
        }

        /// <summary>
        /// Public so ControlsViewModel can await a fresh Roles-tab scan (via a closure supplied
        /// by MainViewModel) before resolving the TRITIUM_LOOPS macro placeholder against the
        /// Engineer's current cargo/carrier-fuel data - RefreshCommand still wraps this same
        /// method for the tab's own manual Refresh button.
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
                    instance.IsCaptain = instance.ProcessId == _captainProcessId;
                    instance.IsEngineer = instance.ProcessId == _engineerProcessId;
                    Instances.Add(instance);
                }

                // A role holder that's no longer running loses the role - there's nothing left
                // to monitor or to gate Engineer eligibility against. Note this only clears the
                // in-memory ProcessId, not the persisted FID (see RestoreRolesFromSettings) -
                // the process disappearing (e.g. the game was closed) shouldn't make the app
                // forget who held the role for next time, only that nobody currently does.
                if (_captainProcessId.HasValue && Instances.All(i => i.ProcessId != _captainProcessId))
                {
                    _captainProcessId = null;
                    StopCaptainWatch();
                }

                if (_engineerProcessId.HasValue && Instances.All(i => i.ProcessId != _engineerProcessId))
                {
                    _engineerProcessId = null;
                }

                RestoreRolesFromSettings(results);
                RestoreMacroSelectionFromSettings();
                NotifyAutoPilotEligibilityChanged();

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
        /// Re-selects the Captain/Engineer macro that was persisted the last time each was
        /// explicitly chosen (by Id, not Name - see RecordedMacro.Id) - runs on every refresh,
        /// not just once at startup, the same "only while currently unselected in memory"
        /// convention RestoreRolesFromSettings already follows, since a freshly-recorded macro
        /// (added after this ViewModel's own construction) still needs to be resolvable once it
        /// exists.
        /// </summary>
        private void RestoreMacroSelectionFromSettings()
        {
            if (CaptainMacro is null && TryGetSettingGuid(CaptainMacroIdSettingKey, out var captainMacroId))
            {
                var match = AvailableMacros.FirstOrDefault(m => m.Id == captainMacroId);
                if (match != null)
                {
                    _captainMacro = match;
                    OnPropertyChanged(nameof(CaptainMacro));
                }
            }

            if (EngineerMacro is null && TryGetSettingGuid(EngineerMacroIdSettingKey, out var engineerMacroId))
            {
                var match = AvailableMacros.FirstOrDefault(m => m.Id == engineerMacroId);
                if (match != null)
                {
                    _engineerMacro = match;
                    OnPropertyChanged(nameof(EngineerMacro));
                }
            }
        }

        private bool TryGetSettingGuid(string key, out Guid value)
        {
            value = Guid.Empty;
            return _settings.GetString(key) is { Length: > 0 } text && Guid.TryParse(text, out value);
        }

        /// <summary>
        /// Called (via MainViewModel wiring RouteViewModel.RouteSaved) whenever the route is
        /// (re)built by Save, first time or after Edit. If a Captain is currently assigned,
        /// restarts that instance's journal watch so the freshly-(re)built route is re-derived
        /// from their real progress from scratch, exactly as if Captain had just been assigned -
        /// RouteViewModel.Save already gives every row a clean, freshly-constructed starting
        /// state, so there's no separate Reset to fire here the way ToggleCaptain needs one.
        /// If no Captain is assigned, this is a no-op - Save's own default (row 1 marked next)
        /// is left standing.
        /// </summary>
        public void RefreshRouteForCurrentCaptain()
        {
            if (_captainProcessId is null)
            {
                return;
            }

            var instance = Instances.FirstOrDefault(i => i.ProcessId == _captainProcessId);
            if (instance != null)
            {
                StartCaptainWatch(instance);
            }
        }

        /// <summary>
        /// Re-assigns Captain/Engineer to whichever scanned instance's FID matches what was
        /// persisted last time that role was explicitly assigned - runs on every refresh (not
        /// just once at startup), but only when the role is currently unassigned in memory, so
        /// it naturally covers both "app just launched" and "the role holder's game process
        /// restarted mid-session" (new ProcessId, same FID) without needing separate logic for
        /// either. A commander's FID is stable across restarts; ProcessId is not, which is why
        /// this matches on FID rather than trying to persist/restore ProcessId directly.
        /// Deliberately bypasses CanBeEngineer for the Engineer restore - this is re-applying a
        /// previously-valid, user-made assignment, not validating a brand new one, and cargo
        /// capacity is commonly still unknown this early after a scan.
        /// </summary>
        private void RestoreRolesFromSettings(IReadOnlyList<EliteInstanceViewModel> results)
        {
            if (_captainProcessId is null &&
                _settings.GetString(CaptainFidSettingKey) is { } captainFid && IsRealFid(captainFid) &&
                results.FirstOrDefault(i => i.Fid == captainFid) is { } captainMatch)
            {
                captainMatch.IsCaptain = true;
                _captainProcessId = captainMatch.ProcessId;
                _routeEventTrigger.Fire(RowEventKind.Reset, string.Empty);
                StartCaptainWatch(captainMatch);
            }

            if (_engineerProcessId is null &&
                _settings.GetString(EngineerFidSettingKey) is { } engineerFid && IsRealFid(engineerFid) &&
                results.FirstOrDefault(i => i.Fid == engineerFid) is { } engineerMatch)
            {
                engineerMatch.IsEngineer = true;
                _engineerProcessId = engineerMatch.ProcessId;
            }
        }

        /// <summary>
        /// False for null/empty and for the literal "Unknown" EliteInstanceScanner falls back to
        /// when a Commander event hasn't been read yet - persisting or matching on that value
        /// would let an unrelated instance (also still showing "Unknown") match a role it was
        /// never actually assigned.
        /// </summary>
        private static bool IsRealFid(string? fid) => !string.IsNullOrEmpty(fid) && fid != "Unknown";

        private void ToggleCaptain(EliteInstanceViewModel? instance)
        {
            if (instance is null)
            {
                return;
            }

            if (instance.IsCaptain)
            {
                instance.IsCaptain = false;
                _captainProcessId = null;
                _settings.SetString(CaptainFidSettingKey, string.Empty);
                StopCaptainWatch();
                NotifyAutoPilotEligibilityChanged();
                return;
            }

            foreach (var other in Instances)
            {
                if (other.IsCaptain)
                {
                    other.IsCaptain = false;
                }
            }

            instance.IsCaptain = true;
            _captainProcessId = instance.ProcessId;
            if (IsRealFid(instance.Fid))
            {
                _settings.SetString(CaptainFidSettingKey, instance.Fid);
            }

            // Assigning Captain to an instance starts the route from a clean slate before
            // replaying that instance's journal - a previous Captain's leftover progress must
            // not linger and interfere with matching. Fired synchronously (we're already on the
            // UI thread here), so it's guaranteed to apply before any of the new watcher's
            // replayed events, which are always queued via the dispatcher (see
            // StartCaptainWatch) and so can never run ahead of this.
            _routeEventTrigger.Fire(RowEventKind.Reset, string.Empty);

            StartCaptainWatch(instance);
            NotifyAutoPilotEligibilityChanged();

            // Assigning Captain also triggers a reread to refresh this tab - that same reread
            // is what replays the carrier's journal history into the route.
            if (RefreshCommand.CanExecute(null))
            {
                RefreshCommand.Execute(null);
            }
        }

        private void ToggleEngineer(EliteInstanceViewModel? instance)
        {
            if (instance is null)
            {
                return;
            }

            if (instance.IsEngineer)
            {
                instance.IsEngineer = false;
                _engineerProcessId = null;
                _settings.SetString(EngineerFidSettingKey, string.Empty);
                NotifyAutoPilotEligibilityChanged();
                return;
            }

            if (!instance.CanBeEngineer)
            {
                return;
            }

            foreach (var other in Instances)
            {
                if (other.IsEngineer)
                {
                    other.IsEngineer = false;
                }
            }

            instance.IsEngineer = true;
            _engineerProcessId = instance.ProcessId;
            if (IsRealFid(instance.Fid))
            {
                _settings.SetString(EngineerFidSettingKey, instance.Fid);
            }

            NotifyAutoPilotEligibilityChanged();
        }

        private static bool CanToggleEngineer(EliteInstanceViewModel? instance) =>
            instance != null && (instance.IsEngineer || instance.CanBeEngineer);

        private void StartCaptainWatch(EliteInstanceViewModel instance)
        {
            StopCaptainWatch();

            if (instance.JournalFilePath is null || instance.CarrierId is null)
            {
                // No matched journal, or no carrier established yet this session - nothing to
                // watch until the next reassignment/refresh resolves one.
                return;
            }

            var dispatcher = Application.Current.Dispatcher;
            _captainWatcher = new CarrierRouteJournalWatcher(
                instance.JournalFilePath,
                instance.CarrierId.Value,
                (kind, systemName, isLive) => dispatcher.BeginInvoke(() => _routeEventTrigger.Fire(kind, systemName, isLive)),
                () => dispatcher.BeginInvoke(() =>
                {
                    // Same guarded-refresh pattern ToggleCaptain's own assignment-triggered
                    // refresh already uses - a no-op if a scan happens to already be running.
                    if (RefreshCommand.CanExecute(null))
                    {
                        RefreshCommand.Execute(null);
                    }
                }));

            _ = _captainWatcher.StartAsync();
        }

        private void StopCaptainWatch()
        {
            _captainWatcher?.Dispose();
            _captainWatcher = null;
        }
    }
}
