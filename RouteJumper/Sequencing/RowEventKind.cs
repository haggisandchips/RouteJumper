namespace RouteJumper.Sequencing
{
    /// <summary>
    /// Which single-step route action a row-addressable event (see <see cref="RowEvent"/>)
    /// maps to. Deliberately named after the route's own vocabulary (Status values), not
    /// after any journal event - the sequencer must not know where the event came from.
    ///
    /// Plotted/Arrived are raised the instant their real-world event is observed. Jumping/
    /// CooldownElapsed are *derived* - raised only once the real-world clock reaches a time
    /// computed from a Plotted/Arrived event (DepartureTime, and 5 minutes after arrival,
    /// respectively - see SPEC §11.5) - but are still real events by the time they reach
    /// RouteSequencer: whoever raises them (see CarrierRouteJournalWatcher) is responsible for
    /// only doing so once that time has actually arrived, immediately if it's already past.
    /// RouteSequencer itself has no notion of "later" - it only ever reacts to "now".
    /// </summary>
    public enum RowEventKind
    {
        /// <summary>Status = "Plotted" for the targeted row.</summary>
        Plotted,

        /// <summary>
        /// Derived from Plotted: Status = "Jumping" for the targeted row, once its
        /// carrier's DepartureTime is reached.
        /// </summary>
        Jumping,

        /// <summary>
        /// The composite "arrived" step (see §7.2.2.4): targeted row's icon -> Complete,
        /// next row's icon -> InProgress, Status -> "Cooldown".
        /// </summary>
        Arrived,

        /// <summary>
        /// Derived from Arrived: Status -> *(cleared)* for the targeted row, once its
        /// 5-minute cooldown period has elapsed.
        /// </summary>
        CooldownElapsed,

        /// <summary>
        /// Not row-targeted - applies to every row: Icon -> None, Status -> *(cleared)*.
        /// Raised when the Captain role is (re)assigned to an instance (see SPEC §11.5), so a
        /// full journal replay starts from a clean slate rather than layering onto whatever
        /// state a previous Captain (or a manual demo run) left behind. The event's SystemName
        /// is meaningless for this kind and should be ignored/empty.
        /// </summary>
        Reset
    }
}
