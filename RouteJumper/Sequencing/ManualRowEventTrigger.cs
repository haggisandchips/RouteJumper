namespace RouteJumper.Sequencing
{
    /// <summary>
    /// Default row-addressable trigger: something else (e.g. a journal watcher) calls
    /// <see cref="Fire"/> whenever it observes a real-world event; this just relays it as a
    /// <see cref="RowTriggered"/> event, the same producer/consumer split as
    /// <see cref="ManualSequenceTrigger"/> uses for the timer-paced sequence.
    /// </summary>
    public class ManualRowEventTrigger : IRowEventTrigger
    {
        public event EventHandler<RowEvent>? RowTriggered;

        public void Fire(RowEventKind kind, string systemName) =>
            RowTriggered?.Invoke(this, new RowEvent(kind, systemName));
    }
}
