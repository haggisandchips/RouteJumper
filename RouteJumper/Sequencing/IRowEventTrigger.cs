namespace RouteJumper.Sequencing
{
    /// <summary>
    /// A trigger that names WHICH row an event applies to (by System text), rather than always
    /// meaning "advance to the next row in order". A single event can bring the whole route up
    /// to date in one go - e.g. after the app restarts mid-journey and several rows need to be
    /// caught up at once.
    /// </summary>
    public interface IRowEventTrigger
    {
        /// <summary>Raised whenever a row-addressable event occurs.</summary>
        event EventHandler<RowEvent>? RowTriggered;
    }
}
