using System.Linq;
using RouteJumper.Common;
using RouteJumper.Models;
using RouteJumper.Sequencing;
using RouteJumper.Services;

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
        private TrackingMode _mode;
        private int _selectedTabIndex = RouteTabIndex;

        public MainViewModel()
        {
            _settings = new AppSettingsStore();
            var config = new AppConfigStore();
            var routeEventTrigger = new ManualRowEventTrigger();

            SpeechAnnouncer = new SpeechAnnouncer(_settings, new SapiSpeechEngine());
            UpdatePreferences = new UpdatePreferences(_settings);

            // One shared instance across all three ViewModels below - not just each defaulting to
            // its own (which would still share the same underlying on-disk cache via AppSettingsStore
            // just fine, and did before this). The instance itself must be shared so RouteViewModel's
            // DataSeeded subscription (its live-refresh debounce - see its own constructor) actually
            // observes seeds made through RolesViewModel's/TrackViewModel's own CarrierRouteJournalWatcher/
            // ShipRouteJournalWatcher, which write through this same object rather than a separate one.
            var starSystemLookupService = new EdsmStarSystemLookupService(_settings, config);

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
                    : RolesViewModel!.CaptainInstance?.CurrentSystem);
            RolesViewModel = new RolesViewModel(
                routeEventTrigger,
                _settings,
                new EliteInstanceScanner(config),
                () => ControlsViewModel!.Macros,
                starSystemLookupService);
            TrackViewModel = new TrackViewModel(routeEventTrigger, _settings, new EliteInstanceScanner(config), starSystemLookupService);
            ControlsViewModel = new ControlsViewModel(
                _settings,
                new EliteInstanceScanner(config),
                () => RouteViewModel.Rows.FirstOrDefault(r => r.Icon == RowIcon.InProgress)?.SystemText,
                () => RolesViewModel.EngineerInstance,
                RolesViewModel.RefreshAsync);

            RouteViewModel.RouteSaved += (_, _) =>
            {
                RolesViewModel.RefreshRouteForCurrentCaptain();
                TrackViewModel.RefreshRouteForCurrentTrackedInstance();
            };
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

        public RouteViewModel RouteViewModel { get; }

        public RolesViewModel RolesViewModel { get; }

        public TrackViewModel TrackViewModel { get; }

        public ControlsViewModel ControlsViewModel { get; }

        /// <summary>Owns spoken-announcement voice/volume/mute state - bound directly by MainWindow's mute button and the Preferences dialog.</summary>
        public SpeechAnnouncer SpeechAnnouncer { get; }

        /// <summary>Owns whether the silent startup update check (§3.7) runs at all - bound directly by the Preferences dialog's own "Updates" section.</summary>
        public UpdatePreferences UpdatePreferences { get; }

        /// <summary>
        /// File &gt; Ship Mode toggle (a checkable MenuItem binds directly to this). Switches
        /// between Fleet Carrier mode (Roles/Controls tabs, Auto Pilot) and Ship mode (Track tab,
        /// no automation) - see ApplyMode. Persisted immediately; restored at startup.
        /// </summary>
        public bool IsShipMode
        {
            get => _mode == TrackingMode.Ship;
            set
            {
                var newMode = value ? TrackingMode.Ship : TrackingMode.FleetCarrier;
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
