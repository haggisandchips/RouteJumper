using System.Collections.ObjectModel;
using System.ComponentModel;
using RouteJumper.Models;
using RouteJumper.ViewModels;

namespace RouteJumper.Services
{
    /// <summary>
    /// Drives Auto Pilot (Route tab, SPEC §4.2): while running, watches the route's rows and,
    /// for whichever row is currently in-progress, plays the Captain's selected macro (Roles
    /// tab, §5.5) against the Captain's assigned instance to plot that row's jump - but only if
    /// it actually still needs plotting (blank Status): immediately if it isn't showing Cooldown,
    /// or after Cooldown clears plus a configurable extra delay (Controls tab Options §6.1,
    /// AutoPilotDelayMs) if it is. A row that's already Plotted or Jumping - whether Auto Pilot
    /// itself just triggered it moments ago and journal tracking hasn't caught up yet, or a jump
    /// was already in flight before Auto Pilot was even engaged (app just (re)started mid-plot,
    /// or a manual play) - is left alone entirely; playing the macro again would plot a second,
    /// redundant jump. The same delay is also
    /// applied the other way around a row's Cooldown: the moment Cooldown *starts* on a row, if
    /// an Engineer is currently assigned (with a macro selected - CanEngageAutoPilot already
    /// guarantees that whenever Engineer is assigned at all), this waits the same delay and then
    /// plays the Engineer's selected macro against the Engineer's assigned instance, so refueling
    /// happens automatically alongside the Captain's own plot/jump/cooldown cycle rather than
    /// requiring manual intervention. Repeats for every row in turn until the route completes (no
    /// row is left in-progress) or Auto Pilot is stopped.
    ///
    /// Lives outside Sequencing/ deliberately - CLAUDE.md's non-negotiable rule only constrains
    /// RouteSequencer's own row-mutation logic. This class never mutates a row itself, only
    /// observes state RouteSequencer has already set, and its own delay is exactly the kind of
    /// real-world scheduling concern SPEC's journal-watcher note already carves out as
    /// belonging in a watcher, not in the row-update logic itself.
    ///
    /// Both the Captain's plot and the Engineer's refuel go through the same single _playMacro
    /// channel (ControlsViewModel.PlayMacro), which only ever runs one playback at a time -
    /// starting a new one cancels whichever is still running, the same as a manual Play does. In
    /// practice the two triggers are separated by the whole Cooldown window (Engineer fires at
    /// its start, Captain at its end) so they don't normally overlap, but a slow-running Engineer
    /// macro could still be cancelled by the Captain's plot firing before it finishes.
    ///
    /// Also announces each trigger via <see cref="_speak"/> (SpeechAnnouncer.Speak) ahead of
    /// time - "Plotting beginning shortly"/"Plotting beginning in 5 seconds" before the Captain's
    /// macro plays, and the same wording with "Refueling" before the Engineer's. Both targets are
    /// only ever knowable once Cooldown starts (the Engineer's own trigger is a fixed delay from
    /// that moment; the Captain's is a fixed delay from Cooldown's own precomputed clear time -
    /// RouteRowViewModel.PhaseEndUtc, the same instant the progress bar counts down to), so both
    /// are scheduled together right there, once per row's Cooldown period - never for the
    /// "fires immediately, no Cooldown was waited on" case, since that leaves no lead time to
    /// announce anything against.
    /// </summary>
    public sealed class AutoPilotController
    {
        private static readonly TimeSpan ShortlyLeadTime = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan ImminentLeadTime = TimeSpan.FromSeconds(5);

        private readonly ObservableCollection<RouteRowViewModel> _rows;
        private readonly Func<RecordedMacroViewModel?> _getCaptainMacro;
        private readonly Func<EliteInstanceViewModel?> _getCaptainInstance;
        private readonly Func<RecordedMacroViewModel?> _getEngineerMacro;
        private readonly Func<EliteInstanceViewModel?> _getEngineerInstance;
        private readonly Func<int> _getAutoPilotDelayMs;
        private readonly Action<RecordedMacroViewModel, EliteInstanceViewModel> _playMacro;
        private readonly Action _onRouteComplete;
        private readonly Action<string> _speak;

        private CancellationTokenSource? _cts;
        private RouteRowViewModel? _lastTriggeredRow;
        private RouteRowViewModel? _pendingCooldownRow;
        private RouteRowViewModel? _engineerTriggeredForRow;
        private bool _isRunning;
        private bool _evaluationScheduled;

        public AutoPilotController(
            ObservableCollection<RouteRowViewModel> rows,
            Func<RecordedMacroViewModel?> getCaptainMacro,
            Func<EliteInstanceViewModel?> getCaptainInstance,
            Func<RecordedMacroViewModel?> getEngineerMacro,
            Func<EliteInstanceViewModel?> getEngineerInstance,
            Func<int> getAutoPilotDelayMs,
            Action<RecordedMacroViewModel, EliteInstanceViewModel> playMacro,
            Action onRouteComplete,
            Action<string> speak)
        {
            _rows = rows;
            _getCaptainMacro = getCaptainMacro;
            _getCaptainInstance = getCaptainInstance;
            _getEngineerMacro = getEngineerMacro;
            _getEngineerInstance = getEngineerInstance;
            _getAutoPilotDelayMs = getAutoPilotDelayMs;
            _playMacro = playMacro;
            _onRouteComplete = onRouteComplete;
            _speak = speak;
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
            _engineerTriggeredForRow = null;
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
            _engineerTriggeredForRow = null;
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

                // Fire the Engineer's refuel exactly once per row's Cooldown period - further
                // evaluations while it's still active (e.g. some other row's Icon/Status changing
                // elsewhere) must not restart the wait or replay the macro. This is also the only
                // moment either trigger's eventual firing time is knowable, so it's also where
                // both triggers' advance announcements get scheduled - see AnnounceBeforeTrigger.
                if (!ReferenceEquals(_engineerTriggeredForRow, currentRow))
                {
                    _engineerTriggeredForRow = currentRow;
                    var delay = TimeSpan.FromMilliseconds(Math.Max(0, _getAutoPilotDelayMs()));
                    var nowUtc = DateTime.UtcNow;

                    AnnounceBeforeTrigger(
                        "Refueling",
                        nowUtc + delay,
                        () => _getEngineerMacro() != null && _getEngineerInstance() is { WindowHandle: not 0 },
                        _cts!.Token);

                    if (currentRow.PhaseEndUtc is { } cooldownClearsAtUtc)
                    {
                        AnnounceBeforeTrigger("Plotting", cooldownClearsAtUtc + delay, isRelevant: null, _cts.Token);
                    }

                    _ = TriggerEngineerRefuelAsync(_cts.Token);
                }

                return;
            }

            if (!string.IsNullOrEmpty(currentRow.Status))
            {
                // Already Plotted or Jumping - a jump for this row has already been requested,
                // whether that happened before Auto Pilot was even engaged (e.g. the app was
                // (re)started, or the jump was plotted manually, mid-journey with a plot already
                // in flight) or Auto Pilot itself triggered it moments ago and journal tracking
                // just hasn't advanced the row past it yet. Either way, playing the Captain's
                // macro again here would plot a second, redundant jump - there's nothing to do
                // but wait for journal tracking to naturally move this row on. Still remember it
                // as handled, so an unrelated PropertyChanged elsewhere doesn't re-examine it.
                _pendingCooldownRow = null;
                _lastTriggeredRow = currentRow;
                return;
            }

            // Only apply the extra delay if this row was the one we were just waiting on
            // Cooldown for - a row that never showed Cooldown at all (e.g. Auto Pilot engaged
            // mid-route with nothing currently cooling down, or a manual "Set next system"
            // override) fires immediately instead.
            var applyDelay = ReferenceEquals(_pendingCooldownRow, currentRow);
            _pendingCooldownRow = null;
            _lastTriggeredRow = currentRow;

            _ = TriggerCaptainPlotAsync(applyDelay, _cts!.Token);
        }

        private async Task TriggerCaptainPlotAsync(bool applyDelay, CancellationToken cancellationToken)
        {
            try
            {
                if (applyDelay)
                {
                    await DelayAsync(cancellationToken);
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

        /// <summary>
        /// Mirrors TriggerCaptainPlotAsync, but for the Engineer's refuel macro - always waits
        /// the same AutoPilotDelayMs first (there's no "fires immediately" case here, unlike the
        /// Captain's plot, since this is only ever called right as Cooldown starts), then plays
        /// it if an Engineer is currently assigned with a macro selected. A no-op (after the
        /// wait) if not - CanEngageAutoPilot already requires a selected Engineer macro whenever
        /// Engineer is assigned at all, but Engineer being unassigned entirely is always valid.
        /// </summary>
        private async Task TriggerEngineerRefuelAsync(CancellationToken cancellationToken)
        {
            try
            {
                await DelayAsync(cancellationToken);

                if (_getEngineerMacro() is { } macro && _getEngineerInstance() is { WindowHandle: not 0 } instance)
                {
                    _playMacro(macro, instance);
                }
            }
            catch (OperationCanceledException)
            {
                // Auto Pilot was stopped while waiting - nothing to do.
            }
        }

        /// <summary>
        /// Schedules the two spoken lead-time announcements for a trigger due at
        /// <paramref name="triggerAtUtc"/> - "{activity} beginning shortly" 30 seconds before it,
        /// then "{activity} beginning in 5 seconds" 5 seconds before. Either (or both) is simply
        /// skipped if that much lead time has already passed by the time this is called (e.g. a
        /// short configured Auto Pilot delay), rather than announcing something already imminent
        /// or already done as if it were still 30/5 seconds out. <paramref name="isRelevant"/>,
        /// if given, is re-checked right before speaking - e.g. the Engineer's refuel trigger is
        /// only worth announcing if an Engineer is (still) actually assigned with a macro
        /// selected by the time it would fire.
        /// </summary>
        private void AnnounceBeforeTrigger(string activity, DateTime triggerAtUtc, Func<bool>? isRelevant, CancellationToken cancellationToken)
        {
            _ = AnnounceAtAsync(activity, "beginning shortly", triggerAtUtc - ShortlyLeadTime, isRelevant, cancellationToken);
            _ = AnnounceAtAsync(activity, "beginning in 5 seconds", triggerAtUtc - ImminentLeadTime, isRelevant, cancellationToken);
        }

        private async Task AnnounceAtAsync(string activity, string suffix, DateTime whenUtc, Func<bool>? isRelevant, CancellationToken cancellationToken)
        {
            var delay = whenUtc - DateTime.UtcNow;
            if (delay <= TimeSpan.Zero)
            {
                return;
            }

            try
            {
                await Task.Delay(delay, cancellationToken);
                if (isRelevant?.Invoke() ?? true)
                {
                    _speak($"{activity} {suffix}");
                }
            }
            catch (OperationCanceledException)
            {
                // Auto Pilot was stopped (or this trigger was superseded) while waiting - nothing
                // to announce.
            }
        }

        private async Task DelayAsync(CancellationToken cancellationToken)
        {
            var delayMs = Math.Max(0, _getAutoPilotDelayMs());
            if (delayMs > 0)
            {
                await Task.Delay(delayMs, cancellationToken);
            }
        }
    }
}
