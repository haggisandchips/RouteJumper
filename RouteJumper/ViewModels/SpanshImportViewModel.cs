using System.Globalization;
using RouteJumper.Common;
using RouteJumper.Models;
using RouteJumper.Services;
using RouteJumper.Services.Logging;
using RouteJumper.Services.Spansh;

namespace RouteJumper.ViewModels
{
    /// <summary>
    /// ViewModel for the "Spansh" modal dialog (Spansh menu) - a "Fleet Carrier" tab:
    /// pick a Source and Destination system (SpanshSystemPickerViewModel), then Calculate
    /// requests a route from Spansh and polls it to completion; a "Neutron Plotter" tab (same
    /// picker, plus an editable Range/Efficiency) calculating a neutron-highway route instead;
    /// and a "Galaxy Plotter" tab calculating an exact route using the CMDR's own real ship build
    /// (re-read from that instance's journal in the background as soon as this ViewModel is
    /// constructed - see LoadGalaxyLoadoutAsync) rather than a flat range.
    /// All three apply their result to the Route tab via <see cref="_applyRoute"/>
    /// (RouteViewModel.ImportFromSpansh, in production - injected so this ViewModel has no direct
    /// reference to RouteViewModel, the same cross-tab decoupling principle MainViewModel already
    /// uses elsewhere).
    /// </summary>
    public class SpanshImportViewModel : ObservableObject
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

        /// <summary>Spansh's own hosted Fleet Carrier Router - unlike this dialog's Calculate (a single source-&gt;destination hop sequence), it accounts for tritium capacity and schedules restock stops along the way.</summary>
        private const string FleetCarrierRouterUrl = "https://spansh.co.uk/fleet-carrier";

        /// <summary>Spansh's own hosted Neutron Plotter - linked from the Neutron Plotter tab's own footnote for anything beyond this dialog's plain source-&gt;destination Calculate (via/waypoint planning, visualising the route, ...).</summary>
        private const string NeutronPlotterUrl = "https://spansh.co.uk/plotter";

        /// <summary>Spansh's own hosted Galaxy Plotter (its own "exact-plotter" page) - linked from the Galaxy Plotter tab's own footnote for how to follow the calculated route in-game, and detailed explanations of its parameters/routing algorithms, none of which this dialog's own compact form has room to cover.</summary>
        private const string GalaxyPlotterUrl = "https://spansh.co.uk/exact-plotter";

        /// <summary>Spansh's own default efficiency (Neutron Plotter tab) - a route-optimisation/speed trade-off, editable but pre-filled with this until changed.</summary>
        private const string DefaultNeutronEfficiency = "60";

        /// <summary>Spansh's own default route-planning algorithm (Galaxy Plotter tab).</summary>
        private const string DefaultGalaxyAlgorithm = "optimistic";

        private readonly ISpanshRouteService _routeService;
        private readonly Func<IReadOnlyList<SpanshRouteJump>, RouteType, bool> _applyRoute;

        private bool _isCalculating;
        private string _statusMessage = string.Empty;
        private CancellationTokenSource? _calculateCts;

        private bool _isNeutronCalculating;
        private string _neutronStatusMessage = string.Empty;
        private string _neutronRange = string.Empty;
        private string _neutronEfficiency = DefaultNeutronEfficiency;
        private bool _isOvercharge;
        private CancellationTokenSource? _neutronCalculateCts;

        private bool _isGalaxyCalculating;
        private string _galaxyStatusMessage = string.Empty;
        private string _galaxyCargo = "0";
        private string _galaxyReserveTankSize = "0";
        private bool _galaxyIsSupercharged;
        private bool _galaxyUseSupercharge = true;
        private bool _galaxyUseInjections;
        private bool _galaxyUseInjectionsWhenRequired;
        private bool _galaxyExcludeSecondary;
        private bool _galaxyRefuelEveryScoopable = true;
        private string _galaxyAlgorithm = DefaultGalaxyAlgorithm;
        private LoadoutSnapshot? _galaxyLoadout;
        private ShipBuildParameters? _galaxyParameters;
        private CancellationTokenSource? _galaxyCalculateCts;

        /// <summary>
        /// <paramref name="config"/> defaults to a real AppConfigStore, read once here (a fresh
        /// SpanshImportViewModel is created every time the dialog is opened, MainWindow.
        /// OnSpanshClick, so a hand-edited routejumper.conf takes effect on the next open, the same
        /// "re-read on the next relevant action" convention AppConfigStore's other settings follow)
        /// - overridable so tests can supply a directory-scoped instance instead.
        ///
        /// <paramref name="knownCurrentSystem"/> pre-fills the Neutron Plotter tab's own Source
        /// field from the CMDR's own ship's current system (the Captain's in Fleet Carrier mode,
        /// or the tracked instance's in Ship mode - MainViewModel.GetKnownShipState), when known -
        /// stays fully editable afterward, the same as every other pre-filled field elsewhere in
        /// the app (e.g. the Controls tab's own Test fields). Null/blank leaves the field blank
        /// for the CMDR to fill in. Range has no equivalent pre-fill - the journal's own
        /// MaxJumpRange reflects whatever fuel/cargo the ship happened to be carrying when last
        /// logged, which may not match whatever load-out the CMDR actually wants to plan the
        /// route around - so it's left for them to judge and type in rather than presumed (a
        /// smarter default is a future enhancement, not this one).
        /// <paramref name="knownCarrierSystem"/> pre-fills the Fleet Carrier tab's own Source from
        /// the Captain's own fleet carrier's real current location (MainViewModel.
        /// GetKnownShipState - Fleet Carrier mode only, always null in Ship mode), when known.
        /// Unlike <paramref name="knownCurrentSystem"/> above, this can't just be dropped in as an
        /// unconfirmed local value - the Fleet Carrier tab's own Calculate posts a Spansh-assigned
        /// id, not a name (see SpanshSystemPickerViewModel's own doc comment), so this instead
        /// kicks off a background search for the exact name and only applies it once (and if) a
        /// matching suggestion actually comes back - see PrefillFleetCarrierSourceAsync.
        /// <paramref name="defaultToOvercharge"/> similarly defaults the Neutron Plotter tab's own
        /// Normal/Overcharge supercharge choice - true only when that same ship's FrameShiftDrive
        /// slot is filled with an overcharged FSD booster (EliteInstanceViewModel.
        /// HasOverchargedFsd) - editable via the two radio buttons regardless.
        ///
        /// <paramref name="knownJournalFilePath"/> is what the Galaxy Plotter tab re-reads in the
        /// background (via <paramref name="readLoadoutSnapshot"/>) to derive that tab's own
        /// ship-specific request fields (Services\Spansh\ShipBuildDerivation) - the same instance
        /// <paramref name="knownCurrentSystem"/> already comes from. Null/blank leaves that tab
        /// showing an explanatory GalaxyStatusMessage with Calculate disabled (see
        /// LoadGalaxyLoadoutAsync) rather than silently doing nothing. Its own Source pre-fills
        /// from <paramref name="knownCurrentSystem"/> the same way the Neutron Plotter tab's does,
        /// but resolved to a real Spansh id first (PrefillGalaxySourceAsync) since this tab's own
        /// Calculate posts an id like the Fleet Carrier tab's does, not a bare name.
        /// <paramref name="knownCurrentCargo"/> pre-fills the Galaxy Plotter tab's own Cargo field.
        /// <paramref name="readLoadoutSnapshot"/> defaults to a real EliteInstanceScanner.
        /// ReadLoadoutSnapshot call on a background thread - overridable so tests can supply
        /// deterministic results without touching disk.
        /// </summary>
        public SpanshImportViewModel(
            ISpanshRouteService routeService,
            Func<IReadOnlyList<SpanshRouteJump>, RouteType, bool> applyRoute,
            AppConfigStore? config = null,
            string? knownCurrentSystem = null,
            string? knownCarrierSystem = null,
            bool defaultToOvercharge = false,
            string? knownJournalFilePath = null,
            int? knownCurrentCargo = null,
            Func<string, Task<LoadoutSnapshot?>>? readLoadoutSnapshot = null)
        {
            _routeService = routeService;
            _applyRoute = applyRoute;

            CalculateCommand = new AsyncRelayCommand(CalculateAsync, CanCalculate);
            OpenFleetCarrierRouterCommand = new RelayCommand(() => BrowserLauncher.Open(FleetCarrierRouterUrl));
            NeutronCalculateCommand = new AsyncRelayCommand(CalculateNeutronAsync, CanCalculateNeutron);
            OpenNeutronPlotterCommand = new RelayCommand(() => BrowserLauncher.Open(NeutronPlotterUrl));
            OpenGalaxyPlotterCommand = new RelayCommand(() => BrowserLauncher.Open(GalaxyPlotterUrl));
            GalaxyCalculateCommand = new AsyncRelayCommand(CalculateGalaxyAsync, CanCalculateGalaxy);

            var debounceDelay = TimeSpan.FromMilliseconds((config ?? new AppConfigStore()).SpanshAutocompleteDebounceMs);
            var cachedSearch = CreateCachingSearch(routeService.SearchSystemNamesAsync);
            Source = new SpanshSystemPickerViewModel(cachedSearch, debounceDelay);
            Destination = new SpanshSystemPickerViewModel(cachedSearch, debounceDelay);
            Source.SelectionChanged += (_, _) => CalculateCommand.RaiseCanExecuteChanged();
            Destination.SelectionChanged += (_, _) => CalculateCommand.RaiseCanExecuteChanged();

            if (!string.IsNullOrWhiteSpace(knownCarrierSystem))
            {
                _ = PrefillFleetCarrierSourceAsync(cachedSearch, knownCarrierSystem);
            }

            NeutronSource = new SpanshSystemPickerViewModel(cachedSearch, debounceDelay);
            NeutronDestination = new SpanshSystemPickerViewModel(cachedSearch, debounceDelay);
            NeutronSource.SelectionChanged += (_, _) => NeutronCalculateCommand.RaiseCanExecuteChanged();
            NeutronDestination.SelectionChanged += (_, _) => NeutronCalculateCommand.RaiseCanExecuteChanged();

            if (!string.IsNullOrWhiteSpace(knownCurrentSystem))
            {
                // Locally known from the CMDR's own journal, not (yet) Spansh-confirmed - Id64 is
                // left null since only the name is actually needed for the neutron endpoint
                // (unlike the Fleet Carrier tab's Source, which posts an id and so must come from
                // an actual Spansh search result). Still fully editable afterward: typing further
                // clears Selected again, the same as any other SpanshSystemPickerViewModel field
                // (see its own doc comment) - a fresh pick from suggestions is then required
                // before Calculate re-enables.
                NeutronSource.Selected = new SpanshSystemSuggestion(knownCurrentSystem, null, knownCurrentSystem);
            }

            _isOvercharge = defaultToOvercharge;

            GalaxySource = new SpanshSystemPickerViewModel(cachedSearch, debounceDelay);
            GalaxyDestination = new SpanshSystemPickerViewModel(cachedSearch, debounceDelay);
            GalaxySource.SelectionChanged += (_, _) => GalaxyCalculateCommand.RaiseCanExecuteChanged();
            GalaxyDestination.SelectionChanged += (_, _) => GalaxyCalculateCommand.RaiseCanExecuteChanged();

            if (!string.IsNullOrWhiteSpace(knownCurrentSystem))
            {
                _ = PrefillGalaxySourceAsync(cachedSearch, knownCurrentSystem);
            }

            GalaxyCargo = (knownCurrentCargo ?? 0).ToString(CultureInfo.InvariantCulture);

            if (!string.IsNullOrWhiteSpace(knownJournalFilePath))
            {
                _ = LoadGalaxyLoadoutAsync(readLoadoutSnapshot ?? DefaultReadLoadoutSnapshot, knownJournalFilePath);
            }
            else
            {
                GalaxyStatusMessage = "No running instance available to read a ship loadout from.";
            }
        }

        private static Task<LoadoutSnapshot?> DefaultReadLoadoutSnapshot(string journalFilePath) =>
            Task.Run(() => EliteInstanceScanner.ReadLoadoutSnapshot(journalFilePath));

        /// <summary>
        /// Resolves a known system name to a real Spansh suggestion (via a live search for its
        /// exact name) and applies it to <paramref name="field"/>, so Calculate is ready to go
        /// without the CMDR needing to type/pick it themselves - shared by the Fleet Carrier tab's
        /// own Source (PrefillFleetCarrierSourceAsync) and the Galaxy Plotter tab's own Source
        /// (PrefillGalaxySourceAsync), both of which need a real Spansh-assigned id rather than
        /// just a name (unlike the Neutron Plotter tab's own Source, which posts a bare name and
        /// so can be set directly in the constructor with no network round trip).
        ///
        /// Never overwrites anything the CMDR has already done themselves by the time the search
        /// resolves (an actual pick, or text typed into the field) - a background pre-fill catching
        /// up late must never clobber what's already the CMDR's own deliberate choice. Silently
        /// leaves the field unfilled if the search fails, or turns up no suggestion whose name
        /// matches <paramref name="systemName"/> exactly (e.g. Spansh has no record of it) - the
        /// same "just leave it blank" fallback an unresolved pre-fill already has everywhere else
        /// in this dialog.
        /// </summary>
        private async Task PrefillSourceAsync(
            SpanshSystemPickerViewModel field,
            Func<string, CancellationToken, Task<IReadOnlyList<SpanshSystemSuggestion>>> search,
            string systemName,
            string logContext)
        {
            IReadOnlyList<SpanshSystemSuggestion> results;
            try
            {
                results = await search(systemName, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Log.Warn("Spansh", $"Failed to pre-fill {logContext} Source from {systemName}.", ex);
                return;
            }

            if (field.Selected != null || !string.IsNullOrEmpty(field.Query))
            {
                return;
            }

            var match = results
                .Where(r => string.Equals(r.Name, systemName, StringComparison.OrdinalIgnoreCase))
                .Select(r => (SpanshSystemSuggestion?)r)
                .FirstOrDefault();
            if (match is { } suggestion)
            {
                field.Selected = suggestion;
            }
        }

        /// <summary>See PrefillSourceAsync. Internal (not private) so tests can await it directly rather than racing a fire-and-forget background task.</summary>
        internal Task PrefillFleetCarrierSourceAsync(
            Func<string, CancellationToken, Task<IReadOnlyList<SpanshSystemSuggestion>>> search, string carrierSystem) =>
            PrefillSourceAsync(Source, search, carrierSystem, "Fleet Carrier");

        /// <summary>See PrefillSourceAsync. Internal (not private) so tests can await it directly rather than racing a fire-and-forget background task.</summary>
        internal Task PrefillGalaxySourceAsync(
            Func<string, CancellationToken, Task<IReadOnlyList<SpanshSystemSuggestion>>> search, string currentSystem) =>
            PrefillSourceAsync(GalaxySource, search, currentSystem, "Galaxy Plotter");

        /// <summary>
        /// Wraps SearchSystemNamesAsync with an in-memory cache keyed by query text (case-
        /// insensitive), shared by both the Source and Destination fields, for as long as this
        /// dialog stays open - a fresh SpanshImportViewModel (and so a fresh cache) is created
        /// every time the dialog is opened (MainWindow.OnSpanshClick), so this never persists
        /// across dialog sessions. Avoids re-querying Spansh for a query text already seen this
        /// session (e.g. backspacing and retyping the same text, or the same name being searched
        /// for both fields) - a plain Dictionary is safe here since every call originates from the
        /// UI thread (SpanshSystemPickerViewModel's own DispatcherTimer), never truly concurrently.
        /// A cancelled/failed search is never cached (only reached after a successful await), so a
        /// superseded or failed lookup is simply retried in full next time, same as before.
        ///
        /// Internal (not private) so tests can exercise the caching behaviour directly against a
        /// fake search delegate, without needing SpanshSystemPickerViewModel's own 200ms debounce
        /// timer to actually fire (it needs a pumped Dispatcher message loop, which a plain xUnit
        /// test doesn't run).
        /// </summary>
        internal static Func<string, CancellationToken, Task<IReadOnlyList<SpanshSystemSuggestion>>> CreateCachingSearch(
            Func<string, CancellationToken, Task<IReadOnlyList<SpanshSystemSuggestion>>> search)
        {
            var cache = new Dictionary<string, IReadOnlyList<SpanshSystemSuggestion>>(StringComparer.OrdinalIgnoreCase);

            return async (query, cancellationToken) =>
            {
                if (cache.TryGetValue(query, out var cached))
                {
                    return cached;
                }

                var results = await search(query, cancellationToken);
                cache[query] = results;
                return results;
            };
        }

        public SpanshSystemPickerViewModel Source { get; }

        public SpanshSystemPickerViewModel Destination { get; }

        public AsyncRelayCommand CalculateCommand { get; }

        public RelayCommand OpenFleetCarrierRouterCommand { get; }

        /// <summary>Drives the indeterminate progress bar - true from the moment Calculate is clicked until the job either completes, fails, or is cancelled (the dialog closing).</summary>
        public bool IsCalculating
        {
            get => _isCalculating;
            private set
            {
                if (SetProperty(ref _isCalculating, value))
                {
                    CalculateCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public SpanshSystemPickerViewModel NeutronSource { get; }

        public SpanshSystemPickerViewModel NeutronDestination { get; }

        public AsyncRelayCommand NeutronCalculateCommand { get; }

        public RelayCommand OpenNeutronPlotterCommand { get; }

        /// <summary>The ship's own jump range (ly), free text - pre-filled from the CMDR's own ship when known (see the constructor), but always editable. Required (non-blank) for Calculate to enable - see CanCalculateNeutron.</summary>
        public string NeutronRange
        {
            get => _neutronRange;
            set
            {
                if (SetProperty(ref _neutronRange, value))
                {
                    NeutronCalculateCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>Spansh's own route-optimisation/speed trade-off (1-100), free text - pre-filled with Spansh's own default (60) but always editable. Required (non-blank) for Calculate to enable - see NeutronRange.</summary>
        public string NeutronEfficiency
        {
            get => _neutronEfficiency;
            set
            {
                if (SetProperty(ref _neutronEfficiency, value))
                {
                    NeutronCalculateCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// True for the "Overcharge" supercharge choice, false for "Regular" - defaults from the
        /// CMDR's own ship (see the constructor's own defaultToOvercharge parameter) but is a
        /// plain two-way radio-button choice from here on, no different from any other manually
        /// picked option. Doesn't itself gate Calculate (see CanCalculateNeutron) - there's always
        /// exactly one of the two selected, never blank.
        /// </summary>
        public bool IsOvercharge
        {
            get => _isOvercharge;
            set => SetProperty(ref _isOvercharge, value);
        }

        /// <summary>Drives the Neutron Plotter tab's own indeterminate progress bar - see IsCalculating.</summary>
        public bool IsNeutronCalculating
        {
            get => _isNeutronCalculating;
            private set
            {
                if (SetProperty(ref _isNeutronCalculating, value))
                {
                    NeutronCalculateCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string NeutronStatusMessage
        {
            get => _neutronStatusMessage;
            private set => SetProperty(ref _neutronStatusMessage, value);
        }

        public SpanshSystemPickerViewModel GalaxySource { get; }

        public SpanshSystemPickerViewModel GalaxyDestination { get; }

        public AsyncRelayCommand GalaxyCalculateCommand { get; }

        public RelayCommand OpenGalaxyPlotterCommand { get; }

        /// <summary>Every algorithm Spansh's /api/generic/route accepts, in the same order (and with the same default, "optimistic") as its own web client.</summary>
        public static IReadOnlyList<string> GalaxyAlgorithms { get; } = new[] { "fuel", "fuel_jumps", "guided", "optimistic", "pessimistic" };

        /// <summary>Drives the Galaxy Plotter tab's own indeterminate progress bar - see IsCalculating.</summary>
        public bool IsGalaxyCalculating
        {
            get => _isGalaxyCalculating;
            private set
            {
                if (SetProperty(ref _isGalaxyCalculating, value))
                {
                    GalaxyCalculateCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// Doubles as this tab's own explanation for why Calculate is disabled while no usable
        /// ship loadout has been resolved yet (see LoadGalaxyLoadoutAsync) - not just a
        /// Calculate-in-progress status, unlike StatusMessage/NeutronStatusMessage.
        /// </summary>
        public string GalaxyStatusMessage
        {
            get => _galaxyStatusMessage;
            private set => SetProperty(ref _galaxyStatusMessage, value);
        }

        /// <summary>Cargo (tons) to plan the route around - free text, pre-filled from the CMDR's own currently-tracked cargo total (see the constructor) but always editable, same convention as NeutronRange. Required (non-blank) for Calculate to enable - see CanCalculateGalaxy.</summary>
        public string GalaxyCargo
        {
            get => _galaxyCargo;
            set
            {
                if (SetProperty(ref _galaxyCargo, value))
                {
                    GalaxyCalculateCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>Fuel reserve (tons) to keep back - free text, defaults to "0", always editable. Spansh's own "reserve_size" field.</summary>
        public string GalaxyReserveTankSize
        {
            get => _galaxyReserveTankSize;
            set => SetProperty(ref _galaxyReserveTankSize, value);
        }

        /// <summary>Spansh's own "is_supercharged" route option - default false.</summary>
        public bool GalaxyIsSupercharged
        {
            get => _galaxyIsSupercharged;
            set => SetProperty(ref _galaxyIsSupercharged, value);
        }

        /// <summary>Spansh's own "use_supercharge" route option - default true.</summary>
        public bool GalaxyUseSupercharge
        {
            get => _galaxyUseSupercharge;
            set => SetProperty(ref _galaxyUseSupercharge, value);
        }

        /// <summary>Spansh's own "use_injections" route option - default false.</summary>
        public bool GalaxyUseInjections
        {
            get => _galaxyUseInjections;
            set => SetProperty(ref _galaxyUseInjections, value);
        }

        /// <summary>Spansh's own "use_injections_when_required" route option - default false.</summary>
        public bool GalaxyUseInjectionsWhenRequired
        {
            get => _galaxyUseInjectionsWhenRequired;
            set => SetProperty(ref _galaxyUseInjectionsWhenRequired, value);
        }

        /// <summary>Spansh's own "exclude_secondary" route option - default false.</summary>
        public bool GalaxyExcludeSecondary
        {
            get => _galaxyExcludeSecondary;
            set => SetProperty(ref _galaxyExcludeSecondary, value);
        }

        /// <summary>Spansh's own "refuel_every_scoopable" route option - default true.</summary>
        public bool GalaxyRefuelEveryScoopable
        {
            get => _galaxyRefuelEveryScoopable;
            set => SetProperty(ref _galaxyRefuelEveryScoopable, value);
        }

        /// <summary>One of GalaxyAlgorithms - defaults to Spansh's own default, "optimistic".</summary>
        public string GalaxyAlgorithm
        {
            get => _galaxyAlgorithm;
            set => SetProperty(ref _galaxyAlgorithm, value);
        }

        /// <summary>Raised once a route has been successfully calculated and applied to the Route tab - the view closes the dialog on this. Shared by all three tabs, since any one succeeding is the same "done" moment from the dialog's own perspective.</summary>
        public event EventHandler? RouteApplied;

        private bool CanCalculate() => !IsCalculating && Source.Selected != null && Destination.Selected != null;

        private bool CanCalculateNeutron() =>
            !IsNeutronCalculating
            && NeutronSource.Selected != null
            && NeutronDestination.Selected != null
            && !string.IsNullOrWhiteSpace(NeutronRange)
            && !string.IsNullOrWhiteSpace(NeutronEfficiency);

        private bool CanCalculateGalaxy() =>
            !IsGalaxyCalculating
            && GalaxySource.Selected != null
            && GalaxyDestination.Selected != null
            && _galaxyParameters != null
            && !string.IsNullOrWhiteSpace(GalaxyCargo);

        /// <summary>Internal (not private) so tests can await it directly - same testability precedent as CalculateNeutronAsync/CalculateGalaxyAsync.</summary>
        internal async Task CalculateAsync()
        {
            if (Source.Selected is not { } source || Destination.Selected is not { } destination)
            {
                return;
            }

            _calculateCts?.Cancel();
            var cts = new CancellationTokenSource();
            _calculateCts = cts;

            IsCalculating = true;
            StatusMessage = "Requesting route from Spansh…";

            try
            {
                var jobId = await _routeService.StartFleetCarrierRouteAsync(source.Id, destination.Id, cts.Token);
                Log.Info("Spansh", $"Fleet carrier route requested {source.Name} -> {destination.Name} (job {jobId}).");
                StatusMessage = "Queued…";

                while (true)
                {
                    await Task.Delay(PollInterval, cts.Token);

                    var status = await _routeService.GetJobResultAsync(jobId, cts.Token);
                    if (status.State == SpanshJobState.Pending)
                    {
                        // Spansh's own "state" field comes back lowercase (e.g. "queued",
                        // "running") - capitalized here for display, matching this dialog's own
                        // other status wording ("Queued…", "Route applied.", ...).
                        StatusMessage = $"{Capitalize(status.StatusText ?? "unknown")}…";
                        continue;
                    }

                    if (status.State == SpanshJobState.Failed)
                    {
                        StatusMessage = $"Failed: {status.FailureReason}";
                        Log.Warn("Spansh", $"Route calculation failed: {status.FailureReason}");
                        return;
                    }

                    if (_applyRoute(status.Jumps, RouteType.Plain))
                    {
                        Log.Info("Spansh", "Route calculated and applied.");
                        StatusMessage = "Route applied.";
                        RouteApplied?.Invoke(this, EventArgs.Empty);
                    }
                    else
                    {
                        StatusMessage = "Spansh returned an empty route.";
                    }

                    return;
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelled by the dialog closing (CancelInFlightWork) - nothing further to report.
            }
            catch (Exception ex)
            {
                StatusMessage = "Failed: could not reach Spansh.";
                Log.Warn("Spansh", "Route calculation failed.", ex);
            }
            finally
            {
                IsCalculating = false;
            }
        }

        /// <summary>Internal (not private) so tests can await it directly - StartNeutronRouteAsync's own outright-rejection error surfacing (below) is worth exercising without waiting through a real PollInterval delay.</summary>
        internal async Task CalculateNeutronAsync()
        {
            if (NeutronSource.Selected is not { } source || NeutronDestination.Selected is not { } destination)
            {
                return;
            }

            _neutronCalculateCts?.Cancel();
            var cts = new CancellationTokenSource();
            _neutronCalculateCts = cts;

            IsNeutronCalculating = true;
            NeutronStatusMessage = "Requesting route from Spansh…";

            try
            {
                var superchargeMultiplier = IsOvercharge ? ShipBuildDerivation.OverchargeSuperchargeMultiplier : ShipBuildDerivation.RegularSuperchargeMultiplier;
                var jobId = await _routeService.StartNeutronRouteAsync(source.Name, destination.Name, NeutronRange.Trim(), NeutronEfficiency.Trim(), superchargeMultiplier, cts.Token);
                Log.Info("Spansh", $"Neutron route requested {source.Name} -> {destination.Name} (job {jobId}).");
                NeutronStatusMessage = "Queued…";

                while (true)
                {
                    await Task.Delay(PollInterval, cts.Token);

                    var status = await _routeService.GetNeutronJobResultAsync(jobId, cts.Token);
                    if (status.State == SpanshJobState.Pending)
                    {
                        // Spansh's own "state" field comes back lowercase (e.g. "started") -
                        // capitalized for display, matching this dialog's own other status
                        // wording ("Queued…", "Route applied.", ...).
                        NeutronStatusMessage = $"{Capitalize(status.StatusText ?? "unknown")}…";
                        continue;
                    }

                    if (status.State == SpanshJobState.Failed)
                    {
                        NeutronStatusMessage = $"Failed: {status.FailureReason}";
                        Log.Warn("Spansh", $"Neutron route calculation failed: {status.FailureReason}");
                        return;
                    }

                    if (_applyRoute(status.Jumps, RouteType.Neutron))
                    {
                        Log.Info("Spansh", "Neutron route calculated and applied.");
                        NeutronStatusMessage = "Route applied.";
                        RouteApplied?.Invoke(this, EventArgs.Empty);
                    }
                    else
                    {
                        NeutronStatusMessage = "Spansh returned an empty route.";
                    }

                    return;
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelled by the dialog closing (CancelInFlightWork) - nothing further to report.
            }
            catch (Exception ex)
            {
                // Unlike CalculateAsync's own generic "could not reach Spansh" fallback,
                // StartNeutronRouteAsync throws with Spansh's own reported reason (e.g. "range
                // must be greater than 10 LY") for anything it rejects outright - surfaced
                // verbatim here, since it's already clear and actionable. A genuine transport
                // failure's own exception message (e.g. "No such host is known") is shown the
                // same way rather than special-cased, since it's still more useful than nothing.
                NeutronStatusMessage = $"Failed: {ex.Message}";
                Log.Warn("Spansh", "Neutron route calculation failed.", ex);
            }
            finally
            {
                IsNeutronCalculating = false;
            }
        }

        /// <summary>
        /// Re-reads <paramref name="journalFilePath"/> in the background (via
        /// <paramref name="readLoadoutSnapshot"/>) and derives this tab's own ship-specific
        /// request fields (Services\Spansh\ShipBuildDerivation), so Calculate is ready to go by
        /// the time the CMDR would actually press it - called once from the constructor. Every
        /// outcome sets GalaxyStatusMessage to something explaining the current state, rather than
        /// leaving Calculate silently disabled with no reason given (SPEC's general "explain why
        /// blocked" convention, e.g. Trim for FC's own precondition messages). Internal (not
        /// private) so tests can await it directly rather than racing a fire-and-forget background
        /// task.
        /// </summary>
        internal async Task LoadGalaxyLoadoutAsync(Func<string, Task<LoadoutSnapshot?>> readLoadoutSnapshot, string journalFilePath)
        {
            GalaxyStatusMessage = "Reading ship loadout…";

            try
            {
                var loadout = await readLoadoutSnapshot(journalFilePath);
                if (loadout is null)
                {
                    GalaxyStatusMessage = "No ship loadout logged yet this session - open the in-game Outfitting or Ship screen once, then reopen this dialog.";
                    return;
                }

                var result = ShipBuildDerivation.Derive(loadout.Value);
                if (!result.Success)
                {
                    GalaxyStatusMessage = $"Could not use this ship's loadout: {result.ErrorMessage}";
                    return;
                }

                _galaxyLoadout = loadout;
                _galaxyParameters = result.Parameters;
                GalaxyStatusMessage = string.Empty;
            }
            catch (Exception ex)
            {
                GalaxyStatusMessage = "Could not read this ship's loadout.";
                Log.Warn("Spansh", "Failed to read ship loadout for Galaxy Plotter.", ex);
            }
            finally
            {
                GalaxyCalculateCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>Internal (not private) - same testability precedent as CalculateNeutronAsync.</summary>
        internal async Task CalculateGalaxyAsync()
        {
            if (GalaxySource.Selected is not { } source || GalaxyDestination.Selected is not { } destination || _galaxyParameters is not { } parameters)
            {
                return;
            }

            _galaxyCalculateCts?.Cancel();
            var cts = new CancellationTokenSource();
            _galaxyCalculateCts = cts;

            IsGalaxyCalculating = true;
            GalaxyStatusMessage = "Requesting route from Spansh…";

            try
            {
                var request = new SpanshGenericRouteRequest(
                    SourceId: source.Id,
                    DestinationId: destination.Id,
                    IsSupercharged: GalaxyIsSupercharged,
                    UseSupercharge: GalaxyUseSupercharge,
                    UseInjections: GalaxyUseInjections,
                    UseInjectionsWhenRequired: GalaxyUseInjectionsWhenRequired,
                    ExcludeSecondary: GalaxyExcludeSecondary,
                    RefuelEveryScoopable: GalaxyRefuelEveryScoopable,
                    FuelPower: parameters.FuelPower,
                    FuelMultiplier: parameters.FuelMultiplier,
                    OptimalMass: parameters.OptimalMass,
                    BaseMass: parameters.BaseMass,
                    TankSize: parameters.TankSize,
                    InternalTankSize: parameters.InternalTankSize,
                    ReserveSize: GalaxyReserveTankSize.Trim(),
                    MaxFuelPerJump: parameters.MaxFuelPerJump,
                    RangeBoost: parameters.RangeBoost,
                    Cargo: GalaxyCargo.Trim(),
                    Algorithm: GalaxyAlgorithm,
                    SuperchargeMultiplier: parameters.SuperchargeMultiplier,
                    InjectionMultiplier: parameters.InjectionMultiplier);

                var jobId = await _routeService.StartGenericRouteAsync(request, cts.Token);
                Log.Info("Spansh", $"Galaxy route requested {source.Name} -> {destination.Name} (job {jobId}).");
                GalaxyStatusMessage = "Queued…";

                while (true)
                {
                    await Task.Delay(PollInterval, cts.Token);

                    var status = await _routeService.GetGenericJobResultAsync(jobId, cts.Token);
                    if (status.State == SpanshJobState.Pending)
                    {
                        GalaxyStatusMessage = $"{Capitalize(status.StatusText ?? "unknown")}…";
                        continue;
                    }

                    if (status.State == SpanshJobState.Failed)
                    {
                        GalaxyStatusMessage = $"Failed: {status.FailureReason}";
                        Log.Warn("Spansh", $"Galaxy route calculation failed: {status.FailureReason}");
                        return;
                    }

                    if (_applyRoute(status.Jumps, RouteType.Galaxy))
                    {
                        Log.Info("Spansh", "Galaxy route calculated and applied.");
                        GalaxyStatusMessage = "Route applied.";
                        RouteApplied?.Invoke(this, EventArgs.Empty);
                    }
                    else
                    {
                        GalaxyStatusMessage = "Spansh returned an empty route.";
                    }

                    return;
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelled by the dialog closing (CancelInFlightWork) - nothing further to report.
            }
            catch (Exception ex)
            {
                GalaxyStatusMessage = "Failed: could not reach Spansh.";
                Log.Warn("Spansh", "Galaxy route calculation failed.", ex);
            }
            finally
            {
                IsGalaxyCalculating = false;
            }
        }

        /// <summary>Called when the dialog is closed while a job is still in flight - cancels the outstanding request/poll wait rather than leaving it running against a ViewModel nothing is looking at any more.</summary>
        public void CancelInFlightWork()
        {
            _calculateCts?.Cancel();
            _neutronCalculateCts?.Cancel();
            _galaxyCalculateCts?.Cancel();
        }

        /// <summary>Capitalizes just the first character - Spansh's own "state" field is always lowercase, but this dialog's other status wording is sentence-case. Internal (not private) so tests can exercise it directly.</summary>
        internal static string Capitalize(string text) =>
            string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text[1..];
    }
}
