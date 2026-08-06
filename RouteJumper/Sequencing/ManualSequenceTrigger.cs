namespace RouteJumper.Sequencing
{
    /// <summary>
    /// Example of an alternate trigger: instead of firing on a timer, this fires whenever
    /// something in the app calls Fire() - e.g. a "Next" button, an incoming event from
    /// another module, a hardware callback, a test harness, etc.
    ///
    /// This demonstrates that the RouteSequencer is agnostic to *what* triggers the next
    /// action - swap this in for TimerSequenceTrigger (or combine both) with no changes
    /// to the sequencing or ViewModel code.
    /// </summary>
    public class ManualSequenceTrigger : ISequenceTrigger
    {
        public event EventHandler? Triggered;

        public bool IsArmed { get; private set; }

        public void Start() => IsArmed = true;

        public void Stop() => IsArmed = false;

        /// <summary>Call this from any other event handler to advance the sequence by one step.</summary>
        public void Fire()
        {
            if (IsArmed)
            {
                Triggered?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
