using System.Linq;
using RouteJumper.Common;
using RouteJumper.Models;
using RouteJumper.Sequencing;
using RouteJumper.Services;
using RouteJumper.Services.Companion;

namespace RouteJumper.ViewModels
{
    /// <summary>
    /// Top-level ViewModel for the main window: hosts the three tab ViewModels, and wires the
    /// shared row-event trigger between them (Roles' Captain journal watcher raises row events;
    /// Route's sequencer consumes them; AutoPilotController also raises one of its own -
    /// RowEventKind.Plotting - the instant it starts playing the Captain's macro, rather than
    /// waiting on anything journal-derived), plus RouteViewModel.RouteSaved ->
    /// RolesViewModel.RefreshRouteForCurrentCaptain, a read-only closure over RouteViewModel.Rows
    /// so ControlsViewModel can resolve a macro's "next system" paste placeholder, closures over
    /// RolesViewModel.EngineerInstance/RefreshAsync so ControlsViewModel can resolve a macro's
    /// TRITIUM_LOOPS placeholder against the Engineer's freshly-rescanned cargo/carrier-fuel data,
    /// and closures over RolesViewModel/ControlsViewModel so AutoPilotController can drive Auto
    /// Pilot (Route tab §4.2) by playing the Captain's selected macro (Roles tab §5.5) to plot
    /// each jump, and the Engineer's (if assigned) to refuel once each Cooldown starts, both
    /// through ControlsViewModel.PlayMacro - none of the tab ViewModels reference each other directly;
    /// this class is the only place that does. RolesViewModel.RefreshAsync is also passed
    /// directly to AutoPilotController (not routed through ControlsViewModel) so its "panic mode"
    /// (§4.7) can rescan for a fresh carrier fuel reading after the Engineer's macro finishes;
    /// RouteViewModel.StopAutoPilot is passed twice - once as the "route completed" stop, once as
    /// the "panic" stop - since both are the same underlying action from Auto Pilot's own
    /// perspective, just reached for different reasons. Also owns the single AppSettingsStore
    /// both tabs persist to/restore from, and the single AppConfigStore both Roles' and Controls'
    /// own independent EliteInstanceScanner instances read the journal folder from.
    /// </summary>
    public class MainViewModel : ObservableObject
    {
        private const string TrackingModeSettingKey = "TrackingMode";

        // Fixed tab order - see MainWindow.xaml's TabControl.Columns. Not persisted (SPEC §7:
        // the last-selected tab never is) - only used to carry the *current* selection across a
        // mode switch, within a single running session.
        private const int RouteTabIndex = 0;
        private const int RolesTabIndex = 1;
        private const int ControlsTabIndex = 2;
        private const int TrackTabIndex = 3;

        private readonly AppSettingsStore _settings;
        private readonly AppConfigStore _config;
        private TrackingMode _mode;
        private int _selectedTabIndex = RouteTabIndex;

        public MainViewModel()
        {
            _settings = new AppSettingsStore();
            _config = new AppConfigStore();
            var routeEventTrigger = new ManualRowEventTrigger();

            SpeechAnnouncer = new SpeechAnnouncer(_settings, new SapiSpeechEngine());
            UpdatePreferences = new UpdatePreferences(_settings);

            // One shared instance across all three ViewModels below - not just each defaulting to
            // its own (which would still share the same underlying on-disk cache via AppSettingsStore
            // just fine, and did before this). The instance itself must be shared so RouteViewModel's
            // DataSeeded subscription (its live-refresh debounce - see its own constructor) actually
            // observes seeds made through RolesViewModel's/TrackViewModel's own CarrierRouteJournalWatcher/
            // ShipRouteJournalWatcher, which write through this same object rather than a separate one.
            var starSystemLookupService = new EdsmStarSystemLookupService(_config);

            // The RolesViewModel/ControlsViewModel property dereferences below are guaranteed
            // safe despite still being unassigned at this exact statement - these closures are
            // only ever invoked later, once the whole constructor (and both assignments) has
            // completed; the nullable analyzer can't see that far ahead through a deferred
            // lambda, hence the null-forgiving operators.
            RouteViewModel = new RouteViewModel(
                _settings,
                routeEventTrigger,
                () => RolesViewModel!.CanEngageAutoPilot,
                starSystemLookupService,
                () => _mode == TrackingMode.Ship
                    ? TrackViewModel!.Instances.FirstOrDefault(i => i.IsTracked)?.CurrentSystem
                    : RolesViewModel!.CaptainInstance?.CurrentSystem,
                () => _config.JournalDirectory,
                () => RolesViewModel!.CaptainInstance != null,
                () => RolesViewModel!.CaptainInstance?.CarrierSystem);
            RolesViewModel = new RolesViewModel(
                routeEventTrigger,
                _settings,
                new EliteInstanceScanner(_config),
                () => ControlsViewModel!.Macros,
                starSystemLookupService);
            TrackViewModel = new TrackViewModel(routeEventTrigger, _settings, new EliteInstanceScanner(_config), starSystemLookupService);
            ControlsViewModel = new ControlsViewModel(
                _settings,
                new EliteInstanceScanner(_config),
                () => RouteViewModel.Rows.FirstOrDefault(r => r.Icon == RowIcon.InProgress)?.SystemText,
                () => RolesViewModel.EngineerInstance,
                RolesViewModel.RefreshAsync);

            RouteViewModel.RouteSaved += (_, _) =>
            {
                RolesViewModel.RefreshRouteForCurrentCaptain();
                TrackViewModel.RefreshRouteForCurrentTrackedInstance();
            };
            // Wired before the mode-restore block below (not after, alongside RestoreFromSettings)
            // so a route restored as Neutron/Galaxy retroactively forces Ship mode on startup too -
            // including one saved under Fleet Carrier mode in an earlier session, before this
            // existed - with no new persisted key needed (TrackingMode/RouteType are both already
            // persisted; see OnRouteTypeChanged).
            RouteViewModel.RouteTypeChanged += (_, type) => OnRouteTypeChanged(type);
            RolesViewModel.AutoPilotEligibilityChanged += (_, _) =>
            {
                RouteViewModel.RaiseAutoPilotEligibilityChanged();

                // Requirements dropping out from under a run already in progress (Captain
                // unassigned, their instance closed, a selected macro deleted, ...) stops it
                // outright, not just disables re-engaging it next time.
                if (RouteViewModel.IsAutoPilotRunning && !RolesViewModel.CanEngageAutoPilot)
                {
                    RouteViewModel.StopAutoPilot();
                }
            };
            ControlsViewModel.MacroDeleted += (_, macro) => RolesViewModel.OnMacroDeleted(macro);

            // Hides the Route tab's companion QR/link button for the duration of any macro
            // execution (Auto Pilot's own Captain plot/Engineer refuel, or a manual Play/Step
            // from the Controls tab - all three run through the same ControlsViewModel.PlayMacro/
            // MacroPlayer channel, §4.7) - clicking "Open in browser" mid-script risks stealing
            // focus from the target Elite Dangerous window, which MacroPlayer treats as a
            // playback failure (panic mode).
            ControlsViewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(ControlsViewModel.IsPlaying) or nameof(ControlsViewModel.IsStepping))
                {
                    RouteViewModel.SetMacroExecuting(ControlsViewModel.IsPlaying || ControlsViewModel.IsStepping);
                }
            };

            var autoPilotController = new AutoPilotController(
                RouteViewModel.Rows,
                () => RolesViewModel.CaptainMacro,
                () => RolesViewModel.CaptainInstance,
                () => RolesViewModel.EngineerMacro,
                () => RolesViewModel.EngineerInstance,
                () => ControlsViewModel.AutoPilotDelayMs,
                ControlsViewModel.PlayMacro,
                RolesViewModel.RefreshAsync,
                RouteViewModel.StopAutoPilot,
                RouteViewModel.StopAutoPilot,
                ControlsViewModel.ReportPlaybackError,
                SpeechAnnouncer.Speak,
                routeEventTrigger);
            RouteViewModel.AutoPilotRunningChanged += (_, running) =>
            {
                if (running)
                {
                    autoPilotController.Start();
                }
                else
                {
                    autoPilotController.Stop();
                }
            };

            // Companion site (SPEC §13): publishes Auto Pilot's key events to Firestore so the
            // Angular app under /app can show a live, read-only feed. A third, independent
            // subscriber to routeEventTrigger.RowTriggered - alongside RouteSequencer's own
            // AttachRowTrigger and RouteViewModel's own OnLiveCarrierLocation subscription -
            // never touches Sequencing/ itself (CLAUDE.md). Every publish call is best-effort and
            // fire-and-forget; nothing here is ever awaited on Auto Pilot's own critical path.
            var companionPublisher = new CompanionSessionPublisher(() => _config.CompanionSessionRetentionHours);

            // Self-managed housekeeping (SPEC §13) - Firestore's own TTL feature turned out to
            // require the paid Blaze plan even for a single delete, so instead the desktop app
            // (the only writer, and so the only thing that ever knows which sessions exist)
            // deletes whatever it locally recorded as due once per launch. Fire-and-forget: never
            // blocks startup, and a failed attempt just retries in full next launch.
            _ = companionPublisher.CleanUpExpiredSessionsAsync();

            autoPilotController.EngineerRefuelSucceeded += (_, fuelLevel) =>
                companionPublisher.PublishEvent(
                    CompanionEventKind.Refueled,
                    RolesViewModel.CaptainInstance?.CarrierSystem ?? string.Empty,
                    $"Refueled - {(fuelLevel is { } level ? $"{level}t" : "unknown level")}");

            autoPilotController.PanicOccurred += (_, message) =>
            {
                companionPublisher.PublishEvent(CompanionEventKind.Panic, string.Empty, message);
                companionPublisher.EndSession(panicked: true);
            };

            routeEventTrigger.RowTriggered += (_, e) =>
            {
                switch (e.Kind)
                {
                    case RowEventKind.Plotted:
                        companionPublisher.PublishEvent(CompanionEventKind.Plotted, e.SystemName, $"Jump plotted to {e.SystemName}");
                        break;
                    case RowEventKind.Arrived:
                        companionPublisher.PublishEvent(CompanionEventKind.Arrived, e.SystemName, $"Arrived at {e.SystemName}");
                        break;
                }
            };

            RouteViewModel.AutoPilotRunningChanged += (_, running) =>
            {
                if (running)
                {
                    _ = StartCompanionSessionAsync(companionPublisher);
                }
                else
                {
                    // Already a no-op if PanicOccurred (above) already ended this same session as
                    // panicked - EndSession clears CurrentSessionId synchronously before this can
                    // ever run, since Panic() raises PanicOccurred before calling _stopAutoPilot
                    // (which is what raises this event in the first place).
                    companionPublisher.EndSession(panicked: false);
                    RouteViewModel.SetCompanionSession(null, null);
                }
            };

            // Restores the persisted TrackingMode (default FleetCarrier) and applies it - must
            // run before RestoreFromSettings below, so RouteViewModel's Auto Pilot button
            // visibility and whichever of Roles/Track is active are correct *before* a restored
            // route (and any Captain/tracked-instance catch-up it triggers) is applied. Safe to
            // call synchronously here despite RolesViewModel/TrackViewModel's own constructors
            // having already kicked off an async scan of their own (Task.Run, still suspended at
            // this point) - nothing can resume either scan's continuation until this whole
            // constructor returns and the WPF Dispatcher gets a chance to run again, so ApplyMode
            // is guaranteed to set each ViewModel's active state before either one's first
            // scan-completion continuation ever executes.
            _mode = Enum.TryParse<TrackingMode>(_settings.GetString(TrackingModeSettingKey), out var restoredMode)
                ? restoredMode
                : TrackingMode.FleetCarrier;
            ApplyMode(_mode);

            // Must run after the RouteSaved wiring above - see RouteViewModel.RestoreFromSettings.
            RouteViewModel.RestoreFromSettings();

            // RestoreFromSettings' own Save() call above captures its Distance/Star Type origin
            // (the closure passed into RouteViewModel's constructor) before RolesViewModel's/
            // TrackViewModel's own startup instance scan - still in flight, suspended at its own
            // Task.Run - has resolved a restored Captain/tracked instance, so row 1's Distance
            // comes back blank on a normal relaunch even though one is about to be restored
            // moments later. Re-triggering enrichment once that scan actually finishes closes the
            // gap; RefreshEnrichment is cheap to call regardless (a no-op if no route is saved).
            _ = RefreshEnrichmentAfterInitialScanAsync();
        }

        private async Task RefreshEnrichmentAfterInitialScanAsync()
        {
            await (_mode == TrackingMode.Ship ? TrackViewModel.InitialScanTask : RolesViewModel.InitialScanTask);
            RouteViewModel.RefreshEnrichment();
        }

        /// <summary>
        /// Starts (or, on an unchanged route, reactivates - see CompanionSessionPublisher's own
        /// doc comment) the companion session (SPEC §13) the moment Auto Pilot is engaged, header
        /// named after the currently-saved route's own first/last row (not the CMDR's live origin
        /// position - a simpler, always-available summary of "this route", the same pair a phone
        /// user glancing at the companion site would expect to see) - then renders and shows the
        /// QR code once the session id comes back. Never blocks Auto Pilot itself: this runs as a
        /// detached fire-and-forget task from the AutoPilotRunningChanged handler above, and a
        /// failed/slow start here has no bearing on Auto Pilot actually starting to drive the
        /// route.
        /// </summary>
        private async Task StartCompanionSessionAsync(CompanionSessionPublisher publisher)
        {
            if (RouteViewModel.Rows.Count == 0)
            {
                return;
            }

            var routeSystems = RouteViewModel.Rows.Select(row => row.SystemText).ToList();

            if (await publisher.StartSessionAsync(routeSystems) is not { } sessionId)
            {
                return;
            }

            // _config.CompanionSiteBaseUrl is read fresh here (not cached) so a hand-edit to
            // routejumper.conf - e.g. pointing it at a local `ng serve` instance for testing -
            // takes effect on the next Auto Pilot engage with no restart required.
            var url = new Uri($"{_config.CompanionSiteBaseUrl}/#/session/{sessionId}");
            RouteViewModel.SetCompanionSession(url, QrCodeImageFactory.Generate(url.ToString()));
        }

        public RouteViewModel RouteViewModel { get; }

        public RolesViewModel RolesViewModel { get; }

        public TrackViewModel TrackViewModel { get; }

        public ControlsViewModel ControlsViewModel { get; }

        /// <summary>
        /// A fresh snapshot of the CMDR's own ship - the Captain's (Fleet Carrier mode) or the
        /// tracked instance's (Ship mode), the same instance RouteViewModel's own origin-distance
        /// closure above already reads CurrentSystem from - used to pre-fill the Spansh dialog's
        /// Neutron Plotter tab (SPEC §4.12) when it's opened (MainWindow.OnSpanshClick). Read on
        /// demand rather than a persisted/bound property, matching SpanshImportViewModel's own
        /// "re-read fresh every time the dialog opens" convention for routejumper.conf.
        ///
        /// Deliberately doesn't also surface MaxJumpRange - Loadout's own MaxJumpRange reflects
        /// whatever fuel/cargo the ship happened to be carrying when last logged, which may not
        /// match whatever load-out the CMDR actually wants to plan the route around, so
        /// pre-filling Range from it would presume rather than help.
        /// EliteInstanceViewModel.MaxJumpRange itself is left in place regardless, captured now
        /// for a future smarter default to build on (the same "captured now, consumed once a real
        /// feature needs it" precedent id64/system-address caching already follows elsewhere,
        /// §4.9) - the CMDR types Range in by hand for now.
        ///
        /// KnownCarrierSystem is a distinct, Fleet-Carrier-mode-only concept - the Captain's own
        /// fleet carrier's real current location (EliteInstanceViewModel.CarrierSystem, the same
        /// field Trim for FC anchors from), not the Captain's own ship's position. Used to pre-fill
        /// the Spansh dialog's Fleet Carrier tab's own Source. Always null in Ship mode - the
        /// tracked instance's own ship carries no fleet-carrier concept at all.
        ///
        /// JournalFilePath/CurrentCargo feed the Spansh dialog's Galaxy Plotter tab (SPEC §4.12):
        /// JournalFilePath is what EliteInstanceScanner.ReadLoadoutSnapshot re-reads in the
        /// background to build that tab's own SLEF ship build, and CurrentCargo pre-fills its own
        /// Cargo field - both from the same resolved instance as CurrentSystem/HasOverchargedFsd
        /// above, so no new resolution logic is needed for either.
        /// </summary>
        public (string? CurrentSystem, string? KnownCarrierSystem, bool HasOverchargedFsd, string? JournalFilePath, int? CurrentCargo) GetKnownShipState()
        {
            var instance = _mode == TrackingMode.Ship
                ? TrackViewModel.Instances.FirstOrDefault(i => i.IsTracked)
                : RolesViewModel.CaptainInstance;
            var knownCarrierSystem = _mode == TrackingMode.Ship ? null : RolesViewModel.CaptainInstance?.CarrierSystem;
            return (instance?.CurrentSystem, knownCarrierSystem, instance?.HasOverchargedFsd ?? false, instance?.JournalFilePath, instance?.CurrentCargo);
        }

        /// <summary>Owns spoken-announcement voice/volume/mute state - bound directly by MainWindow's mute button and the Preferences dialog.</summary>
        public SpeechAnnouncer SpeechAnnouncer { get; }

        /// <summary>Owns whether the silent startup update check (§3.7) runs at all - bound directly by the Preferences dialog's own "Updates" section.</summary>
        public UpdatePreferences UpdatePreferences { get; }

        /// <summary>
        /// File &gt; Ship Mode toggle (a checkable MenuItem binds directly to this). Switches
        /// between Fleet Carrier mode (Roles/Controls tabs, Auto Pilot) and Ship mode (Track tab,
        /// no automation) - see ApplyMode. Persisted immediately; restored at startup.
        ///
        /// Attempting to switch to Fleet Carrier mode while the saved route is Neutron/Galaxy-typed
        /// (RouteViewModel.RouteType) is silently ignored - CanSelectFleetCarrierMode already keeps
        /// the Fleet Carrier chip disabled in that state, but this keeps the invariant true
        /// regardless of call path. See OnRouteTypeChanged for the other half (forcing Ship mode
        /// the instant such a route is saved/restored).
        /// </summary>
        public bool IsShipMode
        {
            get => _mode == TrackingMode.Ship;
            set
            {
                var newMode = value ? TrackingMode.Ship : TrackingMode.FleetCarrier;
                if (newMode == TrackingMode.FleetCarrier && RouteViewModel.RouteType != RouteType.Plain)
                {
                    return;
                }

                if (_mode == newMode)
                {
                    return;
                }

                // Computed from the *old* mode's own selection, before ApplyMode below hides the
                // tab it might currently be pointing at.
                SelectedTabIndex = MapSelectedTabAcrossModeSwitch(newMode, SelectedTabIndex);

                _mode = newMode;
                _settings.SetString(TrackingModeSettingKey, _mode.ToString());
                ApplyMode(_mode);
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Drives the Fleet Carrier chip's own IsEnabled - false for as long as the saved route is
        /// Neutron/Galaxy-typed (a fleet carrier's own Auto Pilot has no notion of a neutron boost
        /// or an FSD injection, so running it against one of these routes was never meaningful).
        /// Re-enables the instant the route reverts to Plain (Edit-&gt;Save, Import Current Route,
        /// Trim for FC) - this never switches back to Fleet Carrier mode on its own, only makes it
        /// selectable again.
        /// </summary>
        public bool CanSelectFleetCarrierMode => RouteViewModel.RouteType == RouteType.Plain;

        /// <summary>
        /// The Fleet Carrier chip's own tooltip (bound in place of a static string) - the normal
        /// explanatory text while the route is Plain, or why it's currently disabled otherwise.
        /// Only ever actually shown while Ship mode is selected (ToolTipService.IsEnabled on the
        /// chip itself, MainWindow.xaml) - which a non-Plain route always forces anyway, so the
        /// disabled chip's own explanation is never hidden behind "this is already the active mode".
        /// </summary>
        public string FleetCarrierModeTooltip => RouteViewModel.RouteType switch
        {
            RouteType.Neutron => "Not appropriate for Neutron Plotter routes. Edit the route to revert it to a plain route, then you can switch back to Fleet Carrier mode.",
            RouteType.Galaxy => "Not appropriate for Galaxy Plotter routes. Edit the route to revert it to a plain route, then you can switch back to Fleet Carrier mode.",
            _ => "Track a Fleet Carrier's progress via a Captain's journal, with optional Auto Pilot automation."
        };

        /// <summary>
        /// Forces Ship mode the instant the saved route becomes Neutron/Galaxy-typed (a no-op if
        /// already there), and keeps CanSelectFleetCarrierMode/FleetCarrierModeTooltip in sync
        /// either way - called from RouteViewModel.RouteTypeChanged, wired in the constructor.
        /// </summary>
        private void OnRouteTypeChanged(RouteType type)
        {
            if (type != RouteType.Plain)
            {
                IsShipMode = true;
            }

            OnPropertyChanged(nameof(CanSelectFleetCarrierMode));
            OnPropertyChanged(nameof(FleetCarrierModeTooltip));
        }

        /// <summary>
        /// The TabControl's own selection (two-way bound) - not persisted (SPEC §7), but carried
        /// across a mode switch within the running session so the user never lands on a tab that
        /// just became hidden. See MapSelectedTabAcrossModeSwitch.
        /// </summary>
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set => SetProperty(ref _selectedTabIndex, value);
        }

        /// <summary>
        /// Keeps the selected tab meaningful across a mode switch, rather than leaving it
        /// pointing at a tab that's about to be hidden: entering Ship mode from Controls moves to
        /// Route (Controls has no Ship-mode equivalent); from Roles moves to Track (its closest
        /// equivalent); Route stays Route. Leaving Ship mode maps Track back to Roles; anything
        /// else (i.e. Route) stays put.
        /// </summary>
        private static int MapSelectedTabAcrossModeSwitch(TrackingMode newMode, int currentTabIndex)
        {
            if (newMode == TrackingMode.Ship)
            {
                return currentTabIndex switch
                {
                    ControlsTabIndex => RouteTabIndex,
                    RolesTabIndex => TrackTabIndex,
                    _ => currentTabIndex
                };
            }

            return currentTabIndex == TrackTabIndex ? RolesTabIndex : currentTabIndex;
        }

        /// <summary>
        /// Activates exactly one of RolesViewModel/TrackViewModel's own journal watcher (see each
        /// one's SetActive) and updates the Route tab's Auto Pilot button visibility to match -
        /// exactly one tracking source is ever live at a time, matching "alternative mode," not
        /// "both at once."
        /// </summary>
        private void ApplyMode(TrackingMode mode)
        {
            var isShip = mode == TrackingMode.Ship;
            RolesViewModel.SetActive(!isShip);
            TrackViewModel.SetActive(isShip);
            RouteViewModel.SetShipMode(isShip);
        }
    }
}
