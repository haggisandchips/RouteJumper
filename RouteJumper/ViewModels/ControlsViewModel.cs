using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using RouteJumper.Common;
using RouteJumper.Models;
using RouteJumper.Services;

namespace RouteJumper.ViewModels
{
    /// <summary>
    /// ViewModel for the "Controls" tab (SPEC §6): a configurable key-binding table, a
    /// lightweight running-instances list independent of the Roles tab's own scan, and a
    /// recorder/player for named macros built from that key-binding vocabulary.
    /// </summary>
    public class ControlsViewModel : ObservableObject
    {
        private const string KeyBindingSettingPrefix = "ControlAction.";
        private const string RecordedMacrosSettingKey = "RecordedMacros";
        private const string CooldownDelayMsSettingKey = "CooldownDelayMs";
        private const int DefaultCooldownDelayMs = 5000;

        private static readonly IReadOnlyDictionary<ControlAction, (Key Key, ModifierKeys Modifiers)> DefaultBindings =
            new Dictionary<ControlAction, (Key, ModifierKeys)>
            {
                [ControlAction.Up] = (Key.Up, ModifierKeys.None),
                [ControlAction.Down] = (Key.Down, ModifierKeys.None),
                [ControlAction.Left] = (Key.Left, ModifierKeys.None),
                [ControlAction.Right] = (Key.Right, ModifierKeys.None),
                [ControlAction.Select] = (Key.Space, ModifierKeys.None),
                [ControlAction.PrevPanel] = (Key.Delete, ModifierKeys.None),
                [ControlAction.NextPanel] = (Key.End, ModifierKeys.None),
                [ControlAction.Exit] = (Key.Back, ModifierKeys.None),
                [ControlAction.RightPanel] = (Key.D4, ModifierKeys.None)
            };

        private readonly AppSettingsStore _settings;
        private readonly EliteInstanceScanner _scanner;
        private readonly Func<string?> _getNextSystemName;

        private bool _isRefreshing;
        private string _statusText = string.Empty;
        private EliteInstanceViewModel? _selectedInstance;
        private bool _isRecording;
        private InputRecorder? _activeRecorder;
        private IntPtr _recordingTargetWindow;
        private int _recordingTargetProcessId;
        private string _recordingTargetCommanderName = string.Empty;
        private CancellationTokenSource? _playbackCts;
        private RecordedMacroViewModel? _selectedMacro;
        private RecordedMacroViewModel? _editingMacro;
        private bool _isPlaying;
        private string? _playbackErrorMessage;
        private int _cooldownDelayMs = DefaultCooldownDelayMs;

        /// <summary>
        /// <paramref name="getNextSystemName"/> resolves the Route tab's current "next system"
        /// (the in-progress row's System text, if any) - MainViewModel supplies this as a
        /// closure over RouteViewModel.Rows, the same one-way, event-free bridging pattern
        /// RouteViewModel.RouteSaved already uses to reach RolesViewModel, since this
        /// ViewModel has no reference to RouteViewModel itself. Used to resolve a macro's
        /// "{NEXT_SYSTEM}" paste placeholder - see MacroPlayer.
        /// </summary>
        public ControlsViewModel(AppSettingsStore settings, EliteInstanceScanner scanner, Func<string?> getNextSystemName)
        {
            _settings = settings;
            _scanner = scanner;
            _getNextSystemName = getNextSystemName;
            _cooldownDelayMs = _settings.GetDouble(CooldownDelayMsSettingKey) is { } storedDelay
                ? Math.Max(0, (int)storedDelay)
                : DefaultCooldownDelayMs;

            KeyBindings = new ObservableCollection<KeyBindingViewModel>(LoadKeyBindings());
            Instances = new ObservableCollection<EliteInstanceViewModel>();
            Macros = new ObservableCollection<RecordedMacroViewModel>(LoadMacros());
            foreach (var macro in Macros)
            {
                WatchForMacroEdits(macro);
            }

            RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsRefreshing);
            StartCaptureCommand = new RelayCommand<KeyBindingViewModel>(StartCapture);
            SelectInstanceCommand = new RelayCommand<EliteInstanceViewModel>(instance => SelectedInstance = instance);
            RecordCommand = new RelayCommand(StartRecording, CanStartRecording);
            StopCommand = new RelayCommand(StopActive, () => IsRecording || IsPlaying);
            SelectMacroCommand = new RelayCommand<RecordedMacroViewModel>(macro => SelectedMacro = macro);
            PlayCommand = new RelayCommand(Play, CanPlay);
            DismissPlaybackErrorCommand = new RelayCommand(() => PlaybackErrorMessage = null);
            EditMacroCommand = new RelayCommand<RecordedMacroViewModel>(OpenEditor);
            CloseMacroEditorCommand = new RelayCommand(() => EditingMacro = null);
            DeleteMacroCommand = new RelayCommand<RecordedMacroViewModel>(DeleteMacro);

            _ = RefreshAsync();
        }

        /// <summary>
        /// Raised when a macro is deleted - lets MainViewModel tell the Roles tab to clear that
        /// macro out of a Captain/Engineer selection that referenced it (see
        /// RolesViewModel.OnMacroDeleted), rather than leaving a dangling selection behind.
        /// </summary>
        public event EventHandler<RecordedMacroViewModel>? MacroDeleted;

        public ObservableCollection<KeyBindingViewModel> KeyBindings { get; }

        public ObservableCollection<EliteInstanceViewModel> Instances { get; }

        public ObservableCollection<RecordedMacroViewModel> Macros { get; }

        public AsyncRelayCommand RefreshCommand { get; }

        /// <summary>Puts a key-binding row into "waiting for a keypress" mode - see KeyBindingViewModel.IsCapturing.</summary>
        public RelayCommand<KeyBindingViewModel> StartCaptureCommand { get; }

        public RelayCommand<EliteInstanceViewModel> SelectInstanceCommand { get; }

        public RelayCommand RecordCommand { get; }

        /// <summary>Stops whichever of recording/playback is currently active - a single button covers both, since only one can ever be happening at a time.</summary>
        public RelayCommand StopCommand { get; }

        /// <summary>Selects a macro as the Play target - see SelectedMacro.</summary>
        public RelayCommand<RecordedMacroViewModel> SelectMacroCommand { get; }

        /// <summary>Plays SelectedMacro against SelectedInstance - any running instance can play any macro, not just the one it was originally recorded against.</summary>
        public RelayCommand PlayCommand { get; }

        /// <summary>Clears PlaybackErrorMessage.</summary>
        public RelayCommand DismissPlaybackErrorCommand { get; }

        public RelayCommand<RecordedMacroViewModel> EditMacroCommand { get; }

        public RelayCommand CloseMacroEditorCommand { get; }

        public RelayCommand<RecordedMacroViewModel> DeleteMacroCommand { get; }

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

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        public EliteInstanceViewModel? SelectedInstance
        {
            get => _selectedInstance;
            set
            {
                if (SetProperty(ref _selectedInstance, value))
                {
                    RecordCommand.RaiseCanExecuteChanged();
                    PlayCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>The macro PlayCommand will run - selected by clicking its name in the Recorded Macros list, independent of which instance (if any) it was originally recorded against.</summary>
        public RecordedMacroViewModel? SelectedMacro
        {
            get => _selectedMacro;
            set
            {
                if (SetProperty(ref _selectedMacro, value))
                {
                    PlayCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>Non-null while the Recorded Macros section shows the full-size editor (Name + script text + syntax reference) for this macro, in place of the compact list.</summary>
        public RecordedMacroViewModel? EditingMacro
        {
            get => _editingMacro;
            set
            {
                if (SetProperty(ref _editingMacro, value))
                {
                    OnPropertyChanged(nameof(IsEditingMacro));
                    RecordCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsEditingMacro => EditingMacro != null;

        public bool IsRecording
        {
            get => _isRecording;
            private set
            {
                if (SetProperty(ref _isRecording, value))
                {
                    RecordCommand.RaiseCanExecuteChanged();
                    PlayCommand.RaiseCanExecuteChanged();
                    StopCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// True while a macro is actively playing. Play itself stays enabled while a *different*
        /// playback is already running (starting a new one cancels the old one first), but not
        /// while recording is active - recording and playback would otherwise both be injecting/
        /// capturing input at once. Only StopCommand and RecordCommand's CanExecute depend on
        /// this beyond Play's own.
        /// </summary>
        public bool IsPlaying
        {
            get => _isPlaying;
            private set
            {
                if (SetProperty(ref _isPlaying, value))
                {
                    RecordCommand.RaiseCanExecuteChanged();
                    StopCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// Non-null while a closeable banner should be shown explaining why playback stopped
        /// unexpectedly (currently: the target window lost focus mid-script - see MacroPlayer's
        /// PlaybackAbortedException). Never set for an ordinary user-initiated Stop, which isn't
        /// an error.
        /// </summary>
        public string? PlaybackErrorMessage
        {
            get => _playbackErrorMessage;
            private set => SetProperty(ref _playbackErrorMessage, value);
        }

        /// <summary>
        /// Options (§6.1): how long Auto Pilot (Route tab, §4.2) waits after a row's Cooldown
        /// status clears before plotting the next jump, on top of however long the cooldown
        /// itself already took - a real jump needs a moment after cooldown for the UI to settle
        /// before the Captain's macro starts clicking through panels. Not used for anything
        /// besides that - has no effect on the Cooldown status/timing itself (§5.7), and no
        /// effect on a manually-triggered Play. Clamped to non-negative.
        /// </summary>
        public int CooldownDelayMs
        {
            get => _cooldownDelayMs;
            set
            {
                var clamped = Math.Max(0, value);
                if (SetProperty(ref _cooldownDelayMs, clamped))
                {
                    _settings.SetDouble(CooldownDelayMsSettingKey, clamped);
                }
            }
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

                if (SelectedInstance != null && Instances.All(i => i.ProcessId != SelectedInstance.ProcessId))
                {
                    SelectedInstance = null;
                }

                // A lone running instance is the obvious recording/playback target - select it
                // automatically. With more than one, the user has to click one (selection would
                // otherwise be an arbitrary, silent guess).
                if (SelectedInstance is null && Instances.Count == 1)
                {
                    SelectedInstance = Instances[0];
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

        private void StartCapture(KeyBindingViewModel? binding)
        {
            if (binding is null)
            {
                return;
            }

            foreach (var each in KeyBindings)
            {
                each.IsCapturing = false;
            }

            binding.IsCapturing = true;
        }

        /// <summary>
        /// Called by ControlsView's code-behind PreviewKeyDown handler (view-layer input glue,
        /// same carve-out RouteView.xaml.cs's focus handling uses) once a real, non-modifier key
        /// is pressed while a row is capturing. A bare modifier key alone leaves capture mode
        /// active (nothing to bind yet); Escape cancels capture without changing the binding.
        /// </summary>
        public void CompleteCapture(KeyBindingViewModel binding, Key key, ModifierKeys modifiers)
        {
            if (IsBareModifierKey(key))
            {
                return;
            }

            binding.IsCapturing = false;

            if (key == Key.Escape)
            {
                return;
            }

            var storage = KeyBindingFormatter.ToStorageString(key, modifiers);
            binding.StorageString = storage;
            _settings.SetString(KeyBindingSettingPrefix + binding.Action, storage);
        }

        private static bool IsBareModifierKey(Key key) =>
            key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
                or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;

        private List<KeyBindingViewModel> LoadKeyBindings()
        {
            var result = new List<KeyBindingViewModel>();
            foreach (var action in Enum.GetValues<ControlAction>())
            {
                var (defaultKey, defaultModifiers) = DefaultBindings[action];
                var defaultStorage = KeyBindingFormatter.ToStorageString(defaultKey, defaultModifiers);
                var stored = _settings.GetString(KeyBindingSettingPrefix + action);
                result.Add(new KeyBindingViewModel(action, string.IsNullOrEmpty(stored) ? defaultStorage : stored));
            }

            return result;
        }

        private bool CanStartRecording() =>
            !IsRecording && !IsPlaying && !IsEditingMacro && SelectedInstance is { WindowHandle: not 0 };

        private void StartRecording()
        {
            if (SelectedInstance is not { WindowHandle: not 0 } instance)
            {
                return;
            }

            _recordingTargetWindow = instance.WindowHandle;
            _recordingTargetProcessId = instance.ProcessId;
            _recordingTargetCommanderName = instance.CommanderName;

            _activeRecorder = new InputRecorder(_recordingTargetWindow, ResolveRecordToken);
            _activeRecorder.Start();
            IsRecording = true;
        }

        /// <summary>Translates a captured key to a script token - the bound action's name if it matches one of the current key bindings, otherwise a literal "KEY &lt;storage&gt;".</summary>
        private string ResolveRecordToken(Key key, ModifierKeys modifiers)
        {
            var storage = KeyBindingFormatter.ToStorageString(key, modifiers);
            var match = KeyBindings.FirstOrDefault(b => b.StorageString == storage);
            return match != null ? match.ActionName : $"KEY {storage}";
        }

        private void StopRecording()
        {
            if (_activeRecorder is null)
            {
                return;
            }

            var steps = _activeRecorder.Stop();
            _activeRecorder.Dispose();
            _activeRecorder = null;
            IsRecording = false;

            // Always a new entry - SPEC §6.3: "If a recording already exists and the user
            // clicks the record button again, the application should start a new recording and
            // keep the existing one."
            var macro = new RecordedMacroViewModel(new RecordedMacro
            {
                Id = Guid.NewGuid(),
                Name = $"Recording {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                ScriptText = MacroScriptSerializer.ToScriptText(steps),
                SourceProcessId = _recordingTargetProcessId,
                SourceCommanderName = _recordingTargetCommanderName,
                RecordedAtUtc = DateTime.UtcNow
            });

            WatchForMacroEdits(macro);
            Macros.Add(macro);
            SaveMacros();
        }

        private void WatchForMacroEdits(RecordedMacroViewModel macro) =>
            macro.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(RecordedMacroViewModel.Name) or nameof(RecordedMacroViewModel.ScriptText))
                {
                    SaveMacros();
                }
            };

        /// <summary>
        /// Opening the editor also selects the macro as the Play target - without this, a user
        /// who opens a script straight from its pencil icon (never having clicked its name in
        /// the list first) would find Play silently disabled with no macro selected, which looks
        /// exactly like "Play does nothing" from the outside. Play stays usable the whole time
        /// the editor is open (CanPlay doesn't check IsEditingMacro), so a script can be tweaked
        /// and re-run without leaving the editor.
        /// </summary>
        private void OpenEditor(RecordedMacroViewModel? macro)
        {
            if (macro is null)
            {
                return;
            }

            SelectedMacro = macro;
            EditingMacro = macro;
        }

        private bool CanPlay() => !IsRecording && SelectedInstance is { WindowHandle: not 0 } && SelectedMacro != null;

        /// <summary>
        /// Plays SelectedMacro against SelectedInstance - deliberately not restricted to the
        /// instance a macro was originally recorded against (SourceProcessId is display-only,
        /// see RecordedMacroViewModel), since a game process's PID never survives a restart
        /// anyway, and the recorded keystrokes/positions are just as valid against any other
        /// running instance the user picks. Stays enabled while a playback is already running -
        /// starting a new one cancels the old one first, same as StopCommand does explicitly.
        /// </summary>
        private void Play()
        {
            if (SelectedInstance is not { WindowHandle: not 0 } instance || SelectedMacro is not { } macro)
            {
                return;
            }

            StartPlayback(macro, instance);
        }

        /// <summary>
        /// Plays a specific macro against a specific instance, bypassing SelectedMacro/
        /// SelectedInstance entirely - used by AutoPilotController (Route tab's Auto Pilot,
        /// §4.2) to plot the Captain's macro, so an autopilot-triggered play is exactly as
        /// visible (IsPlaying), stoppable (StopCommand), and error-reported
        /// (PlaybackErrorMessage) as a manual one, rather than a separate, invisible channel.
        /// </summary>
        public void PlayMacro(RecordedMacroViewModel macro, EliteInstanceViewModel instance) => StartPlayback(macro, instance);

        private void StartPlayback(RecordedMacroViewModel macro, EliteInstanceViewModel instance)
        {
            _playbackCts?.Cancel();
            var cts = new CancellationTokenSource();
            _playbackCts = cts;
            IsPlaying = true;
            PlaybackErrorMessage = null;

            var player = new MacroPlayer(instance.WindowHandle, ResolveActionBinding, _getNextSystemName);
            _ = RunPlaybackAsync(player, macro.ScriptText, cts);
        }

        /// <summary>
        /// Runs playback to completion (or cancellation) and clears IsPlaying afterward - but
        /// only if this is still the current playback (a second Play click replaces _playbackCts
        /// before this one's finally block runs, so the stale first call must not clear the flag
        /// out from under the second one). A PlaybackAbortedException - the target window losing
        /// focus mid-script, see MacroPlayer - surfaces a closeable error banner; an ordinary
        /// OperationCanceledException (the user pressed Stop, or started a different playback)
        /// does not, since that's an intentional action, not a failure.
        /// </summary>
        private async Task RunPlaybackAsync(MacroPlayer player, string scriptText, CancellationTokenSource cts)
        {
            try
            {
                await player.PlayAsync(scriptText, cts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (PlaybackAbortedException ex)
            {
                PlaybackErrorMessage = ex.Message;
            }
            finally
            {
                if (ReferenceEquals(_playbackCts, cts))
                {
                    IsPlaying = false;
                }
            }
        }

        /// <summary>Dispatches the single Stop button to whichever of recording/playback is actually active - only one of the two is ever possible at once (Record is disabled while a macro editor is open, and there's no path to start a recording while a playback is running or vice versa).</summary>
        private void StopActive()
        {
            if (IsRecording)
            {
                StopRecording();
            }
            else if (IsPlaying)
            {
                StopPlayback();
            }
        }

        private void StopPlayback() => _playbackCts?.Cancel();

        private string? ResolveActionBinding(ControlAction action) =>
            KeyBindings.FirstOrDefault(b => b.Action == action)?.StorageString;

        private void DeleteMacro(RecordedMacroViewModel? macro)
        {
            if (macro is null)
            {
                return;
            }

            if (ReferenceEquals(SelectedMacro, macro))
            {
                SelectedMacro = null;
            }

            if (ReferenceEquals(EditingMacro, macro))
            {
                EditingMacro = null;
            }

            Macros.Remove(macro);
            SaveMacros();
            MacroDeleted?.Invoke(this, macro);
        }

        private void SaveMacros()
        {
            var models = Macros.Select(m => m.ToModel()).ToList();
            _settings.SetString(RecordedMacrosSettingKey, JsonSerializer.Serialize(models));
        }

        private List<RecordedMacroViewModel> LoadMacros()
        {
            var json = _settings.GetString(RecordedMacrosSettingKey);
            if (string.IsNullOrEmpty(json))
            {
                return new List<RecordedMacroViewModel>();
            }

            try
            {
                var models = JsonSerializer.Deserialize<List<RecordedMacro>>(json) ?? new List<RecordedMacro>();

                // Pre-migration macros (recorded before RecordedMacro.Id existed) deserialize
                // with Guid.Empty - heal them here, once, so a Roles-tab role's macro selection
                // (which references a macro by Id, not by its freely-editable Name) has
                // something stable to point at from the very first load onward.
                var healedAny = false;
                foreach (var model in models)
                {
                    if (model.Id == Guid.Empty)
                    {
                        model.Id = Guid.NewGuid();
                        healedAny = true;
                    }
                }

                if (healedAny)
                {
                    _settings.SetString(RecordedMacrosSettingKey, JsonSerializer.Serialize(models));
                }

                return models.Select(m => new RecordedMacroViewModel(m)).ToList();
            }
            catch (JsonException)
            {
                return new List<RecordedMacroViewModel>();
            }
        }
    }
}
