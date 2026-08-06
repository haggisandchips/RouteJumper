namespace RouteJumper.Sequencing
{
    /// <summary>
    /// Anything that can raise a "move to the next action" event.
    ///
    /// The RouteSequencer does not know or care WHERE the trigger comes from - it just
    /// advances one step every time Triggered fires. This is what makes each action in
    /// the sequence independently triggerable: today the trigger is a fast timer,
    /// but it could equally be a button click, an incoming message from hardware,
    /// a signal from another part of the app, etc. Multiple triggers can even be wired
    /// up to the same sequencer (e.g. a timer AND a manual "skip" button).
    /// </summary>
    public interface ISequenceTrigger
    {
        /// <summary>Raised whenever this trigger decides the next action should run.</summary>
        event EventHandler? Triggered;

        /// <summary>Arm/start the trigger (e.g. start the timer).</summary>
        void Start();

        /// <summary>Disarm/stop the trigger.</summary>
        void Stop();
    }
}
