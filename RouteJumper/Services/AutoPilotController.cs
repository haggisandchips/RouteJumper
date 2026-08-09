using System.Collections.ObjectModel;
using System.ComponentModel;
using RouteJumper.Models;
using RouteJumper.ViewModels;

namespace RouteJumper.Services
{
    /// <summary>
    /// Drives Auto Pilot (Route tab, SPEC §4.2): while running, watches the route's rows and,
    /// for whichever row is currently in-progress, plays the Captain's selected macro (Roles
    /// tab, §5.5) against the Captain's assigned instance to plot that row's jump - immediately
    /// if the row isn't showing Cooldown, or after Cooldown clears plus a configurable extra
    /// delay (Controls tab Options, §6.1) if it is. Repeats for every row in turn until the
    /// route completes (no row is left in-progress) or Auto Pilot is stopped.
    ///
    /// Lives outside Sequencing/ deliberately - CLAUDE.md's non-negotiable rule only constrains
    /// RouteSequencer's own row-mutation logic. This class never mutates a row itself, only
    /// observes state RouteSequencer has already set, and its own delay is exactly the kind of
    /// real-world scheduling concern SPEC's journal-watcher note already carves out as
    /// belonging in a watcher, not in the row-update logic itself.
    /// </summary>
    public sealed class AutoPilotController
    {
        private readonly ObservableCollection<RouteRowViewModel> _rows;
        private readonly Func<RecordedMacroViewModel?> _getCaptainMacro;
        private readonly Func<EliteInstanceViewModel?> _getCaptainInstance;
        private readonly Func<int> _getCooldownDelayMs;
        private readonly Action<RecordedMacroViewModel, EliteInstanceViewModel> _playMacro;
        private readonly Action _onRouteComplete;

        private CancellationTokenSource? _cts;
        private RouteRowViewModel? _lastTriggeredRow;
        private RouteRowViewModel? _pendingCooldownRow;
        private bool _isRunning;
        private bool _evaluationScheduled;

        public AutoPilotController(
            ObservableCollection<RouteRowViewModel> rows,
            Func<RecordedMacroViewModel?> getCaptainMacro,
            Func<EliteInstanceViewModel?> getCaptainInstance,
            Func<int> getCooldownDelayMs,
            Action<RecordedMacroViewModel, EliteInstanceViewModel> playMacro,
            Action onRouteComplete)
        {
            _rows = rows;
            _getCaptainMacro = getCaptainMacro;
            _getCaptainInstance = getCaptainInstance;
            _getCooldownDelayMs = getCooldownDelayMs;
            _playMacro = playMacro;
            _onRouteComplete = onRouteComplete;
        }

        /// <summary>Begins watching the route - evaluates immediately (covers "Auto Pilot was just clicked"), then again every time a row's Icon/Status changes.</summary>
        public void Start()
        {
            if (_isRunning)
            {
                return;
            }

            _isRunning = true;
            _lastTriggeredRow = null;
            _pendingCooldownRow = null;
            _cts = new CancellationTokenSource();

            foreach (var row in _rows)
            {
                row.PropertyChanged += OnRowPropertyChanged;
            }

            ScheduleEvaluation();
        }

        /// <summary>Stops watching and cancels whatever wait (if any) is currently pending. A no-op if not currently running.</summary>
        public void Stop()
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;

            foreach (var row in _rows)
            {
                row.PropertyChanged -= OnRowPropertyChanged;
            }

            _cts?.Cancel();
            _cts = null;
            _pendingCooldownRow = null;
            _evaluationScheduled = false;
        }

        private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(RouteRowViewModel.Icon) or nameof(RouteRowViewModel.Status))
            {
                ScheduleEvaluation();
            }
        }

        /// <summary>
        /// A single row transition (e.g. RouteSequencer's Arrived case) sets a completed row's
        /// Icon, then the next row's Icon, then - separately again - that row's Status, each a
        /// distinct assignment that raises its own PropertyChanged synchronously the instant it
        /// happens. Reacting to each one inline would see the route mid-transition (e.g. a
        /// moment where no row is in-progress at all, or a row that's InProgress but whose
        /// Cooldown Status hasn't been set yet) and could act on that transient, not-yet-settled
        /// state. Deferring via Task.Yield lets the *whole* synchronous transition finish first -
        /// EvaluateAndMaybeTrigger then only ever sees fully-settled row state - while
        /// _evaluationScheduled coalesces however many PropertyChanged events one transition
        /// raises into a single evaluation.
        /// </summary>
        private void ScheduleEvaluation()
        {
            if (_evaluationScheduled || !_isRunning)
            {
                return;
            }

            _evaluationScheduled = true;
            _ = DeferredEvaluateAsync();
        }

        private async Task DeferredEvaluateAsync()
        {
            await Task.Yield();
            _evaluationScheduled = false;
            EvaluateAndMaybeTrigger();
        }

        /// <summary>
        /// The single decision point - run both once at Start() (covering "Auto Pilot was just
        /// clicked") and every time afterward a row's Icon/Status changes (covering every
        /// subsequent jump), so "Cooldown active right now → wait for it plus the extra delay"
        /// and "Cooldown not active → fire immediately" is one rule applied uniformly, not two
        /// separately-implemented cases.
        /// </summary>
        private void EvaluateAndMaybeTrigger()
        {
            if (!_isRunning)
            {
                return;
            }

            var currentRow = _rows.FirstOrDefault(r => r.Icon == RowIcon.InProgress);
            if (currentRow is null)
            {
                // "No row is in-progress right now" is not, on its own, proof the route is
                // done - RouteSequencer's Arrived case (and Reset) sets a completed row's Icon
                // and the next row's Icon in two separate assignments, each raising its own
                // PropertyChanged the instant it happens; this method runs synchronously on the
                // first one, transiently seeing *no* in-progress row a moment before the second
                // assignment supplies one. Only treat it as real completion once every row has
                // actually reached Complete - otherwise just wait for the next PropertyChanged
                // (already on its way) to re-evaluate.
                if (_rows.Count > 0 && _rows.All(r => r.Icon == RowIcon.Complete))
                {
                    Stop();
                    _onRouteComplete();
                }

                return;
            }

            if (ReferenceEquals(currentRow, _lastTriggeredRow))
            {
                return;
            }

            if (currentRow.Status == "Cooldown")
            {
                _pendingCooldownRow = currentRow;
                return;
            }

            // Only apply the extra delay if this row was the one we were just waiting on
            // Cooldown for - a row that never showed Cooldown at all (e.g. Auto Pilot engaged
            // mid-route with nothing currently cooling down, or a manual "Set next system"
            // override) fires immediately instead.
            var applyDelay = ReferenceEquals(_pendingCooldownRow, currentRow);
            _pendingCooldownRow = null;
            _lastTriggeredRow = currentRow;

            _ = TriggerAsync(applyDelay, _cts!.Token);
        }

        private async Task TriggerAsync(bool applyDelay, CancellationToken cancellationToken)
        {
            try
            {
                if (applyDelay)
                {
                    var delayMs = Math.Max(0, _getCooldownDelayMs());
                    if (delayMs > 0)
                    {
                        await Task.Delay(delayMs, cancellationToken);
                    }
                }

                if (_getCaptainMacro() is { } macro && _getCaptainInstance() is { WindowHandle: not 0 } instance)
                {
                    _playMacro(macro, instance);
                }
            }
            catch (OperationCanceledException)
            {
                // Auto Pilot was stopped while waiting - nothing to do.
            }
        }
    }
}
