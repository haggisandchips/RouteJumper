using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RouteJumper.Common;
using RouteJumper.Models;
using RouteJumper.Sequencing;
using RouteJumper.Services;
using RouteJumper.Services.Logging;

namespace RouteJumper.ViewModels
{
    /// <summary>
    /// ViewModel for the "Route" tab.
    /// </summary>
    public class RouteViewModel : ObservableObject
    {
        private const string RouteTextSettingKey = "RouteText";
        private const string RouteTypeSettingKey = "RouteType";
        private const string RouteMetadataSettingKey = "RouteRowMetadata";

        private static readonly JsonSerializerOptions RouteMetadataJsonOptions = new() { PropertyNameCaseInsensitive = true };

        /// <summary>Fleet carriers' own real-world maximum jump range - the threshold TrimToJumpRange collapses the route's intermediate rows against.</summary>
        public const double MaxCarrierJumpLightYears = 500.0;

        private readonly RouteSequencer _sequencer;
        private readonly AppSettingsStore _settings;
        private readonly Func<bool> _canEngageAutoPilot;
        private readonly IStarSystemLookupService _starSystemLookupService;
        private readonly RouteRowEnrichmentService _enrichmentService;
        private readonly Func<string?> _getOriginSystemName;
        private readonly Func<string> _getJournalDirectory;
        private readonly Func<bool> _isCaptainAssigned;
        private readonly Func<string?> _getCarrierSystemName;
        private CancellationTokenSource? _enrichmentCts;

        private string _routeText = string.Empty;
        private string? _lastSavedRouteText;
        private RouteType _routeType = RouteType.Plain;
        private bool _isSaved;
        private bool _isAutoPilotRunning;
        private bool _showAutoPilotButton = true;
        private bool _showTrimButton = true;
        private bool _autoCopyToClipboardEnabled;
        private bool _hasUnresolvedSystems;
        private bool _unresolvedSystemsBannerDismissed;
        private RouteRowViewModel? _clipboardSourceRow;
        private uint _expectedClipboardSequenceNumber;
        private readonly DispatcherTimer _progressTimer;
        private readonly DispatcherTimer _dataSeededDebounceTimer;

        /// <summary>
        /// <paramref name="canEngageAutoPilot"/> resolves whether the Roles tab currently has
        /// everything Auto Pilot needs (Captain assigned with a macro selected, and the same for
        /// Engineer if *it's* assigned too) - supplied by MainViewModel as a closure over
        /// RolesViewModel, the same one-way, event-free bridging pattern used elsewhere, since
        /// this ViewModel has no reference to RolesViewModel itself. Defaults to always-true so
        /// existing callers/tests that don't care about role/macro gating keep working.
        ///
        /// <paramref name="starSystemLookupService"/>/<paramref name="getOriginSystemName"/> drive
        /// the Distance/Star Type columns (see Save's own enrichment trigger below) -
        /// <paramref name="getOriginSystemName"/> resolves row 1's "previous" system (the CMDR's
        /// own current system at Save time), the same closure-over-another-tab's-ViewModel
        /// bridging pattern as <paramref name="canEngageAutoPilot"/>. Both default to a real EDSM
        /// lookup / "unknown" respectively, so existing callers/tests that don't care about
        /// enrichment keep working unchanged.
        ///
        /// <paramref name="getJournalDirectory"/> resolves the configured journal folder
        /// (AppConfigStore.JournalDirectory) ImportFromNavRoute reads NavRoute.json out of -
        /// deliberately not tied to any particular assigned Captain/tracked instance, since
        /// NavRoute.json is a per-installation file (the same "one physical file regardless of how
        /// many instances are running" caveat SPEC §5.2 already notes for Status.json/Cargo.json),
        /// not something a specific running instance's own journal path could reliably resolve
        /// anyway. Defaults to a real AppConfigStore read, so existing callers/tests that don't
        /// care about Import keep working unchanged.
        ///
        /// <paramref name="isCaptainAssigned"/>/<paramref name="getCarrierSystemName"/> gate and
        /// feed TrimToJumpRange - trimming needs the Captain's own fleet carrier's real current
        /// location (never the pasted route's own row 1, which may no longer be where the carrier
        /// actually is) to anchor the first hop efficiently, so both are required before it will
        /// run at all. Default to always-assigned/unknown-location respectively - a caller that
        /// doesn't care about Trim for FC never needs to supply real closures, though TrimToJumpRange
        /// itself won't proceed past its own gating with the unknown-location default.
        /// </summary>
        public RouteViewModel(
            AppSettingsStore settings,
            IRowEventTrigger? rowEventTrigger = null,
            Func<bool>? canEngageAutoPilot = null,
            IStarSystemLookupService? starSystemLookupService = null,
            Func<string?>? getOriginSystemName = null,
            Func<string>? getJournalDirectory = null,
            Func<bool>? isCaptainAssigned = null,
            Func<string?>? getCarrierSystemName = null)
        {
            _settings = settings;
            _canEngageAutoPilot = canEngageAutoPilot ?? (() => true);
            _starSystemLookupService = starSystemLookupService ?? new EdsmStarSystemLookupService();
            _enrichmentService = new RouteRowEnrichmentService(_starSystemLookupService);
            _getOriginSystemName = getOriginSystemName ?? (() => null);
            _getJournalDirectory = getJournalDirectory ?? (() => new AppConfigStore().JournalDirectory);
            _isCaptainAssigned = isCaptainAssigned ?? (() => true);
            _getCarrierSystemName = getCarrierSystemName ?? (() => null);
            Rows = new ObservableCollection<RouteRowViewModel>();

            // Debounced live refresh: a live FSDTarget/NavRoute.json seed (Ship mode) or a
            // Captain's own ship FSDTarget (Fleet Carrier mode - see CarrierRouteJournalWatcher)
            // can resolve a system's Distance/Star Type *after* this table's own last Save/restore
            // already rendered that row blank - without this, the newly-known data would only ever
            // show up at the next Save/restore (e.g. an app restart), not live. DataSeeded may fire
            // from a background thread (journal watchers tail files off the UI thread), so it's
            // marshalled onto this DispatcherTimer's own captured dispatcher (the thread that
            // constructed this ViewModel - the UI thread in production) before touching it, rather
            // than Application.Current.Dispatcher - which would be null in a headless test host
            // that never starts a real WPF Application. The timer itself debounces the slower
            // EDSM-lookup catch-all: one NavRoute.json read seeds many systems in a tight loop, and
            // a full RefreshEnrichment() per seed would be wasteful - Stop+Start on every
            // DataSeeded restarts the countdown, so a burst collapses into one full pass shortly
            // after it quiets down, not one per system.
            _dataSeededDebounceTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _dataSeededDebounceTimer.Tick += (_, _) =>
            {
                _dataSeededDebounceTimer.Stop();
                RefreshEnrichment();
            };
            _starSystemLookupService.DataSeeded += (_, _) => _dataSeededDebounceTimer.Dispatcher.BeginInvoke(() =>
            {
                // Applied immediately, ahead of and independent from the debounce below: any
                // row whose Distance/Star Type is already resolvable purely from cache updates
                // right now, without waiting on the debounce or on PopulateAsync's own sequential
                // row-order sweep. This is what lets the one row a CMDR plotted a whole in-game
                // route just to look up (SPEC §4.9 - that system is, by construction, the *last*
                // one seeded in the burst) update the instant its NavRoute.json entry is cached,
                // rather than waiting out every unrelated row above it in the table first. The
                // debounced RefreshEnrichment below still owns the remaining, genuinely uncached
                // lookups.
                _enrichmentService.ApplyCachedValues(Rows, _getOriginSystemName());

                _dataSeededDebounceTimer.Stop();
                _dataSeededDebounceTimer.Start();
            });

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
            AutoPilotCommand = new RelayCommand(ToggleAutoPilot, () => ShowAutoPilotButton && IsSaved && Rows.Count > 0 && _canEngageAutoPilot());
            OpenCompanionSiteCommand = new RelayCommand(() =>
            {
                if (CompanionUrl is { } url)
                {
                    BrowserLauncher.Open(url.ToString());
                }
            });
            CopySystemCommand = new RelayCommand<RouteRowViewModel>(CopySystemToClipboard);
            SetNextSystemCommand = new RelayCommand<RouteRowViewModel>(SetNextSystem);
            DismissUnresolvedSystemsBannerCommand = new RelayCommand(DismissUnresolvedSystemsBanner);

            // Purely cosmetic (§4.4's Status-column countdown progress bar) - never mutates
            // Icon/Status itself, just recomputes each row's already-cheap Progress from
            // whatever PhaseEndUtc RouteSequencer last set, so it stays outside CLAUDE.md's
            // event-driven rule for Sequencing/. At most one row ever has a live countdown at a
            // time, so ticking every row on each interval is negligible.
            _progressTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _progressTimer.Tick += (_, _) =>
            {
                foreach (var row in Rows)
                {
                    row.RefreshProgress();
                }
            };
            _progressTimer.Start();
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
                    OnPropertyChanged(nameof(CanEdit));
                }
            }
        }

        /// <summary>
        /// How the currently-saved route was produced - Plain unless ImportFromSpansh was just
        /// called with a Neutron/Galaxy RouteType, and reset back to Plain by every Save() (see
        /// Save's own doc comment for why that's the single choke point for this). Drives
        /// IsNeutronRoute/IsGalaxyRoute (the Route table's own conditional extra columns),
        /// RouteView's own Edit-confirmation dialog, and (via RouteTypeChanged) MainViewModel
        /// forcing Ship mode and disabling the Fleet Carrier chip for as long as this stays
        /// non-Plain.
        /// </summary>
        public RouteType RouteType
        {
            get => _routeType;
            private set
            {
                if (SetProperty(ref _routeType, value))
                {
                    OnPropertyChanged(nameof(IsNeutronRoute));
                    OnPropertyChanged(nameof(IsGalaxyRoute));
                    RouteTypeChanged?.Invoke(this, value);
                }
            }
        }

        /// <summary>
        /// Raised whenever RouteType changes (including via Save()'s own always-reset-to-Plain
        /// behavior) - lets MainViewModel force Ship mode and disable the Fleet Carrier chip
        /// while a Neutron/Galaxy route is saved, without RouteViewModel needing a reference to
        /// the mode toggle itself (same decoupling principle as RouteSaved/AutoPilotRunningChanged).
        /// </summary>
        public event EventHandler<RouteType>? RouteTypeChanged;

        /// <summary>Drives the Route table's own Neutron-only "Jumps" column visibility.</summary>
        public bool IsNeutronRoute => RouteType == RouteType.Neutron;

        /// <summary>Drives the Route table's own Galaxy-only "Refuel"/"Inject"/"Neutron" column visibility.</summary>
        public bool IsGalaxyRoute => RouteType == RouteType.Galaxy;

        /// <summary>
        /// Mirrors EditCommand's own CanExecute (IsSaved && !IsAutoPilotRunning) as a plain
        /// bindable property - RouteView's own Edit button binds IsEnabled to this directly
        /// (rather than Command, which it can no longer use - see RouteView.xaml.cs.OnEditClick's
        /// own doc comment for why a confirming Click handler and a bound Command can't coexist).
        /// EditCommand itself is unchanged and still used by tests exercising it directly.
        /// </summary>
        public bool CanEdit => IsSaved && !IsAutoPilotRunning;

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
        /// True once a completed enrichment pass (RunEnrichmentAsync) finds at least one row
        /// showing "Plot needed"/"Target needed" (RouteRowViewModel.IsDistancePlaceholder/
        /// IsStarTypePlaceholder) - i.e. EDSM has confirmed it can't resolve that row's own
        /// system. Drives ShowUnresolvedSystemsBanner below; not itself shown directly.
        /// </summary>
        public bool HasUnresolvedSystems
        {
            get => _hasUnresolvedSystems;
            private set
            {
                if (SetProperty(ref _hasUnresolvedSystems, value))
                {
                    OnPropertyChanged(nameof(ShowUnresolvedSystemsBanner));
                }
            }
        }

        /// <summary>
        /// Drives the Route tab's own dismissible advisory banner (above the table) telling the
        /// CMDR some systems' Distance/Star Type couldn't be resolved - shown once a completed
        /// enrichment pass confirms a genuine gap (HasUnresolvedSystems), until Dismiss is
        /// clicked (DismissUnresolvedSystemsBannerCommand) or a fresh Save resets both flags.
        /// Deliberately separate from the per-row "Plot needed"/"Target needed" placeholders
        /// themselves (§4.9), which stay showing regardless - this is just a one-time nudge
        /// pointing at them, not a replacement for them.
        /// </summary>
        public bool ShowUnresolvedSystemsBanner => HasUnresolvedSystems && !_unresolvedSystemsBannerDismissed;

        /// <summary>Closes the unresolved-systems banner (above) without affecting the underlying per-row placeholders - reappears only after a fresh Save re-confirms the same (or a new) gap.</summary>
        public RelayCommand DismissUnresolvedSystemsBannerCommand { get; }

        private void DismissUnresolvedSystemsBanner()
        {
            _unresolvedSystemsBannerDismissed = true;
            OnPropertyChanged(nameof(ShowUnresolvedSystemsBanner));
        }

        /// <summary>
        /// Drives the Auto Pilot button's label ("Auto Pilot" when false, "Stop" when true) and
        /// disables Edit while engaged. While true, AutoPilotController (via MainViewModel
        /// wiring AutoPilotRunningChanged) plots each row's jump in turn using the Captain's
        /// selected macro (Roles tab §5.5), immediately if no Cooldown is active or after it
        /// clears plus the configured delay (Controls tab §6.1) otherwise, until the route
        /// completes or this is toggled off again.
        /// </summary>
        public bool IsAutoPilotRunning
        {
            get => _isAutoPilotRunning;
            private set
            {
                if (SetProperty(ref _isAutoPilotRunning, value))
                {
                    EditCommand.RaiseCanExecuteChanged();
                    OnPropertyChanged(nameof(CanEdit));
                    AutoPilotRunningChanged?.Invoke(this, value);
                }
            }
        }

        /// <summary>
        /// Raised whenever IsAutoPilotRunning changes - lets MainViewModel start/stop
        /// AutoPilotController without RouteViewModel needing a reference to it directly (same
        /// decoupling principle as RouteSaved/AutoPilotEligibilityChanged).
        /// </summary>
        public event EventHandler<bool>? AutoPilotRunningChanged;

        private Uri? _companionUrl;
        private BitmapImage? _companionQrImage;
        private bool _isMacroExecuting;
        private bool _isCompanionPopupOpen;

        /// <summary>The companion site link for the run currently in progress (SPEC §13) - null whenever there is none (Auto Pilot idle, or the session failed to start).</summary>
        public Uri? CompanionUrl
        {
            get => _companionUrl;
            private set
            {
                if (SetProperty(ref _companionUrl, value))
                {
                    OnPropertyChanged(nameof(HasCompanionSession));
                    OnCompanionButtonVisibilityChanged();
                }
            }
        }

        public BitmapImage? CompanionQrImage
        {
            get => _companionQrImage;
            private set => SetProperty(ref _companionQrImage, value);
        }

        /// <summary>True whenever a session exists at all - not itself bound to anything in the view; see ShowCompanionButton for the button's actual visibility.</summary>
        public bool HasCompanionSession => CompanionUrl != null;

        /// <summary>
        /// True while a macro is actively executing - Auto Pilot's own Captain plot/Engineer
        /// refuel, or a manual Play/Step started from the Controls tab, since all three run
        /// through the same single ControlsViewModel.PlayMacro/MacroPlayer channel (§4.7). Set
        /// externally via SetMacroExecuting - RouteViewModel has no reference to ControlsViewModel
        /// itself, the same decoupling principle as AutoPilotRunningChanged/SetCompanionSession.
        /// </summary>
        public bool IsMacroExecuting
        {
            get => _isMacroExecuting;
            private set
            {
                if (SetProperty(ref _isMacroExecuting, value))
                {
                    OnCompanionButtonVisibilityChanged();
                }
            }
        }

        internal void SetMacroExecuting(bool executing) => IsMacroExecuting = executing;

        /// <summary>
        /// Drives the QR/link button's own visibility on the Route tab - shown only while a
        /// companion session is actually live AND no macro is currently executing. Hidden (not
        /// merely disabled) for the duration of a macro run because clicking "Open in browser"
        /// mid-script risks stealing focus from the target Elite Dangerous window, which
        /// MacroPlayer treats as a playback failure (§4.7's panic mode).
        /// </summary>
        public bool ShowCompanionButton => HasCompanionSession && !IsMacroExecuting;

        /// <summary>
        /// Backs the QR popup's own open/closed state - a VM-owned property (bound to both the
        /// toggle button's IsChecked and the Popup's own IsOpen in RouteView.xaml) rather than a
        /// raw ToggleButton.IsChecked/Popup.IsOpen ElementName binding, so it can be forced closed
        /// the instant ShowCompanionButton itself goes false (a macro starting to execute, or the
        /// session ending) instead of leaving an orphaned popup open behind its own now-hidden
        /// anchor button.
        /// </summary>
        public bool IsCompanionPopupOpen
        {
            get => _isCompanionPopupOpen;
            set => SetProperty(ref _isCompanionPopupOpen, value && ShowCompanionButton);
        }

        private void OnCompanionButtonVisibilityChanged()
        {
            OnPropertyChanged(nameof(ShowCompanionButton));
            if (!ShowCompanionButton)
            {
                IsCompanionPopupOpen = false;
            }
        }

        /// <summary>
        /// Called externally (via MainViewModel, once CompanionSessionPublisher.StartSessionAsync
        /// resolves, and again with both arguments null once Auto Pilot stops) to push the
        /// companion site's QR/link state into this tab's own bindable properties - RouteViewModel
        /// has no reference to CompanionSessionPublisher itself, the same decoupling principle as
        /// AutoPilotRunningChanged/RaiseAutoPilotEligibilityChanged above.
        /// </summary>
        internal void SetCompanionSession(Uri? url, BitmapImage? qrImage)
        {
            CompanionUrl = url;
            CompanionQrImage = qrImage;
        }

        public RelayCommand SaveCommand { get; }

        public RelayCommand CancelCommand { get; }

        /// <summary>Returns to the text box (with its contents unchanged) to revise the route.</summary>
        public RelayCommand EditCommand { get; }

        /// <summary>Toggles IsAutoPilotRunning, flipping the button's label between "Auto Pilot" and "Stop".</summary>
        public RelayCommand AutoPilotCommand { get; }

        /// <summary>Opens the companion site link (CompanionUrl) in the default browser - clicking the QR code itself in its popup, a quicker way to open the page locally than scanning it, e.g. while testing against a local `ng serve` instance (CompanionSiteBaseUrl, §13).</summary>
        public RelayCommand OpenCompanionSiteCommand { get; }

        /// <summary>Called (via MainViewModel wiring RolesViewModel.AutoPilotEligibilityChanged) whenever role/macro assignment on the Roles tab changes, so this button's enabled state stays in sync without waiting for an incidental UI event to re-query it.</summary>
        public void RaiseAutoPilotEligibilityChanged() => AutoPilotCommand.RaiseCanExecuteChanged();

        /// <summary>
        /// Stops Auto Pilot the same way clicking it again while running would - called
        /// externally (via MainViewModel) when AutoPilotController detects the route has run to
        /// completion, or when Roles-tab eligibility (Captain/macro assignment) drops out from
        /// under a run already in progress. A no-op if not currently running.
        /// </summary>
        public void StopAutoPilot() => IsAutoPilotRunning = false;

        /// <summary>
        /// Whether the Route tab's Auto Pilot button is shown at all - false in Ship mode, where
        /// there's no macro automation (the CMDR flies and plots every jump manually). Also
        /// folded into AutoPilotCommand's own CanExecute as defense-in-depth, so a stale
        /// reference to the command can't be invoked while it's hidden either.
        /// </summary>
        public bool ShowAutoPilotButton
        {
            get => _showAutoPilotButton;
            private set
            {
                if (SetProperty(ref _showAutoPilotButton, value))
                {
                    AutoPilotCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// Whether the Route tab's "Trim to jump range" button is shown at all - Fleet Carrier
        /// mode only, the same as ShowAutoPilotButton, since trimming to a 500ly max hop is a
        /// fleet-carrier-specific planning aid with no Ship-mode equivalent (a solo ship's own
        /// jump range varies by build/fuel, not a fixed 500ly).
        /// </summary>
        public bool ShowTrimButton
        {
            get => _showTrimButton;
            private set => SetProperty(ref _showTrimButton, value);
        }

        /// <summary>
        /// Called by MainViewModel whenever TrackingMode changes. Entering Ship mode hides the
        /// Auto Pilot button and forcibly stops any run already in progress - hiding the button
        /// alone would leave a macro silently still playing in the background, which would
        /// directly violate Ship mode's "no automation at all" premise. Leaving Ship mode simply
        /// shows the button again; nothing was left running to resume.
        /// </summary>
        public void SetShipMode(bool isShipMode)
        {
            ShowAutoPilotButton = !isShipMode;
            ShowTrimButton = !isShipMode;
            if (isShipMode)
            {
                StopAutoPilot();
            }
        }

        /// <summary>
        /// Imports whatever route is currently plotted in-game (NavRoute.json, read straight out
        /// of the configured journal folder - <see cref="_getJournalDirectory"/> - unconditionally,
        /// with no Captain/tracked instance needing to be assigned first), replacing the currently
        /// saved route with it, exactly as if the CMDR had pasted the same system list by hand and
        /// clicked Save. Applies immediately (calling Save() itself) rather than leaving the result
        /// in Edit state for review - the caller (RouteView's own confirmation dialog) is where the
        /// "are you sure" step belongs, since this itself is a destructive, all-or-nothing
        /// replacement of the current route with no partial/preview state in between. Deliberately
        /// unconditional: NavRoute.json is a per-installation file, not tied to any one running
        /// instance (the same "one physical file regardless of how many instances are running"
        /// caveat SPEC §5.2 notes for Status.json/Cargo.json), so gating this on a specific
        /// assignment would both require one unnecessarily and could still name a route belonging
        /// to some other instance entirely - the CMDR is left to judge for themselves whether the
        /// imported route is the one they meant. Every entry's own coordinates/star type are also
        /// seeded into the shared IStarSystemLookupService cache along the way ("as we go") - not
        /// because Save strictly needs it (EDSM would resolve the same values anyway), but because
        /// NavRoute.json already hands over exact values for free, including any
        /// procedurally-generated system EDSM has no record of at all, so Save's own Distance/Star
        /// Type enrichment resolves instantly from cache instead of re-fetching what's already
        /// known.
        ///
        /// NavRoute.json's own "Route" array always starts with wherever the CMDR was standing
        /// when the route was plotted (their departure system) - see
        /// StarSystemCacheSeeder.NavRouteEntry's own doc comment - so that first entry is deliberately
        /// skipped: the pasted route describes systems to travel *to*, the same as if the CMDR had
        /// typed it by hand, and typing one's own current system as the route's first line would
        /// never occur to a CMDR doing this manually either.
        /// </summary>
        public NavRouteImportOutcome ImportFromNavRoute()
        {
            var entries = StarSystemCacheSeeder.ReadEntriesFromDirectory(_getJournalDirectory());
            if (entries is null || entries.Count < 2)
            {
                Log.Warn("Route", "Import Current Route skipped - no route is currently plotted in-game (or NavRoute.json couldn't be read).");
                return NavRouteImportOutcome.NoRoutePlotted;
            }

            foreach (var entry in entries)
            {
                if (entry.Coordinates is { } coordinates)
                {
                    _starSystemLookupService.SeedCoordinates(entry.SystemName, coordinates);
                }

                if (entry.StarType is { } starType)
                {
                    _starSystemLookupService.SeedStarType(entry.SystemName, starType);
                }

                if (entry.SystemAddress is { } systemAddress)
                {
                    _starSystemLookupService.SeedSystemAddress(entry.SystemName, systemAddress);
                }
            }

            RouteText = string.Join("\n", entries.Skip(1).Select(e => e.SystemName));
            Save();
            Log.Info("Route", $"Imported {entries.Count - 1} system(s) from NavRoute.json.");
            return NavRouteImportOutcome.Success;
        }

        /// <summary>
        /// Applies a route freshly calculated by Spansh's fleet-carrier route planner (the Spansh
        /// menu, SpanshImportViewModel) - replaces the currently-saved route with every jump
        /// Spansh returned, unconditionally including the source system as row 1 (unlike
        /// ImportFromNavRoute, which skips NavRoute.json's own leading departure entry - a Spansh
        /// route is a deliberately hand-picked source/destination pair, not "wherever the CMDR
        /// happened to be standing", so the source is itself a real, intended waypoint here).
        /// Applies immediately (calling Save() itself), the same "no Edit-state review step" shape
        /// ImportFromNavRoute uses. Every jump's own coordinates and system address (id64) are
        /// seeded into the shared IStarSystemLookupService cache along the way, the same "as we go"
        /// caching ImportFromNavRoute already does for NavRoute.json. Returns false (route left
        /// untouched) if Spansh returned no jumps at all.
        ///
        /// <paramref name="routeType"/> tags the freshly-saved route (default Plain, for the
        /// Fleet Carrier tab's own jumps, which never populate the Neutron/Galaxy-only fields
        /// below) - when it's Neutron or Galaxy, each jump's own Jumps/MustRefuel/MustInject/
        /// HasNeutron is applied to the matching row (Save() just rebuilt Rows from this exact
        /// jump list, in the same order, so a plain zip is safe) and persisted alongside RouteType
        /// itself, ready to restore on the next launch (RestoreFromSettings). Save() itself always
        /// resets RouteType back to Plain first - see its own doc comment - so this only takes
        /// effect because it runs immediately afterward, before anything else can call Save()
        /// again.
        /// </summary>
        public bool ImportFromSpansh(IReadOnlyList<SpanshRouteJump> jumps, RouteType routeType = RouteType.Plain)
        {
            if (jumps.Count == 0)
            {
                Log.Warn("Route", "Spansh route import skipped - no jumps returned.");
                return false;
            }

            foreach (var jump in jumps)
            {
                _starSystemLookupService.SeedCoordinates(jump.Name, jump.Coordinates);
                _starSystemLookupService.SeedSystemAddress(jump.Name, jump.Id64);
            }

            RouteText = string.Join("\n", jumps.Select(j => j.Name));
            Save();

            if (routeType != RouteType.Plain)
            {
                var metadata = jumps
                    .Select(j => new RouteRowMetadata(j.Jumps, j.MustRefuel, j.MustInject, j.HasNeutron))
                    .ToList();
                ApplyRouteTypeAndMetadata(routeType, metadata);
                _settings.SetString(RouteTypeSettingKey, routeType.ToString());
                _settings.SetString(RouteMetadataSettingKey, JsonSerializer.Serialize(metadata));
            }

            Log.Info("Route", $"Imported {jumps.Count} system(s) from Spansh.");
            return true;
        }

        /// <summary>Per-row Neutron/Galaxy Plotter-only data (RouteRowViewModel.Jumps/MustRefuel/MustInject/HasNeutron) - the JSON shape persisted under RouteMetadataSettingKey, one entry per row by index. Shares field names/nullability with SpanshRouteJump's own trailing fields on purpose, so ImportFromSpansh's own projection is a direct 1:1 mapping.</summary>
        private sealed record RouteRowMetadata(int? Jumps, bool? MustRefuel, bool? MustInject, bool? HasNeutron);

        /// <summary>
        /// Applies <paramref name="type"/>/<paramref name="metadata"/> onto the current Rows -
        /// shared by ImportFromSpansh and RestoreFromSettings, both of which must also re-persist
        /// RouteTypeSettingKey/RouteMetadataSettingKey themselves immediately afterward (this
        /// method only ever touches in-memory state) - the Save() both callers run first already
        /// overwrote both settings keys back to Plain/empty (its own unconditional reset), so
        /// skipping that re-persist step - a bug fixed here - left the on-disk value silently
        /// wrong (Plain) from that point on, even though the in-memory RouteType was correctly
        /// Neutron/Galaxy for the rest of the session: invisible until the *next* restart, when
        /// RestoreFromSettings would read back the stale "Plain" and never re-apply anything.
        /// Zips by index; a mismatched
        /// count (metadata persisted against a route no longer matching in row count - shouldn't
        /// normally happen, since Save() and this are always called back-to-back against the same
        /// data, but defensive against a hand-edited/corrupted settings row) applies only as many
        /// entries as both sides actually have, rather than throwing.
        /// </summary>
        private void ApplyRouteTypeAndMetadata(RouteType type, IReadOnlyList<RouteRowMetadata> metadata)
        {
            RouteType = type;

            var count = Math.Min(Rows.Count, metadata.Count);
            for (var i = 0; i < count; i++)
            {
                Rows[i].Jumps = metadata[i].Jumps;
                Rows[i].MustRefuel = metadata[i].MustRefuel;
                Rows[i].MustInject = metadata[i].MustInject;
                Rows[i].HasNeutron = metadata[i].HasNeutron;
            }
        }

        /// <summary>
        /// Collapses the currently-saved route's rows down to a series of hops no longer than
        /// MaxCarrierJumpLightYears, dropping whichever intermediate rows aren't needed to stay
        /// within that reach - a planning aid for a route pasted (or imported, see
        /// ImportFromNavRoute) with many closely-spaced waypoints (e.g. a neutron-highway plotter's
        /// own output), collapsed down to only the systems a fleet carrier actually needs to jump
        /// via. Requires every row's own coordinates already resolved (SPEC §4.9's Distance
        /// column) - a route with any still-unresolved/confirmed-unavailable row can't be trimmed
        /// reliably, since a leg distance involving it isn't known.
        ///
        /// Also requires a Captain currently assigned (Roles tab) with their carrier's own current
        /// location known - the pasted route's own row 1 is only ever where the CMDR *started*
        /// planning the route, not necessarily where the carrier genuinely is right now (it may
        /// already be mid-route, or have detoured). Trimming from the wrong assumed starting point
        /// would make the very first hop it plots inefficient - possibly a short hop when a much
        /// longer one straight from the carrier's real position was available. The carrier's real
        /// location is therefore added as the route's own new first entry (unless it's already the
        /// same system as row 1, in which case nothing is added - there's nothing for a same-system
        /// entry to anchor differently) and the greedy walk below is anchored from there instead.
        ///
        /// Greedy "farthest reachable waypoint" simplification, the standard shape for this kind
        /// of route-thinning: starting from the carrier's own real location (always kept), repeatedly
        /// jumps to the farthest-along row still within range in a straight line, never skipping
        /// past a genuine &gt;MaxCarrierJumpLightYears gap (that row is kept too - there's no way to
        /// skip it regardless). The route's last row is always kept as well, as a natural consequence
        /// of the loop always advancing until it reaches the end. Unlike ImportFromNavRoute, this
        /// applies immediately (calling Save() itself) rather than leaving the result in Edit state
        /// for review - the trim is a deterministic, purely mechanical distance calculation with no
        /// judgment call for the CMDR to make, so there's nothing a manual confirmation step would
        /// actually protect against.
        /// </summary>
        public RouteTrimResult TrimToJumpRange()
        {
            if (Rows.Count == 0)
            {
                Log.Warn("Route", "Trim for FC skipped - no saved route to trim.");
                return new RouteTrimResult(RouteTrimOutcome.NoRoute);
            }

            if (!_isCaptainAssigned())
            {
                Log.Warn("Route", "Trim for FC skipped - no Captain assigned; the carrier's real current location is needed to anchor the first hop.");
                return new RouteTrimResult(RouteTrimOutcome.CaptainNotAssigned);
            }

            var carrierSystem = _getCarrierSystemName();
            if (string.IsNullOrWhiteSpace(carrierSystem))
            {
                Log.Warn("Route", "Trim for FC skipped - the Captain's carrier's current location isn't known yet; open Carrier Management in-game to establish it.");
                return new RouteTrimResult(RouteTrimOutcome.CarrierLocationUnknown);
            }

            // Skip prepending a duplicate leading entry when the carrier is already sitting at
            // row 1's own system - there's nothing for a same-system anchor to change.
            var prependCarrierLocation = !string.Equals(carrierSystem, Rows[0].SystemText, StringComparison.OrdinalIgnoreCase);
            var systemNames = new List<string>(Rows.Count + (prependCarrierLocation ? 1 : 0));
            if (prependCarrierLocation)
            {
                systemNames.Add(carrierSystem);
            }
            systemNames.AddRange(Rows.Select(r => r.SystemText));

            var coordinates = new List<GalacticCoordinates>(systemNames.Count);
            foreach (var systemName in systemNames)
            {
                if (!_starSystemLookupService.TryGetCachedCoordinates(systemName, out var rowCoords) || rowCoords is not { } resolved)
                {
                    Log.Warn("Route", "Trim for FC skipped - not every row's coordinates are known yet.");
                    return new RouteTrimResult(RouteTrimOutcome.CoordinatesUnavailable);
                }

                coordinates.Add(resolved);
            }

            var keptIndexes = new List<int> { 0 };
            var currentIndex = 0;
            while (currentIndex < systemNames.Count - 1)
            {
                var farthestIndex = currentIndex + 1;
                for (var candidate = currentIndex + 1; candidate < systemNames.Count; candidate++)
                {
                    if (coordinates[currentIndex].DistanceTo(coordinates[candidate]) <= MaxCarrierJumpLightYears)
                    {
                        farthestIndex = candidate;
                    }
                }

                keptIndexes.Add(farthestIndex);
                currentIndex = farthestIndex;
            }

            var removedCount = systemNames.Count - keptIndexes.Count;
            if (removedCount == 0 && !prependCarrierLocation)
            {
                return new RouteTrimResult(RouteTrimOutcome.Success);
            }

            RouteText = string.Join("\n", keptIndexes.Select(i => systemNames[i]));
            Save();
            Log.Info("Route", $"Trimmed route to {keptIndexes.Count} row(s) (removed {removedCount}), max {MaxCarrierJumpLightYears:0} ly/hop.");
            return new RouteTrimResult(RouteTrimOutcome.Success, removedCount);
        }

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
            _unresolvedSystemsBannerDismissed = false;
            HasUnresolvedSystems = false;

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
            _lastSavedRouteText = RouteText;
            _settings.SetString(RouteTextSettingKey, RouteText);

            // Every Save (manual, Import Current Route, Trim for FC, or a restore) reverts the
            // route to Plain by default - only ImportFromSpansh re-tags it (Neutron/Galaxy) and
            // re-persists these two keys immediately afterward, once this call returns. This is
            // deliberately the single choke point for that reset rather than special-casing every
            // other Save() caller individually - see RouteType's own doc comment.
            RouteType = RouteType.Plain;
            _settings.SetString(RouteTypeSettingKey, RouteType.Plain.ToString());
            _settings.SetString(RouteMetadataSettingKey, string.Empty);

            Log.Info("Route", $"Route saved - {Rows.Count} row(s).");
            RouteSaved?.Invoke(this, EventArgs.Empty);

            TriggerEnrichment();
        }

        /// <summary>
        /// (Re)starts populating every row's Distance/Star Type (RouteRowEnrichmentService)
        /// against a fresh snapshot of the current Rows/origin - called at the end of every Save
        /// (including RestoreFromSettings' own re-invocation of it), and once more by
        /// RefreshEnrichment below. Cancels any still-running previous population first, so an
        /// Edit-&gt;Save cycle (or a fast double-Save) cleanly abandons a now-superseded lookup
        /// rather than racing to mutate rows a newer Save already replaced. This is a one-time,
        /// best-effort background calculation, never wired into RouteSequencer/the event-driven
        /// Sequencing/ engine (CLAUDE.md) - Distance/Star Type describe the route's static
        /// topology, not tracked progress, and never need live recomputation once resolved.
        /// </summary>
        private void TriggerEnrichment()
        {
            _enrichmentCts?.Cancel();
            var cts = new CancellationTokenSource();
            _enrichmentCts = cts;

            var rowsSnapshot = Rows.ToList();
            var origin = _getOriginSystemName();
            _ = RunEnrichmentAsync(rowsSnapshot, origin, cts.Token);
        }

        private async Task RunEnrichmentAsync(List<RouteRowViewModel> rows, string? origin, CancellationToken cancellationToken)
        {
            try
            {
                await _enrichmentService.PopulateAsync(rows, origin, cancellationToken);

                // "Post processing" is only genuinely complete once this pass ran to its own
                // end uncancelled - a still-running/superseded pass has nothing conclusive to
                // report yet, so the banner (and its underlying flag) is left exactly as it was
                // rather than flickering based on a partial result.
                HasUnresolvedSystems = rows.Any(row => row.IsDistancePlaceholder || row.IsStarTypePlaceholder);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a later Save/RefreshEnrichment - that newer run already owns
                // populating the current Rows, so there's nothing left to do here.
            }
        }

        /// <summary>
        /// Re-runs Distance/Star Type population for the currently-saved route without rebuilding
        /// Rows itself - called by MainViewModel once RolesViewModel's/TrackViewModel's own
        /// startup instance scan finishes, since on a normal app relaunch RestoreFromSettings'
        /// own Save() runs (and so captures its origin) before that scan has resolved a restored
        /// Captain/tracked instance, leaving row 1's Distance blank even though one is about to be
        /// restored moments later. Re-fetching here is cheap regardless, since
        /// EdsmStarSystemLookupService's cache will almost always already have everything from
        /// that first pass. A no-op-ish call (nothing to populate) if no route is saved yet.
        /// </summary>
        public void RefreshEnrichment() => TriggerEnrichment();

        /// <summary>
        /// Undoes whatever's been typed since Edit was last entered: if a route has been saved
        /// before, restores the text box to that last-saved text and returns to Table state -
        /// Rows itself was never touched by Edit (only Save rebuilds it), so this leaves the
        /// table, including any in-progress icon/status/journal-tracked progress, exactly as it
        /// was. If nothing has ever been saved yet (a fresh, never-saved launch), there's no
        /// table to go back to, so this just clears the box instead, staying in Edit state -
        /// SaveCommand's own CanExecute (non-whitespace text required) keeps it disabled either
        /// way until something new is typed.
        /// </summary>
        private void Cancel()
        {
            if (_lastSavedRouteText is { } lastSaved)
            {
                RouteText = lastSaved;
                IsSaved = true;
            }
            else
            {
                RouteText = string.Empty;
            }
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

            // Read RouteType/metadata *before* calling Save() below - Save() itself
            // unconditionally overwrites both of these same settings keys back to Plain/empty
            // (its own always-clear behavior), so reading them afterward would only ever see
            // what Save() itself just wrote, never what an earlier session's ImportFromSpansh
            // actually persisted.
            var savedTypeText = _settings.GetString(RouteTypeSettingKey);
            var savedMetadataJson = _settings.GetString(RouteMetadataSettingKey);

            RouteText = savedRouteText;
            Save();

            // Re-apply whatever was actually persisted, the same "read back what ImportFromSpansh
            // wrote" shape, so a Neutron/Galaxy route's extra columns survive an app restart too.
            // A missing/unparsable/empty type or metadata row (never persisted at all, or the
            // JSON is somehow corrupt) leaves the route as the Plain default Save() already set,
            // rather than throwing.
            if (savedTypeText is { } typeText
                && Enum.TryParse<RouteType>(typeText, out var savedType)
                && savedType != RouteType.Plain
                && !string.IsNullOrWhiteSpace(savedMetadataJson))
            {
                try
                {
                    var metadata = JsonSerializer.Deserialize<List<RouteRowMetadata>>(savedMetadataJson, RouteMetadataJsonOptions);
                    if (metadata != null)
                    {
                        ApplyRouteTypeAndMetadata(savedType, metadata);

                        // Save() above already overwrote these same two keys back to Plain/empty -
                        // without re-writing them here, the on-disk state silently disagrees with
                        // what's now shown for the rest of this session, surfacing only on the
                        // *next* restart (the bug this fixes - see ApplyRouteTypeAndMetadata's own
                        // doc comment). savedMetadataJson is re-used verbatim rather than
                        // re-serializing metadata, since it's already the exact JSON that round-
                        // tripped through Deserialize just above.
                        _settings.SetString(RouteTypeSettingKey, savedType.ToString());
                        _settings.SetString(RouteMetadataSettingKey, savedMetadataJson);
                    }
                }
                catch (JsonException ex)
                {
                    Log.Warn("Route", "Could not restore the saved route's Neutron/Galaxy Plotter data - it will show as a plain route instead.", ex);
                }
            }
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
        /// arrived at - matched by name, skipping any row already Complete, since this fires
        /// ahead of the delayed Arrived transition and so can't rely on the *current* visit's row
        /// already showing Complete - but a repeated system name earlier in the route from a
        /// genuinely earlier, already-finished visit must not be matched instead, or every later
        /// revisit would keep copying whatever followed that first visit) and, if a next row
        /// exists, copies *that* row's System text - no confirmation sound, unlike the manual
        /// click-to-copy, since this fires unattended.
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
                if (Rows[i].Icon == RowIcon.Complete)
                {
                    continue;
                }

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
            Log.Info("Route", $"Auto-copied \"{nextRow.SystemText}\" to clipboard (arrived at \"{e.SystemName}\").");
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

            Log.Info("Route", $"Set next system - row {targetIndex + 1} (\"{targetRow.SystemText}\") is now current.");

            for (var i = 0; i < Rows.Count; i++)
            {
                var row = Rows[i];
                row.Status = string.Empty;
                row.PhaseEndUtc = null;
                row.Icon = i < targetIndex ? RowIcon.Complete
                    : i == targetIndex ? RowIcon.InProgress
                    : RowIcon.None;
            }
        }
    }
}
