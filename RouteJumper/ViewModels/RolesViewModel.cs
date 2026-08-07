using System.Collections.ObjectModel;
using System.Windows;
using RouteJumper.Common;
using RouteJumper.Sequencing;
using RouteJumper.Services;

namespace RouteJumper.ViewModels
{
    /// <summary>
    /// ViewModel for the "Roles" tab: lists running EliteDangerous64.exe instances, lets the
    /// user re-scan for them, and lets the user assign the Captain/Engineer roles (§11.5).
    /// </summary>
    public class RolesViewModel : ObservableObject
    {
        private const string CaptainFidSettingKey = "CaptainFid";
        private const string EngineerFidSettingKey = "EngineerFid";

        private readonly EliteInstanceScanner _scanner = new();
        private readonly ManualRowEventTrigger _routeEventTrigger;
        private readonly AppSettingsStore _settings;

        private bool _isRefreshing;
        private string _statusText = string.Empty;
        private int? _captainProcessId;
        private int? _engineerProcessId;
        private CarrierRouteJournalWatcher? _captainWatcher;

        public RolesViewModel(ManualRowEventTrigger routeEventTrigger, AppSettingsStore settings)
        {
            _routeEventTrigger = routeEventTrigger;
            _settings = settings;

            Instances = new ObservableCollection<EliteInstanceViewModel>();
            RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsRefreshing);
            ToggleCaptainCommand = new RelayCommand<EliteInstanceViewModel>(ToggleCaptain);
            ToggleEngineerCommand = new RelayCommand<EliteInstanceViewModel>(ToggleEngineer, CanToggleEngineer);

            _ = RefreshAsync();
        }

        public ObservableCollection<EliteInstanceViewModel> Instances { get; }

        public AsyncRelayCommand RefreshCommand { get; }

        /// <summary>Assigns/unassigns the Captain role to a card's instance.</summary>
        public RelayCommand<EliteInstanceViewModel> ToggleCaptainCommand { get; }

        /// <summary>Assigns/unassigns the Engineer role to a card's instance.</summary>
        public RelayCommand<EliteInstanceViewModel> ToggleEngineerCommand { get; }

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
        /// (re)built by Save, first time or after Edit. If a Captain is currently assigned,
        /// restarts that instance's journal watch so the freshly-(re)built route is re-derived
        /// from their real progress from scratch, exactly as if Captain had just been assigned -
        /// RouteViewModel.Save already gives every row a clean, freshly-constructed starting
        /// state, so there's no separate Reset to fire here the way ToggleCaptain needs one.
        /// If no Captain is assigned, this is a no-op - Save's own default (row 1 marked next)
        /// is left standing, per SPEC §4.5's Update.
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

            // Per SPEC §11.5: assigning Captain to an instance starts the route from a clean
            // slate before replaying that instance's journal - a previous Captain's leftover
            // progress (or a manual demo run) must not linger and interfere with matching.
            // Fired synchronously (we're already on the UI thread here), so it's guaranteed to
            // apply before any of the new watcher's replayed events, which are always queued
            // via the dispatcher (see StartCaptainWatch) and so can never run ahead of this.
            _routeEventTrigger.Fire(RowEventKind.Reset, string.Empty);

            StartCaptainWatch(instance);

            // Per SPEC §11.5: assigning Captain also triggers a reread to refresh this tab -
            // that same reread is what replays the carrier's journal history into the route.
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
                (kind, systemName) => dispatcher.BeginInvoke(() => _routeEventTrigger.Fire(kind, systemName)));

            _ = _captainWatcher.StartAsync();
        }

        private void StopCaptainWatch()
        {
            _captainWatcher?.Dispose();
            _captainWatcher = null;
        }
    }
}
