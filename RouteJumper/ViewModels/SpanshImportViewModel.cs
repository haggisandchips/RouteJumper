using RouteJumper.Common;
using RouteJumper.Models;
using RouteJumper.Services;
using RouteJumper.Services.Logging;

namespace RouteJumper.ViewModels
{
    /// <summary>
    /// ViewModel for the "Spansh" modal dialog (Spansh menu) - a "Fleet Carrier" tab:
    /// pick a Source and Destination system (SpanshSystemPickerViewModel), then Calculate
    /// requests a route from Spansh and polls it to completion; and a "Neutron Plotter" tab (same
    /// picker, plus an editable Range/Efficiency) calculating a neutron-highway route instead.
    /// Both apply their result to the Route tab via <see cref="_applyRoute"/>
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

        /// <summary>Spansh's own default efficiency (Neutron Plotter tab) - a route-optimisation/speed trade-off, editable but pre-filled with this until changed.</summary>
        private const string DefaultNeutronEfficiency = "60";

        /// <summary>Spansh's own "supercharge_multiplier" value for a regular neutron/white dwarf supercharge - confirmed against Spansh's own web client's bundled JS.</summary>
        private const int RegularSuperchargeMultiplier = 4;

        /// <summary>Spansh's own "supercharge_multiplier" value for an overcharged FSD booster - see RegularSuperchargeMultiplier.</summary>
        private const int OverchargeSuperchargeMultiplier = 6;

        private readonly ISpanshRouteService _routeService;
        private readonly Func<IReadOnlyList<SpanshRouteJump>, bool> _applyRoute;

        private bool _isCalculating;
        private string _statusMessage = string.Empty;
        private CancellationTokenSource? _calculateCts;

        private bool _isNeutronCalculating;
        private string _neutronStatusMessage = string.Empty;
        private string _neutronRange = string.Empty;
        private string _neutronEfficiency = DefaultNeutronEfficiency;
        private bool _isOvercharge;
        private CancellationTokenSource? _neutronCalculateCts;

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
        /// </summary>
        public SpanshImportViewModel(
            ISpanshRouteService routeService,
            Func<IReadOnlyList<SpanshRouteJump>, bool> applyRoute,
            AppConfigStore? config = null,
            string? knownCurrentSystem = null,
            string? knownCarrierSystem = null,
            bool defaultToOvercharge = false)
        {
            _routeService = routeService;
            _applyRoute = applyRoute;

            CalculateCommand = new AsyncRelayCommand(CalculateAsync, CanCalculate);
            OpenFleetCarrierRouterCommand = new RelayCommand(() => BrowserLauncher.Open(FleetCarrierRouterUrl));
            NeutronCalculateCommand = new AsyncRelayCommand(CalculateNeutronAsync, CanCalculateNeutron);
            OpenNeutronPlotterCommand = new RelayCommand(() => BrowserLauncher.Open(NeutronPlotterUrl));

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
        }

        /// <summary>
        /// Resolves the Captain's fleet carrier's own current location to a real Spansh suggestion
        /// (via a live search for its exact name) and applies it to the Fleet Carrier tab's own
        /// Source, so Calculate is ready to go without the CMDR needing to type/pick it themselves
        /// - the same "pre-fill from what's already known" convention the Neutron Plotter tab's own
        /// Source already follows, just requiring a real network round trip first here since this
        /// tab's own Calculate needs a Spansh-assigned id, not just a name. Internal (not private)
        /// so tests can await it directly rather than racing a fire-and-forget background task.
        ///
        /// Never overwrites anything the CMDR has already done themselves by the time the search
        /// resolves (an actual pick, or text typed into Source) - a background pre-fill catching up
        /// late must never clobber what's already the CMDR's own deliberate choice. Silently leaves
        /// Source unfilled if the search fails, or turns up no suggestion whose name matches the
        /// carrier's own system exactly (e.g. Spansh has no record of it) - the same "just leave it
        /// blank" fallback an unresolved pre-fill already has everywhere else in this dialog.
        /// </summary>
        internal async Task PrefillFleetCarrierSourceAsync(
            Func<string, CancellationToken, Task<IReadOnlyList<SpanshSystemSuggestion>>> search, string carrierSystem)
        {
            IReadOnlyList<SpanshSystemSuggestion> results;
            try
            {
                results = await search(carrierSystem, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Log.Warn("Spansh", $"Failed to pre-fill Fleet Carrier Source from the carrier's current location ({carrierSystem}).", ex);
                return;
            }

            if (Source.Selected != null || !string.IsNullOrEmpty(Source.Query))
            {
                return;
            }

            var match = results
                .Where(r => string.Equals(r.Name, carrierSystem, StringComparison.OrdinalIgnoreCase))
                .Select(r => (SpanshSystemSuggestion?)r)
                .FirstOrDefault();
            if (match is { } suggestion)
            {
                Source.Selected = suggestion;
            }
        }

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

        /// <summary>Raised once a route has been successfully calculated and applied to the Route tab - the view closes the dialog on this. Shared by both tabs, since either one succeeding is the same "done" moment from the dialog's own perspective.</summary>
        public event EventHandler? RouteApplied;

        private bool CanCalculate() => !IsCalculating && Source.Selected != null && Destination.Selected != null;

        private bool CanCalculateNeutron() =>
            !IsNeutronCalculating
            && NeutronSource.Selected != null
            && NeutronDestination.Selected != null
            && !string.IsNullOrWhiteSpace(NeutronRange)
            && !string.IsNullOrWhiteSpace(NeutronEfficiency);

        private async Task CalculateAsync()
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

                    if (_applyRoute(status.Jumps))
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
                var superchargeMultiplier = IsOvercharge ? OverchargeSuperchargeMultiplier : RegularSuperchargeMultiplier;
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

                    if (_applyRoute(status.Jumps))
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

        /// <summary>Called when the dialog is closed while a job is still in flight - cancels the outstanding request/poll wait rather than leaving it running against a ViewModel nothing is looking at any more.</summary>
        public void CancelInFlightWork()
        {
            _calculateCts?.Cancel();
            _neutronCalculateCts?.Cancel();
        }

        /// <summary>Capitalizes just the first character - Spansh's own "state" field is always lowercase, but this dialog's other status wording is sentence-case. Internal (not private) so tests can exercise it directly.</summary>
        internal static string Capitalize(string text) =>
            string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text[1..];
    }
}
