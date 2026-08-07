using RouteJumper.Models;
using RouteJumper.ViewModels;

namespace RouteJumper.Sequencing
{
    /// <summary>
    /// Builds the flat list of actions for the whole table (icon changes + status changes,
    /// row by row) and executes one action every time the supplied <see cref="ISequenceTrigger"/>
    /// fires. Nothing in here is hardcoded to "a timer" - it just reacts to events - so the
    /// same class works whether actions are paced by a fast timer, a manual trigger,
    /// or several triggers wired in at once. Note that some actions are intentionally grouped
    /// into a single SequenceStep (see BuildSteps) when they should happen simultaneously
    /// rather than one-per-trigger.
    /// </summary>
    public class RouteSequencer
    {
        private readonly List<ISequenceTrigger> _triggers = new();
        private Queue<SequenceStep> _steps = new();
        private IReadOnlyList<RouteRowViewModel>? _addressableRows;

        public bool IsRunning { get; private set; }

        /// <summary>Raised after each individual step executes (useful for logging/diagnostics).</summary>
        public event EventHandler<SequenceStep>? StepExecuted;

        /// <summary>Raised once every row has finished its full sequence.</summary>
        public event EventHandler? Completed;

        /// <summary>
        /// Wires an additional trigger into this sequencer. Any number of triggers can be
        /// attached; whichever one fires next advances the sequence.
        /// </summary>
        public void AttachTrigger(ISequenceTrigger trigger)
        {
            trigger.Triggered += OnTriggered;
            _triggers.Add(trigger);
        }

        /// <summary>
        /// Wires a row-addressable trigger (see <see cref="IRowEventTrigger"/>) into this
        /// sequencer. Unlike <see cref="AttachTrigger"/>, this does not consume the queued
        /// timer-paced steps at all - it applies directly to whichever rows were last passed
        /// to <see cref="SetRows"/>, independently of whether a timer-paced run is in progress.
        /// </summary>
        public void AttachRowTrigger(IRowEventTrigger trigger) => trigger.RowTriggered += OnRowTriggered;

        /// <summary>
        /// Tells the sequencer which rows row-addressable events should apply to. Independent
        /// of <see cref="Start"/>/<see cref="IsRunning"/> - a row-addressable trigger can bring
        /// the route up to date (e.g. from a real-world event source) whether or not the
        /// timer-paced demo sequence has ever been started.
        /// </summary>
        public void SetRows(IReadOnlyList<RouteRowViewModel> rows) => _addressableRows = rows;

        private void OnRowTriggered(object? sender, RowEvent e)
        {
            if (_addressableRows is null)
            {
                return;
            }

            ApplyRowEvent(_addressableRows, e);
        }

        /// <summary>
        /// Applies a single row-addressable event: finds the row it targets (see
        /// <see cref="FindTargetIndex"/>), and - for Plotted/Arrived only - silently completes
        /// every earlier not-yet-complete row along the way. That catch-up is what lets one
        /// event bring the whole route up to date in a single step - e.g. after the app
        /// restarts mid-journey and several rows must be marked complete at once, rather than
        /// replaying each one individually. See SPEC §11.5/§13.1. Jumping never needs to catch
        /// up earlier rows - by construction it only ever targets the row a prior Plotted event
        /// already brought current. CooldownElapsed is handled entirely separately (see
        /// <see cref="ApplyCooldownElapsed"/>) since - per SPEC §7.2's Update - the Cooldown
        /// status it clears lives on the row *after* the one its SystemName names, not on that
        /// row itself. Reset is not row-targeted at all - it clears every row unconditionally
        /// (see SPEC §11.5's Update on Captain reassignment) and skips the rest of this method
        /// entirely. LiveCarrierLocation is not a route-mutating event at all - RouteSequencer
        /// ignores it completely; RouteViewModel has its own separate subscription to the same
        /// trigger for it (see that value's doc comment and SPEC §5.6).
        /// </summary>
        private static void ApplyRowEvent(IReadOnlyList<RouteRowViewModel> rows, RowEvent e)
        {
            if (e.Kind == RowEventKind.Reset)
            {
                foreach (var eachRow in rows)
                {
                    eachRow.Icon = RowIcon.None;
                    eachRow.Status = string.Empty;
                }
                return;
            }

            if (e.Kind == RowEventKind.LiveCarrierLocation)
            {
                return;
            }

            if (e.Kind == RowEventKind.CooldownElapsed)
            {
                ApplyCooldownElapsed(rows, e.SystemName);
                return;
            }

            var targetIndex = FindTargetIndex(rows, e.Kind, e.SystemName);
            if (targetIndex < 0)
            {
                return;
            }

            if (e.Kind is RowEventKind.Plotted or RowEventKind.Arrived)
            {
                for (var i = 0; i < targetIndex; i++)
                {
                    if (rows[i].Icon != RowIcon.Complete)
                    {
                        rows[i].Icon = RowIcon.Complete;
                        rows[i].Status = string.Empty;
                    }
                }
            }

            var row = rows[targetIndex];
            switch (e.Kind)
            {
                case RowEventKind.Plotted:
                    if (row.Icon != RowIcon.Complete)
                    {
                        row.Icon = RowIcon.InProgress;
                    }
                    row.Status = "Plotted";
                    break;

                case RowEventKind.Jumping:
                    row.Status = "Jumping";
                    break;

                case RowEventKind.Arrived:
                    // Per SPEC §7.2's Update: Cooldown belongs to the row that's actually
                    // waiting on it - the next one - not the row that just finished. The
                    // just-arrived row goes straight to Complete with a blank status; if
                    // there's no next row, nothing is put into Cooldown at all.
                    row.Icon = RowIcon.Complete;
                    row.Status = string.Empty;
                    if (targetIndex + 1 < rows.Count)
                    {
                        var nextRow = rows[targetIndex + 1];
                        nextRow.Icon = RowIcon.InProgress;
                        nextRow.Status = "Cooldown";
                    }
                    break;
            }
        }

        /// <summary>
        /// CooldownElapsed's SystemName names the row the carrier arrived *at* (the same name
        /// Arrived above used) - but the Cooldown status it needs to clear was put on the row
        /// *after* that one, not on the arrived-at row itself (see SPEC §7.2's Update). So this
        /// looks up the (by now Complete) arrived-at row by name first, then clears Cooldown on
        /// the row immediately after it, if that row is still showing it. A safe no-op - same
        /// stale/duplicate-timer tolerance <see cref="FindTargetIndex"/> documents - if no such
        /// row is found, or the row after it has already moved past Cooldown (e.g. a manual
        /// "Set next system" override ran in between).
        /// </summary>
        private static void ApplyCooldownElapsed(IReadOnlyList<RouteRowViewModel> rows, string arrivedSystemName)
        {
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i].Icon != RowIcon.Complete ||
                    !string.Equals(rows[i].SystemText, arrivedSystemName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (i + 1 < rows.Count && rows[i + 1].Icon == RowIcon.InProgress && rows[i + 1].Status == "Cooldown")
                {
                    rows[i + 1].Status = string.Empty;
                }

                return;
            }
        }

        /// <summary>
        /// Finds which row a row-addressable event targets. Plotted/Arrived match by System
        /// text against any not-yet-complete row (any current status - matches the row a
        /// catch-up would otherwise skip past). Jumping is a derived follow-up (see
        /// <see cref="RowEventKind"/>) and is matched more precisely - by System text *and* the
        /// exact status Plotted left behind - so a stale/duplicate timer firing after the row
        /// has already moved on is a safe no-op rather than corrupting a later state.
        /// CooldownElapsed does not use this method at all - see
        /// <see cref="ApplyCooldownElapsed"/>.
        /// </summary>
        private static int FindTargetIndex(IReadOnlyList<RouteRowViewModel> rows, RowEventKind kind, string systemName)
        {
            var requireStatus = kind == RowEventKind.Jumping ? "Plotted" : null;

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.Icon == RowIcon.Complete)
                {
                    continue;
                }

                if (requireStatus != null && row.Status != requireStatus)
                {
                    continue;
                }

                if (string.Equals(row.SystemText, systemName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        public void Start(IReadOnlyList<RouteRowViewModel> rows)
        {
            if (IsRunning)
            {
                return;
            }

            _steps = BuildSteps(rows);
            IsRunning = true;

            // The triangle appears on row 1 immediately, per the spec ("initially adds an
            // icon...") - everything after that is paced by the trigger(s).
            RunNextStepImmediately();

            foreach (var trigger in _triggers)
            {
                trigger.Start();
            }
        }

        public void Stop()
        {
            if (!IsRunning)
            {
                return;
            }

            IsRunning = false;
            foreach (var trigger in _triggers)
            {
                trigger.Stop();
            }
        }

        private void OnTriggered(object? sender, EventArgs e)
        {
            if (!IsRunning)
            {
                return;
            }

            RunNextStepImmediately();
        }

        private void RunNextStepImmediately()
        {
            if (_steps.Count == 0)
            {
                Stop();
                Completed?.Invoke(this, EventArgs.Empty);
                return;
            }

            var step = _steps.Dequeue();
            step.Execute();
            StepExecuted?.Invoke(this, step);

            if (_steps.Count == 0)
            {
                Stop();
                Completed?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Builds the ordered action plan:
        /// add triangle to row 1, then for each row in turn:
        ///   Plotting -> Plotted -> Jumping ->
        ///   [combined: triangle becomes tick + status cleared + (if a next row exists) triangle
        ///   added to next row + Cooldown *on that next row*] ->
        ///   (if a next row exists) that next row's Cooldown status cleared.
        /// The combined-step actions are executed together as a single step/trigger event,
        /// rather than one trigger firing per action. Per SPEC §7.2's Update, Cooldown belongs
        /// to the row that's actually waiting on it (the next one), not the row that just
        /// finished - so the last row's cycle is one tick shorter than the others: there's no
        /// next row to put into Cooldown, so there's nothing to clear afterward either.
        /// </summary>
        private static Queue<SequenceStep> BuildSteps(IReadOnlyList<RouteRowViewModel> rows)
        {
            var steps = new Queue<SequenceStep>();

            if (rows.Count == 0)
            {
                return steps;
            }

            steps.Enqueue(new SequenceStep(
                $"Row {rows[0].Number}: show in-progress icon",
                () => rows[0].Icon = RowIcon.InProgress));

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var nextRow = i + 1 < rows.Count ? rows[i + 1] : null;

                steps.Enqueue(new SequenceStep($"Row {row.Number}: Plotting", () => row.Status = "Plotting"));
                steps.Enqueue(new SequenceStep($"Row {row.Number}: Plotted", () => row.Status = "Plotted"));
                steps.Enqueue(new SequenceStep($"Row {row.Number}: Jumping", () => row.Status = "Jumping"));

                steps.Enqueue(new SequenceStep(
                    $"Row {row.Number}: complete icon" + (nextRow != null ? $" + Row {nextRow.Number}: in-progress icon + Cooldown" : string.Empty),
                    () =>
                    {
                        row.Icon = RowIcon.Complete;
                        row.Status = string.Empty;
                        if (nextRow != null)
                        {
                            nextRow.Icon = RowIcon.InProgress;
                            nextRow.Status = "Cooldown";
                        }
                    }));

                if (nextRow != null)
                {
                    steps.Enqueue(new SequenceStep($"Row {nextRow.Number}: clear Cooldown status", () => nextRow.Status = string.Empty));
                }
            }

            return steps;
        }
    }
}
