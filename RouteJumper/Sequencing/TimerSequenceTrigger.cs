using System.Windows.Threading;

namespace RouteJumper.Sequencing
{
    /// <summary>
    /// Default trigger: fires every <see cref="Interval"/> (2 seconds by default) on the
    /// UI dispatcher, so it's safe to update bound ViewModel properties directly from it.
    /// </summary>
    public class TimerSequenceTrigger : ISequenceTrigger
    {
        private readonly DispatcherTimer _timer;

        public TimerSequenceTrigger(TimeSpan? interval = null)
        {
            _timer = new DispatcherTimer
            {
                Interval = interval ?? TimeSpan.FromSeconds(2)
            };
            _timer.Tick += OnTick;
        }

        public event EventHandler? Triggered;

        public void Start() => _timer.Start();

        public void Stop() => _timer.Stop();

        private void OnTick(object? sender, EventArgs e) => Triggered?.Invoke(this, EventArgs.Empty);
    }
}
