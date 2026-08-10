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
    /// this class is the only place that does. Also owns the single AppSettingsStore both tabs
    /// persist to/restore from, and the single AppConfigStore both Roles' and Controls' own
    /// independent EliteInstanceScanner instances read the journal folder from.
    /// </summary>
    public class MainViewModel : ObservableObject
    {
        public MainViewModel()
        {
            var settings = new AppSettingsStore();
            var config = new AppConfigStore();
            var routeEventTrigger = new ManualRowEventTrigger();

            SpeechAnnouncer = new SpeechAnnouncer(settings, new SapiSpeechEngine());

            // The RolesViewModel/ControlsViewModel property dereferences below are guaranteed
            // safe despite still being unassigned at this exact statement - these closures are
            // only ever invoked later, once the whole constructor (and both assignments) has
            // completed; the nullable analyzer can't see that far ahead through a deferred
            // lambda, hence the null-forgiving operators.
            RouteViewModel = new RouteViewModel(settings, routeEventTrigger, () => RolesViewModel!.CanEngageAutoPilot);
            RolesViewModel = new RolesViewModel(
                routeEventTrigger,
                settings,
                new EliteInstanceScanner(config),
                () => ControlsViewModel!.Macros);
            ControlsViewModel = new ControlsViewModel(
                settings,
                new EliteInstanceScanner(config),
                () => RouteViewModel.Rows.FirstOrDefault(r => r.Icon == RowIcon.InProgress)?.SystemText,
                () => RolesViewModel.EngineerInstance,
                RolesViewModel.RefreshAsync);

            RouteViewModel.RouteSaved += (_, _) => RolesViewModel.RefreshRouteForCurrentCaptain();
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
                (macro, instance) => ControlsViewModel.PlayMacro(macro, instance),
                RouteViewModel.StopAutoPilot,
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

            // Must run after the RouteSaved wiring above - see RouteViewModel.RestoreFromSettings.
            RouteViewModel.RestoreFromSettings();
        }

        public RouteViewModel RouteViewModel { get; }

        public RolesViewModel RolesViewModel { get; }

        public ControlsViewModel ControlsViewModel { get; }

        /// <summary>Owns spoken-announcement voice/volume/mute state - bound directly by MainWindow's mute button and the Preferences dialog.</summary>
        public SpeechAnnouncer SpeechAnnouncer { get; }
    }
}
