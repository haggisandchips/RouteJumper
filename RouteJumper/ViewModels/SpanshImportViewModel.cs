using RouteJumper.Common;
using RouteJumper.Models;
using RouteJumper.Services;
using RouteJumper.Services.Logging;

namespace RouteJumper.ViewModels
{
    /// <summary>
    /// ViewModel for the "Spansh" modal dialog (Integrations &gt; Spansh) - currently a single
    /// "Fleet Carrier" tab: pick a Source and Destination system (SpanshSystemPickerViewModel),
    /// then Calculate requests a route from Spansh and polls it to completion, applying the
    /// result to the Route tab via <see cref="_applyRoute"/> (RouteViewModel.ImportFromSpansh, in
    /// production - injected so this ViewModel has no direct reference to RouteViewModel, the
    /// same cross-tab decoupling principle MainViewModel already uses elsewhere).
    /// </summary>
    public class SpanshImportViewModel : ObservableObject
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

        private readonly ISpanshRouteService _routeService;
        private readonly Func<IReadOnlyList<SpanshRouteJump>, bool> _applyRoute;

        private bool _isCalculating;
        private string _statusMessage = string.Empty;
        private CancellationTokenSource? _calculateCts;

        /// <summary>
        /// <paramref name="config"/> defaults to a real AppConfigStore, read once here (a fresh
        /// SpanshImportViewModel is created every time the dialog is opened, MainWindow.
        /// OnSpanshClick, so a hand-edited routejumper.conf takes effect on the next open, the same
        /// "re-read on the next relevant action" convention AppConfigStore's other settings follow)
        /// - overridable so tests can supply a directory-scoped instance instead.
        /// </summary>
        public SpanshImportViewModel(
            ISpanshRouteService routeService, Func<IReadOnlyList<SpanshRouteJump>, bool> applyRoute, AppConfigStore? config = null)
        {
            _routeService = routeService;
            _applyRoute = applyRoute;

            CalculateCommand = new AsyncRelayCommand(CalculateAsync, CanCalculate);

            var debounceDelay = TimeSpan.FromMilliseconds((config ?? new AppConfigStore()).SpanshAutocompleteDebounceMs);
            var cachedSearch = CreateCachingSearch(routeService.SearchSystemNamesAsync);
            Source = new SpanshSystemPickerViewModel(cachedSearch, debounceDelay);
            Destination = new SpanshSystemPickerViewModel(cachedSearch, debounceDelay);
            Source.SelectionChanged += (_, _) => CalculateCommand.RaiseCanExecuteChanged();
            Destination.SelectionChanged += (_, _) => CalculateCommand.RaiseCanExecuteChanged();
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

        /// <summary>Raised once a route has been successfully calculated and applied to the Route tab - the view closes the dialog on this.</summary>
        public event EventHandler? RouteApplied;

        private bool CanCalculate() => !IsCalculating && Source.Selected != null && Destination.Selected != null;

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

        /// <summary>Called when the dialog is closed while a job is still in flight - cancels the outstanding request/poll wait rather than leaving it running against a ViewModel nothing is looking at any more.</summary>
        public void CancelInFlightWork() => _calculateCts?.Cancel();

        /// <summary>Capitalizes just the first character - Spansh's own "state" field is always lowercase, but this dialog's other status wording is sentence-case. Internal (not private) so tests can exercise it directly.</summary>
        internal static string Capitalize(string text) =>
            string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text[1..];
    }
}
