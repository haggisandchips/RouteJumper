using System.Collections.ObjectModel;
using System.ComponentModel;
using RouteJumper.Models;
using RouteJumper.Sequencing;
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
    /// redundant jump. Repeats for every row in turn until the route completes (no row is left
    /// in-progress) or Auto Pilot is stopped.
    ///
    /// The Engineer's refuel still fires at exactly the same real-world moment as before -
    /// AutoPilotDelayMs after the *next* row's Cooldown starts (i.e. near the end of *this* row's
    /// own Jumping phase, not before it) - only *when that moment gets computed* has changed.
    /// Rather than waiting to literally observe Cooldown starting live (which leaves only
    /// AutoPilotDelayMs itself as lead time - nowhere near enough for the 30-second warning
    /// below), it's computed as soon as this row becomes Jumping, using
    /// RouteRowViewModel.PhaseEndUtc at that point - already exactly DepartureTime + 1 minute,
    /// the same "estimated arrival" instant the progress bar itself counts down to (see
    /// CarrierRouteJournalWatcher) - which the carrier's own arrival will hand off Cooldown to on
    /// the very next row at. That's known the moment Jumping starts (itself always 3 minutes
    /// before DepartureTime), giving several real minutes of lead time to schedule against
    /// instead of none.
    ///
    /// Lives outside Sequencing/ deliberately - CLAUDE.md's non-negotiable rule only constrains
    /// RouteSequencer's own row-mutation logic. This class never mutates a row itself, only
    /// observes state RouteSequencer has already set, and its own delay is exactly the kind of
    /// real-world scheduling concern SPEC's journal-watcher note already carves out as
    /// belonging in a watcher, not in the row-update logic itself.
    ///
    /// Both the Captain's plot and the Engineer's refuel go through the same single _playMacro
    /// channel (ControlsViewModel.PlayMacro), which only ever runs one playback at a time -
    /// starting a new one cancels whichever is still running, the same as a manual Play does.
    ///
    /// Also announces each trigger via <see cref="_speak"/> (SpeechAnnouncer.Speak) ahead of
    /// time - "Plotting beginning shortly"/"Plotting beginning in 5 seconds" before the Captain's
    /// macro plays, and the same wording with "Refueling" before the Engineer's - see
    /// AnnounceBeforeTrigger.
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
        private readonly ManualRowEventTrigger _routeEventTrigger;

        private CancellationTokenSource? _cts;
        private RouteRowViewModel? _captainTriggeredForRow;
        private RouteRowViewModel? _pendingCooldownRow;
        private RouteRowViewModel? _refuelTriggeredForRow;
        private RouteRowViewModel? _plottingAnnouncedForRow;
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
            Action<string> speak,
            ManualRowEventTrigger routeEventTrigger)
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
            _routeEventTrigger = routeEventTrigger;
        }

        /// <summary>Begins watching the route - evaluates immediately (covers "Auto Pilot was just clicked"), then again every time a row's Icon/Status changes.</summary>
        public void Start()
        {
            if (_isRunning)
            {
                return;
            }

            _isRunning = true;
            _captainTriggeredForRow = null;
            _pendingCooldownRow = null;
            _refuelTriggeredForRow = null;
            _plottingAnnouncedForRow = null;
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
            _refuelTriggeredForRow = null;
            _plottingAnnouncedForRow = null;
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

            if (currentRow.Status == "Cooldown")
            {
                _pendingCooldownRow = currentRow;

                // The Engineer's refuel is no longer triggered from here - see the class doc
                // comment and the "Plotted" branch below, which schedules it (once) against this
                // same row's own eventual DepartureTime instead, once it's actually known. This
                // branch now only schedules the Plotting announcement - Cooldown's own clear
                // time (its only knowable moment) - exactly once per row's Cooldown period.
                if (!ReferenceEquals(_plottingAnnouncedForRow, currentRow))
                {
                    _plottingAnnouncedForRow = currentRow;

                    if (currentRow.PhaseEndUtc is { } cooldownClearsAtUtc)
                    {
                        var delay = TimeSpan.FromMilliseconds(Math.Max(0, _getAutoPilotDelayMs()));
                        AnnounceBeforeTrigger("Plotting", cooldownClearsAtUtc + delay, isRelevant: null, _cts!.Token);
                    }
                }

                return;
            }

            if (currentRow.Status == "Jumping")
            {
                // The one moment this row's own estimated arrival instant is knowable ahead of
                // time (PhaseEndUtc here is already DepartureTime + 1 minute - see the class doc
                // comment) - schedule the Engineer's refuel (and its own advance announcements)
                // against it exactly once per row, the same real-world target
                // ("AutoPilotDelayMs after Cooldown starts") as before, just computed minutes
                // early instead of at the moment Cooldown itself is actually observed starting.
                if (!ReferenceEquals(_refuelTriggeredForRow, currentRow) && currentRow.PhaseEndUtc is { } estimatedCooldownStartUtc)
                {
                    _refuelTriggeredForRow = currentRow;
                    var delay = TimeSpan.FromMilliseconds(Math.Max(0, _getAutoPilotDelayMs()));
                    var refuelTriggerAtUtc = estimatedCooldownStartUtc + delay;

                    AnnounceBeforeTrigger(
                        "Refueling",
                        refuelTriggerAtUtc,
                        () => _getEngineerMacro() != null && _getEngineerInstance() is { WindowHandle: not 0 },
                        _cts!.Token);

                    _ = TriggerEngineerRefuelAsync(refuelTriggerAtUtc, _cts.Token);
                }

                _pendingCooldownRow = null;
                return;
            }

            if (!string.IsNullOrEmpty(currentRow.Status))
            {
                // Plotted - a jump has been requested but hasn't started counting down to
                // arrival yet (see the "Jumping" branch above for when refuel gets scheduled).
                // Nothing to do here but wait for journal tracking to naturally move this row on
                // to Jumping.
                _pendingCooldownRow = null;
                return;
            }

            // Blank status - the row needs the Captain's jump plotted. Guarded by its own
            // per-row flag (not the same as the guards above, and deliberately never reset for
            // this row afterward) so a CarrierJumpCancelled reverting Status back to blank later
            // doesn't cause a second, redundant plot for the same row - SPEC §4.7's "Auto Pilot
            // only ever plays the Captain's macro once per row it newly becomes aware of".
            if (ReferenceEquals(_captainTriggeredForRow, currentRow))
            {
                return;
            }

            _captainTriggeredForRow = currentRow;

            // Only apply the extra delay if this row was the one we were just waiting on
            // Cooldown for - a row that never showed Cooldown at all (e.g. Auto Pilot engaged
            // mid-route with nothing currently cooling down, or a manual "Set next system"
            // override) fires immediately instead.
            var applyDelay = ReferenceEquals(_pendingCooldownRow, currentRow);
            _pendingCooldownRow = null;

            _ = TriggerCaptainPlotAsync(currentRow.SystemText, applyDelay, _cts!.Token);
        }

        /// <summary>
        /// Raises RowEventKind.Plotting for <paramref name="systemName"/> the instant the
        /// Captain's macro actually starts playing - not before the wait above, and not on any
        /// journal event (see RowEventKind.Plotting) - so RouteSequencer can show that row as
        /// actively being plotted (an indeterminate cue, §4.4) rather than sitting blank while
        /// the macro clicks through the game's UI.
        /// </summary>
        private async Task TriggerCaptainPlotAsync(string systemName, bool applyDelay, CancellationToken cancellationToken)
        {
            try
            {
                if (applyDelay)
                {
                    await DelayAsync(cancellationToken);
                }

                if (_getCaptainMacro() is { } macro && _getCaptainInstance() is { WindowHandle: not 0 } instance)
                {
                    _routeEventTrigger.Fire(RowEventKind.Plotting, systemName);
                    _playMacro(macro, instance);
                }
            }
            catch (OperationCanceledException)
            {
                // Auto Pilot was stopped while waiting - nothing to do.
            }
        }

        /// <summary>
        /// Mirrors TriggerCaptainPlotAsync, but for the Engineer's refuel macro - waits until
        /// <paramref name="triggerAtUtc"/> (see the "Plotted" branch in EvaluateAndMaybeTrigger;
        /// fires immediately instead if that instant has already passed by the time this runs),
        /// then plays it if an Engineer is currently assigned with a macro selected. A no-op
        /// (after the wait) if not - CanEngageAutoPilot already requires a selected Engineer
        /// macro whenever Engineer is assigned at all, but Engineer being unassigned entirely is
        /// always valid.
        /// </summary>
        private async Task TriggerEngineerRefuelAsync(DateTime triggerAtUtc, CancellationToken cancellationToken)
        {
            try
            {
                var delay = triggerAtUtc - DateTime.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }

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
        /// then "{activity} beginning in 5 seconds" 5 seconds before. The 30-second one is simply
        /// skipped if that much lead time isn't actually available (e.g. the Engineer's refuel,
        /// which normally only has the configured Auto Pilot delay - a few seconds by default -
        /// between Cooldown starting and the trigger itself, nowhere near 30 seconds) - announcing
        /// it moments early would just be misleading wording. The 5-second one instead speaks
        /// right away whenever there's less than 5 seconds left but the trigger hasn't actually
        /// happened yet, rather than being skipped the same way - a short-but-real gap should
        /// still get *some* last-moment heads-up rather than silently none at all, which is what
        /// the Engineer's refuel would otherwise almost always get under the default 5000ms Auto
        /// Pilot delay (its own 5-second mark landing right at "now", losing the race against
        /// this method's own tiny bit of processing time on every single run).
        /// <paramref name="isRelevant"/>, if given, is re-checked right before speaking - e.g.
        /// the Engineer's refuel trigger is only worth announcing if an Engineer is (still)
        /// actually assigned with a macro selected by the time it would fire.
        /// </summary>
        private void AnnounceBeforeTrigger(string activity, DateTime triggerAtUtc, Func<bool>? isRelevant, CancellationToken cancellationToken)
        {
            _ = AnnounceAtAsync(activity, "beginning shortly", triggerAtUtc - ShortlyLeadTime, clampToImmediate: false, isRelevant, cancellationToken);
            _ = AnnounceAtAsync(activity, "beginning in 5 seconds", triggerAtUtc - ImminentLeadTime, clampToImmediate: true, isRelevant, cancellationToken);
        }

        /// <summary>
        /// Pure timing decision behind AnnounceAtAsync, pulled out so the exact edge case that
        /// let this go silently wrong once already (the Engineer's 5-second mark landing right
        /// at "now" under the default Auto Pilot delay, losing the race against real elapsed time
        /// on every single run) is covered by a fast, deterministic unit test instead of relying
        /// on a live Task.Delay. Null means "skip entirely".
        /// </summary>
        internal static TimeSpan? ComputeAnnounceDelay(DateTime whenUtc, DateTime nowUtc, bool clampToImmediate)
        {
            var delay = whenUtc - nowUtc;
            if (delay > TimeSpan.Zero)
            {
                return delay;
            }

            return clampToImmediate ? TimeSpan.Zero : null;
        }

        private async Task AnnounceAtAsync(string activity, string suffix, DateTime whenUtc, bool clampToImmediate, Func<bool>? isRelevant, CancellationToken cancellationToken)
        {
            var delay = ComputeAnnounceDelay(whenUtc, DateTime.UtcNow, clampToImmediate);
            if (delay is null)
            {
                return;
            }

            try
            {
                await Task.Delay(delay.Value, cancellationToken);
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
