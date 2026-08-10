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

        private const string AutoPilotDelayMsSettingKey = "AutoPilotDelayMs";
        private const int DefaultAutoPilotDelayMs = 5000;
        private const string AutoWaitMsSettingKey = "AutoWaitMs";
        private const int DefaultAutoWaitMs = 300;

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

        private const int TritiumDepotCapacity = 1000;
        private const string TritiumLoopsPlaceholder = "{TRITIUM_LOOPS}";
        private const string DefaultNextSystemTestOverride = "Sol";
        private const string DefaultTritiumLoopsTestOverride = "1";

        private readonly AppSettingsStore _settings;
        private readonly EliteInstanceScanner _scanner;
        private readonly Func<string?> _getNextSystemName;
        private readonly Func<EliteInstanceViewModel?> _getEngineerInstance;
        private readonly Func<Task> _refreshRolesAsync;

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
        private int _autoPilotDelayMs = DefaultAutoPilotDelayMs;
        private int _autoWaitMs = DefaultAutoWaitMs;
        private bool _isStepping;
        private CancellationTokenSource? _stepCts;
        private string? _stepScriptSnapshot;
        private IReadOnlyList<MacroInstruction> _stepInstructions = Array.Empty<MacroInstruction>();
        private int _stepIndex;
        private int _lastTritiumLoops;
        private string _nextSystemTestOverride = DefaultNextSystemTestOverride;
        private string _tritiumLoopsTestOverride = DefaultTritiumLoopsTestOverride;

        /// <summary>
        /// <paramref name="getNextSystemName"/> resolves the Route tab's current "next system"
        /// (the in-progress row's System text, if any) - MainViewModel supplies this as a
        /// closure over RouteViewModel.Rows, the same one-way, event-free bridging pattern
        /// RouteViewModel.RouteSaved already uses to reach RolesViewModel, since this
        /// ViewModel has no reference to RouteViewModel itself. Used to resolve a macro's
        /// "{NEXT_SYSTEM}" paste placeholder - see MacroPlayer.
        ///
        /// <paramref name="getEngineerInstance"/> and <paramref name="refreshRolesAsync"/> are
        /// the same kind of closure, over RolesViewModel.EngineerInstance/RefreshAsync - used to
        /// resolve a macro's "{TRITIUM_LOOPS}" placeholder against the Engineer's current
        /// cargo/carrier-fuel data during an Auto Pilot-triggered run only (see
        /// ResolveTritiumLoopsAsync; a manual Play/Step in this tab uses the test-value fields
        /// instead, never this closure).
        /// </summary>
        public ControlsViewModel(
            AppSettingsStore settings,
            EliteInstanceScanner scanner,
            Func<string?> getNextSystemName,
            Func<EliteInstanceViewModel?> getEngineerInstance,
            Func<Task> refreshRolesAsync)
        {
            _settings = settings;
            _scanner = scanner;
            _getNextSystemName = getNextSystemName;
            _getEngineerInstance = getEngineerInstance;
            _refreshRolesAsync = refreshRolesAsync;
            _autoPilotDelayMs = _settings.GetDouble(AutoPilotDelayMsSettingKey) is { } storedDelay
                ? Math.Max(0, (int)storedDelay)
                : DefaultAutoPilotDelayMs;
            _autoWaitMs = _settings.GetDouble(AutoWaitMsSettingKey) is { } storedAutoWait
                ? Math.Max(0, (int)storedAutoWait)
                : DefaultAutoWaitMs;

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
            StopCommand = new RelayCommand(StopActive, () => IsRecording || IsPlaying || IsStepping);
            SelectMacroCommand = new RelayCommand<RecordedMacroViewModel>(macro => SelectedMacro = macro);
            PlayCommand = new RelayCommand(Play, CanPlay);
            DismissPlaybackErrorCommand = new RelayCommand(() => PlaybackErrorMessage = null);
            EditMacroCommand = new RelayCommand<RecordedMacroViewModel>(OpenEditor);
            CloseMacroEditorCommand = new RelayCommand(() => EditingMacro = null);
            DeleteMacroCommand = new RelayCommand<RecordedMacroViewModel>(DeleteMacro);
            StepCommand = new RelayCommand(Step, CanStep);

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

        /// <summary>Runs just the next leaf instruction of EditingMacro's script against SelectedInstance, refocusing it first - SPEC §6.5's editor "Step" facility.</summary>
        public RelayCommand StepCommand { get; }

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
                    StepCommand.RaiseCanExecuteChanged();
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
                    EnsureStepState();
                    StepCommand.RaiseCanExecuteChanged();
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
                    StepCommand.RaiseCanExecuteChanged();
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
                    PlayCommand.RaiseCanExecuteChanged();
                    StepCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// True while a single stepped instruction (StepCommand) is executing - mutually
        /// exclusive with IsRecording/IsPlaying (Play, Record, and Step all inject input against
        /// the same target and must never overlap).
        /// </summary>
        public bool IsStepping
        {
            get => _isStepping;
            private set
            {
                if (SetProperty(ref _isStepping, value))
                {
                    RecordCommand.RaiseCanExecuteChanged();
                    PlayCommand.RaiseCanExecuteChanged();
                    StopCommand.RaiseCanExecuteChanged();
                    StepCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>"Next: &lt;instruction&gt;" (e.g. "Next: RIGHT") naming whatever the editor's Step button is about to run, or blank once there's nothing left to step through (an empty/all-comment script, or no macro being edited).</summary>
        public string StepStatusText =>
            _stepInstructions.Count == 0 ? string.Empty : $"Next: {DescribeInstruction(_stepInstructions[_stepIndex])}";

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
        /// Options (§6.1): the delay AutoPilotController applies around a row's Cooldown, on top
        /// of however long Cooldown itself already took - a real jump needs a moment for the
        /// game's own UI to settle before a macro starts clicking through panels. Used twice per
        /// row: once after Cooldown *clears*, before the Captain's macro plots the next jump, and
        /// once after Cooldown *starts*, before the Engineer's macro (if Engineer is currently
        /// assigned) deposits fuel - see AutoPilotController. Has no effect on the Cooldown
        /// status/timing itself (§5.7), and no effect on a manually-triggered Play. Clamped to
        /// non-negative.
        /// </summary>
        public int AutoPilotDelayMs
        {
            get => _autoPilotDelayMs;
            set
            {
                var clamped = Math.Max(0, value);
                if (SetProperty(ref _autoPilotDelayMs, clamped))
                {
                    _settings.SetDouble(AutoPilotDelayMsSettingKey, clamped);
                }
            }
        }

        /// <summary>
        /// Options (§6.1): how long MacroPlayer automatically pauses after each leaf instruction
        /// it executes - lets a script rely on consistent built-in pacing between actions instead
        /// of needing an explicit WAIT after every single one. Applied uniformly regardless of how
        /// the script was started (manual Play, Auto Pilot, or the macro editor's Step button,
        /// §6.5) - skipped only when the very next instruction is itself a WAIT (that one's own
        /// duration already provides the pause; stacking ours in front of it would just be a
        /// redundant extra delay) or, for Step specifically, when there's no next instruction to
        /// pace against (the script is about to wrap back to the start). Clamped to non-negative.
        /// </summary>
        public int AutoWaitMs
        {
            get => _autoWaitMs;
            set
            {
                var clamped = Math.Max(0, value);
                if (SetProperty(ref _autoWaitMs, clamped))
                {
                    _settings.SetDouble(AutoWaitMsSettingKey, clamped);
                }
            }
        }

        /// <summary>
        /// Testing aid: the {NEXT_SYSTEM} value a manual Play or Step (this tab only - never an
        /// Auto Pilot-triggered run, which always resolves it live against the Route tab) resolves
        /// a script's PASTE {NEXT_SYSTEM} against, so a script can be tried out here without a live
        /// route. Defaults to "Sol"; session-only, never persisted. Play/Step are disabled while
        /// this is blank (see CanPlay/CanStep) rather than silently pasting nothing.
        /// </summary>
        public string NextSystemTestOverride
        {
            get => _nextSystemTestOverride;
            set
            {
                if (SetProperty(ref _nextSystemTestOverride, value))
                {
                    PlayCommand.RaiseCanExecuteChanged();
                    StepCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// Testing aid: the {TRITIUM_LOOPS} value a manual Play or Step (this tab only - never an
        /// Auto Pilot-triggered run, which always rescans CMDR info and computes it live) resolves
        /// a script's REPEAT {TRITIUM_LOOPS} against, so a script can be tried out here without a
        /// running instance in the right cargo/fuel state. Defaults to "1"; session-only, never
        /// persisted. Play/Step are disabled while this is blank (see CanPlay/CanStep).
        /// </summary>
        public string TritiumLoopsTestOverride
        {
            get => _tritiumLoopsTestOverride;
            set
            {
                if (SetProperty(ref _tritiumLoopsTestOverride, value))
                {
                    PlayCommand.RaiseCanExecuteChanged();
                    StepCommand.RaiseCanExecuteChanged();
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

                // Re-point at the freshly-scanned object for the same instance (cargo/carrier-fuel
                // data - used by TRITIUM_LOOPS below - would otherwise stay pinned to whatever it
                // was at the moment of selection); null if that instance is no longer running.
                if (SelectedInstance != null)
                {
                    SelectedInstance = Instances.FirstOrDefault(i => i.ProcessId == SelectedInstance.ProcessId);
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
            !IsRecording && !IsPlaying && !IsStepping && !IsEditingMacro && SelectedInstance is { WindowHandle: not 0 };

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

                if (e.PropertyName == nameof(RecordedMacroViewModel.ScriptText) && ReferenceEquals(macro, EditingMacro))
                {
                    EnsureStepState();
                    StepCommand.RaiseCanExecuteChanged();
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

        /// <summary>
        /// Play is also gated on both test-value fields being non-blank (SelectedInstance can't
        /// tell us whether the selected macro actually needs them, and defaulting to "run it and
        /// see" risks pasting/repeating against a blank or unparsable value) - see
        /// NextSystemTestOverride/TritiumLoopsTestOverride.
        /// </summary>
        private bool CanPlay() =>
            !IsRecording && !IsStepping && SelectedInstance is { WindowHandle: not 0 } && SelectedMacro != null &&
            !string.IsNullOrWhiteSpace(NextSystemTestOverride) && !string.IsNullOrWhiteSpace(TritiumLoopsTestOverride);

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

            StartPlayback(macro, instance, useTestValues: true);
        }

        /// <summary>
        /// Plays a specific macro against a specific instance, bypassing SelectedMacro/
        /// SelectedInstance entirely - used by AutoPilotController (Route tab's Auto Pilot,
        /// §4.2) to plot the Captain's macro, so an autopilot-triggered play is exactly as
        /// visible (IsPlaying), stoppable (StopCommand), and error-reported
        /// (PlaybackErrorMessage) as a manual one, rather than a separate, invisible channel.
        /// Always resolves {NEXT_SYSTEM}/{TRITIUM_LOOPS} live (never the test-value fields below) -
        /// those exist purely so this tab's own Play/Step can be tried out without a live route or
        /// a running instance in the right cargo/fuel state; a real Auto Pilot run always needs the
        /// real values.
        /// </summary>
        public void PlayMacro(RecordedMacroViewModel macro, EliteInstanceViewModel instance) => StartPlayback(macro, instance, useTestValues: false);

        private void StartPlayback(RecordedMacroViewModel macro, EliteInstanceViewModel instance, bool useTestValues)
        {
            _playbackCts?.Cancel();
            var cts = new CancellationTokenSource();
            _playbackCts = cts;
            IsPlaying = true;
            PlaybackErrorMessage = null;

            Func<string?> getNextSystemName = useTestValues ? () => NextSystemTestOverride : _getNextSystemName;
            var player = new MacroPlayer(instance.WindowHandle, ResolveActionBinding, getNextSystemName, AutoWaitMs);
            _ = RunPlaybackAsync(player, macro.ScriptText, cts, useTestValues);
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
        private async Task RunPlaybackAsync(MacroPlayer player, string scriptText, CancellationTokenSource cts, bool useTestValues)
        {
            try
            {
                var scriptToPlay = scriptText;

                // Skip the CMDR rescan entirely for the (vast majority of) scripts that don't
                // even reference {TRITIUM_LOOPS} - it's a real, sometimes multi-second delay
                // (scans every running instance's process/journal), and inserting it
                // unconditionally ahead of every single Auto Pilot trigger risked the game
                // window sitting unfocused (or the user's own attention drifting) long enough for
                // its UI state to no longer match what the macro assumes by the time it actually
                // starts sending input. A manual, test-value-driven run (useTestValues) never
                // needs this rescan at all - see ResolveTritiumLoopsAsync.
                if (ReferencesTritiumLoops(scriptText))
                {
                    var loops = await ResolveTritiumLoopsAsync(useTestValues);
                    cts.Token.ThrowIfCancellationRequested();
                    scriptToPlay = SubstituteTritiumLoops(scriptText, loops);
                }

                await player.PlayAsync(scriptToPlay, cts.Token);
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

        /// <summary>Dispatches the single Stop button to whichever of recording/playback/stepping is actually active - only one of the three is ever possible at once (Record is disabled while a macro editor is open, and there's no path to start a recording while a playback or step is running or vice versa).</summary>
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
            else if (IsStepping)
            {
                _stepCts?.Cancel();
            }
        }

        private void StopPlayback() => _playbackCts?.Cancel();

        /// <summary>
        /// (Re)parses and flattens EditingMacro's current script text into leaf instructions for
        /// StepCommand - only actually redone when the script text has changed since the last
        /// call, so repeatedly polling this from CanStep's CommandManager.RequerySuggested churn
        /// doesn't reparse the whole script on every keystroke or focus change elsewhere in the
        /// app. Editing the script while stepping is treated as starting over: the index resets
        /// to 0 rather than trying to preserve a position against instructions that may no longer
        /// correspond to the same lines.
        ///
        /// Flattens against the last resolved TRITIUM_LOOPS value (0 until it's ever been
        /// resolved) purely as a preview - a REPEAT built from it only expands to its real size
        /// once RunStepAsync has actually resolved it fresh from TritiumLoopsTestOverride, at
        /// which point it resets _stepScriptSnapshot to force this to redo the flatten.
        /// </summary>
        private void EnsureStepState()
        {
            var scriptText = EditingMacro?.ScriptText;
            if (scriptText == _stepScriptSnapshot)
            {
                return;
            }

            _stepScriptSnapshot = scriptText;
            _stepInstructions = scriptText is null
                ? Array.Empty<MacroInstruction>()
                : MacroPlayer.Flatten(SubstituteTritiumLoops(scriptText, _lastTritiumLoops));
            _stepIndex = 0;
            OnPropertyChanged(nameof(StepStatusText));
        }

        /// <summary>
        /// Step is also gated on both test-value fields being non-blank - Step only ever plays
        /// against them (there's no Auto Pilot path into Step), so a blank field would otherwise
        /// silently paste nothing or run 0 REPEAT iterations rather than actually being disabled.
        /// </summary>
        private bool CanStep()
        {
            EnsureStepState();
            return !IsRecording && !IsPlaying && !IsStepping &&
                   EditingMacro != null && SelectedInstance is { WindowHandle: not 0 } &&
                   _stepInstructions.Count > 0 &&
                   !string.IsNullOrWhiteSpace(NextSystemTestOverride) && !string.IsNullOrWhiteSpace(TritiumLoopsTestOverride);
        }

        /// <summary>
        /// Runs just the next leaf instruction in EditingMacro's script, wrapping back to the
        /// start once the end is reached - a debugging aid for trying a script one command at a
        /// time (SPEC §6.5), so it naturally supports stepping through the same short script
        /// repeatedly rather than requiring an explicit "reset" action. Always resolves
        /// {NEXT_SYSTEM}/{TRITIUM_LOOPS} from the test-value fields (there's no Auto Pilot path
        /// into Step, so there's nothing else for it to resolve against).
        /// </summary>
        private void Step()
        {
            EnsureStepState();
            if (EditingMacro is null || SelectedInstance is not { WindowHandle: not 0 } instance ||
                _stepInstructions.Count == 0)
            {
                return;
            }

            var cts = new CancellationTokenSource();
            _stepCts = cts;
            IsStepping = true;

            var player = new MacroPlayer(instance.WindowHandle, ResolveActionBinding, () => NextSystemTestOverride, AutoWaitMs);
            _ = RunStepAsync(player, cts);
        }

        /// <summary>
        /// If EditingMacro's script references {TRITIUM_LOOPS}, resolves it from
        /// TritiumLoopsTestOverride first, re-flattening the script against that fresh value
        /// (skipped entirely otherwise - see RunPlaybackAsync for why this isn't unconditional).
        /// Then runs whichever instruction that leaves next in line - unless the *next* one is a
        /// WAIT (its own duration already paces things - though this can't currently happen here,
        /// since Flatten drops WAITs from the stepped sequence entirely) or there is no next
        /// instruction (the script is about to wrap back to the start), pauses for AutoWaitMs
        /// afterward so the user has a moment to see the game react before pressing Step again -
        /// the same rule (and the same setting) MacroPlayer itself applies during a full Play or
        /// Auto Pilot run (see MacroPlayer.RunAsync), just evaluated here instead since Step
        /// executes one pre-flattened instruction per call rather than a whole script in one go.
        /// </summary>
        private async Task RunStepAsync(MacroPlayer player, CancellationTokenSource cts)
        {
            try
            {
                if (ReferencesTritiumLoops(EditingMacro?.ScriptText))
                {
                    await ResolveTritiumLoopsAsync(useTestValues: true);
                    cts.Token.ThrowIfCancellationRequested();
                }

                EnsureStepState();
                if (_stepInstructions.Count == 0)
                {
                    return;
                }

                _stepIndex %= _stepInstructions.Count; // clamp - the fresh flatten may be a different length
                var instruction = _stepInstructions[_stepIndex];
                _stepIndex = (_stepIndex + 1) % _stepInstructions.Count;
                var isFinalStep = _stepIndex == 0; // wrapped back to the start - this was the last instruction
                var nextIsWait = !isFinalStep && _stepInstructions[_stepIndex] is MacroInstruction.Wait;
                OnPropertyChanged(nameof(StepStatusText));

                await player.RunSingleStepAsync(instruction, cts.Token);

                if (!isFinalStep && !nextIsWait && AutoWaitMs > 0)
                {
                    await Task.Delay(AutoWaitMs, cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (ReferenceEquals(_stepCts, cts))
                {
                    IsStepping = false;
                }
            }
        }

        /// <summary>Renders one flattened instruction back into (approximately) its script-line form, for StepStatusText's "Next: …" label.</summary>
        private static string DescribeInstruction(MacroInstruction instruction) => instruction switch
        {
            MacroInstruction.Tap tap => tap.Token,
            MacroInstruction.Hold hold => $"HOLD {hold.Token} {hold.DurationMs}",
            MacroInstruction.Click click => $"CLICK {click.X},{click.Y}",
            MacroInstruction.HoldClick holdClick => $"HOLD CLICK {holdClick.X},{holdClick.Y} {holdClick.DurationMs}",
            MacroInstruction.Paste paste => $"PASTE {paste.Text}",
            _ => instruction.GetType().Name
        };

        /// <summary>
        /// Resolves {TRITIUM_LOOPS}, cached into _lastTritiumLoops for EnsureStepState's preview.
        /// <paramref name="useTestValues"/> true (every manual Play/Step in this tab) parses
        /// TritiumLoopsTestOverride directly and skips the CMDR rescan entirely - the whole point
        /// of the test-value fields is trying a script out without a running instance in the right
        /// cargo/fuel state. False (PlayMacro only, i.e. an Auto Pilot-triggered run) rescans both
        /// this tab's own instance scan and the Roles tab's (via the _refreshRolesAsync closure)
        /// so cargo/carrier-fuel data is current, then computes it for real - this tab's own
        /// SelectedInstance if one happens to be selected here, otherwise the Engineer's
        /// currently-assigned instance (the normal case for an Auto Pilot-triggered run, where
        /// nothing is selected in this tab at all).
        /// </summary>
        private async Task<int> ResolveTritiumLoopsAsync(bool useTestValues)
        {
            if (useTestValues)
            {
                _lastTritiumLoops = int.TryParse(TritiumLoopsTestOverride, out var testLoops) && testLoops >= 0
                    ? testLoops
                    : 0;
            }
            else
            {
                await Task.WhenAll(RefreshAsync(), _refreshRolesAsync());

                var instance = SelectedInstance ?? _getEngineerInstance();
                _lastTritiumLoops = ComputeTritiumLoops(instance);
            }

            // EnsureStepState's cache only ever looks at raw script text, so it can't see that
            // _lastTritiumLoops just changed on its own - force the next call to re-flatten
            // against it rather than reusing a stale preview built from the old value.
            _stepScriptSnapshot = null;

            return _lastTritiumLoops;
        }

        /// <summary>
        /// How many full ship-loads of tritium <paramref name="instance"/> still needs to buy or
        /// mine to fill its carrier's fuel depot to 1000t and leave its own cargo hold topped off
        /// too, net of whatever tritium it's already carrying. Requires a known, positive cargo
        /// capacity to divide by, and a known carrier fuel level - returns 0 (nothing computable)
        /// rather than risk dividing by a zero/unknown capacity, or - previously the actual bug
        /// here - silently treating an unknown fuel level as 0 (empty), which would wildly
        /// overstate how many loops are still needed (continuing to mine/deposit long after the
        /// depot and hold were, in reality, already full) instead of just declining to guess.
        /// </summary>
        private static int ComputeTritiumLoops(EliteInstanceViewModel? instance)
        {
            var capacity = instance?.CargoCapacity ?? 0;
            if (capacity <= 0 || instance!.CarrierFuelLevel is not { } carrierFuel)
            {
                return 0;
            }

            var onBoard = instance.CurrentTritium ?? 0;

            var carrierNeeded = Math.Max(0, TritiumDepotCapacity - carrierFuel);
            var totalNeeded = Math.Max(0, carrierNeeded + capacity - onBoard);

            return (int)Math.Ceiling(totalNeeded / (double)capacity);
        }

        /// <summary>Substitutes the literal "{TRITIUM_LOOPS}" placeholder with loops, wherever it appears in scriptText - most usefully as a REPEAT count (e.g. "REPEAT {TRITIUM_LOOPS}"), resolved here (before parsing) rather than at play time like {NEXT_SYSTEM}/{CENTRE}, since a REPEAT's count has to be known before the script can even be parsed/flattened into steps.</summary>
        private static string SubstituteTritiumLoops(string scriptText, int loops) =>
            scriptText.Replace(TritiumLoopsPlaceholder, loops.ToString(), StringComparison.Ordinal);

        private static bool ReferencesTritiumLoops(string? scriptText) =>
            scriptText != null && scriptText.Contains(TritiumLoopsPlaceholder, StringComparison.Ordinal);

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
