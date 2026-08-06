using RouteJumper.Models;
using RouteJumper.ViewModels;

namespace RouteJumper.Sequencing
{
    /// <summary>
    /// Builds the flat list of actions for the whole table (icon changes + status changes,
    /// row by row) and executes one action every time the supplied <see cref="ISequenceTrigger"/>
    /// fires. Nothing in here is hardcoded to "a timer" - it just reacts to events - so the
    /// same class works whether actions are paced by a 2-second timer, a manual trigger,
    /// or several triggers wired in at once.
    /// </summary>
    public class RouteSequencer
    {
        private readonly List<ISequenceTrigger> _triggers = new();
        private Queue<SequenceStep> _steps = new();

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
        /// Builds the ordered action plan described in the spec:
        /// add triangle to row 1, then for each row in turn:
        ///   Plotting -> Plotted -> Jumping -> triangle becomes tick ->
        ///   triangle added to next row (if any) -> Cooldown -> status cleared.
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

                steps.Enqueue(new SequenceStep($"Row {row.Number}: Plotting", () => row.Status = "Plotting"));
                steps.Enqueue(new SequenceStep($"Row {row.Number}: Plotted", () => row.Status = "Plotted"));
                steps.Enqueue(new SequenceStep($"Row {row.Number}: Jumping", () => row.Status = "Jumping"));
                steps.Enqueue(new SequenceStep($"Row {row.Number}: complete icon", () => row.Icon = RowIcon.Complete));

                if (i + 1 < rows.Count)
                {
                    var nextRow = rows[i + 1];
                    steps.Enqueue(new SequenceStep(
                        $"Row {nextRow.Number}: show in-progress icon",
                        () => nextRow.Icon = RowIcon.InProgress));
                }

                steps.Enqueue(new SequenceStep($"Row {row.Number}: Cooldown", () => row.Status = "Cooldown"));
                steps.Enqueue(new SequenceStep($"Row {row.Number}: clear status", () => row.Status = string.Empty));
            }

            return steps;
        }
    }
}
