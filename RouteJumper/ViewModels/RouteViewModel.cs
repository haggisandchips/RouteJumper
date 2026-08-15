using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
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

        /// <summary>Fleet carriers' own real-world maximum jump range - the threshold TrimToJumpRange collapses the route's intermediate rows against.</summary>
        public const double MaxCarrierJumpLightYears = 500.0;

        private readonly RouteSequencer _sequencer;
        private readonly AppSettingsStore _settings;
        private readonly Func<bool> _canEngageAutoPilot;
        private readonly IStarSystemLookupService _starSystemLookupService;
        private readonly RouteRowEnrichmentService _enrichmentService;
        private readonly Func<string?> _getOriginSystemName;
        private readonly Func<string> _getJournalDirectory;
        private CancellationTokenSource? _enrichmentCts;

        private string _routeText = string.Empty;
        private string? _lastSavedRouteText;
        private bool _isSaved;
        private bool _isAutoPilotRunning;
        private bool _showAutoPilotButton = true;
        private bool _showTrimButton = true;
        private bool _autoCopyToClipboardEnabled;
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
        /// </summary>
        public RouteViewModel(
            AppSettingsStore settings,
            IRowEventTrigger? rowEventTrigger = null,
            Func<bool>? canEngageAutoPilot = null,
            IStarSystemLookupService? starSystemLookupService = null,
            Func<string?>? getOriginSystemName = null,
            Func<string>? getJournalDirectory = null)
        {
            _settings = settings;
            _canEngageAutoPilot = canEngageAutoPilot ?? (() => true);
            _starSystemLookupService = starSystemLookupService ?? new EdsmStarSystemLookupService(settings);
            _enrichmentService = new RouteRowEnrichmentService(_starSystemLookupService);
            _getOriginSystemName = getOriginSystemName ?? (() => null);
            _getJournalDirectory = getJournalDirectory ?? (() => new AppConfigStore().JournalDirectory);
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
            CopySystemCommand = new RelayCommand<RouteRowViewModel>(CopySystemToClipboard);
            SetNextSystemCommand = new RelayCommand<RouteRowViewModel>(SetNextSystem);

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

        public RelayCommand SaveCommand { get; }

        public RelayCommand CancelCommand { get; }

        /// <summary>Returns to the text box (with its contents unchanged) to revise the route.</summary>
        public RelayCommand EditCommand { get; }

        /// <summary>Toggles IsAutoPilotRunning, flipping the button's label between "Auto Pilot" and "Stop".</summary>
        public RelayCommand AutoPilotCommand { get; }

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
            }

            RouteText = string.Join("\n", entries.Skip(1).Select(e => e.SystemName));
            Save();
            Log.Info("Route", $"Imported {entries.Count - 1} system(s) from NavRoute.json.");
            return NavRouteImportOutcome.Success;
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
        /// Greedy "farthest reachable waypoint" simplification, the standard shape for this kind
        /// of route-thinning: starting from row 1 (always kept), repeatedly jumps to the
        /// farthest-along row still within range in a straight line, never skipping past a genuine
        /// &gt;MaxCarrierJumpLightYears gap (that row is kept too - there's no way to skip it
        /// regardless). The route's last row is always kept as well, as a natural consequence of
        /// the loop always advancing until it reaches the end. Unlike ImportFromNavRoute, this
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

            var coordinates = new List<GalacticCoordinates>(Rows.Count);
            foreach (var row in Rows)
            {
                if (!_starSystemLookupService.TryGetCachedCoordinates(row.SystemText, out var rowCoords) || rowCoords is not { } resolved)
                {
                    Log.Warn("Route", "Trim for FC skipped - not every row's coordinates are known yet.");
                    return new RouteTrimResult(RouteTrimOutcome.CoordinatesUnavailable);
                }

                coordinates.Add(resolved);
            }

            var keptIndexes = new List<int> { 0 };
            var currentIndex = 0;
            while (currentIndex < Rows.Count - 1)
            {
                var farthestIndex = currentIndex + 1;
                for (var candidate = currentIndex + 1; candidate < Rows.Count; candidate++)
                {
                    if (coordinates[currentIndex].DistanceTo(coordinates[candidate]) <= MaxCarrierJumpLightYears)
                    {
                        farthestIndex = candidate;
                    }
                }

                keptIndexes.Add(farthestIndex);
                currentIndex = farthestIndex;
            }

            var removedCount = Rows.Count - keptIndexes.Count;
            if (removedCount == 0)
            {
                return new RouteTrimResult(RouteTrimOutcome.Success);
            }

            RouteText = string.Join("\n", keptIndexes.Select(i => Rows[i].SystemText));
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
