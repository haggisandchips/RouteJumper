namespace RouteJumper.Sequencing
{
    /// <summary>
    /// Which single-step route action a row-addressable event (see <see cref="RowEvent"/>)
    /// maps to. Deliberately named after the route's own vocabulary (Status values), not
    /// after any journal event - the sequencer must not know where the event came from.
    ///
    /// Plotted/Arrived are raised the instant their real-world event is observed. Jumping/
    /// CooldownElapsed are *derived* - raised only once the real-world clock reaches a time
    /// computed from a Plotted/Arrived event (3 minutes before DepartureTime, and 4 minutes
    /// after Arrived - itself 1 minute after the carrier's own arrival timestamp - respectively;
    /// see SPEC §11.5) - but are still real events by the time they reach
    /// RouteSequencer: whoever raises them (see CarrierRouteJournalWatcher) is responsible for
    /// only doing so once that time has actually arrived, immediately if it's already past.
    /// RouteSequencer itself has no notion of "later" - it only ever reacts to "now".
    /// </summary>
    public enum RowEventKind
    {
        /// <summary>Status = "Plotted" for the targeted row.</summary>
        Plotted,

        /// <summary>
        /// Derived from Plotted: Status = "Jumping" for the targeted row, once 3 minutes
        /// before its carrier's DepartureTime is reached.
        /// </summary>
        Jumping,

        /// <summary>
        /// The composite "arrived" step (see §7.2.2.4): targeted row's icon -> Complete,
        /// Status -> *(cleared)*; if a next row exists, that row's icon -> InProgress and
        /// *its* Status -> "Cooldown" - the cooldown belongs to the row waiting on it, not the
        /// row that just finished (see SPEC §7.2's Update). Nothing is put into Cooldown if
        /// there's no next row. Raised 1 minute after the carrier's own CarrierLocation
        /// timestamp, not immediately on that event.
        /// </summary>
        Arrived,

        /// <summary>
        /// Derived from Arrived: clears Status on the row *after* the one this event's
        /// SystemName names (i.e. the same row Arrived put into Cooldown - see
        /// RouteSequencer.ApplyCooldownElapsed), once a further 4-minute cooldown period has
        /// elapsed since Arrived (5 minutes total since the carrier's own CarrierLocation
        /// timestamp). A no-op if there was no next row for Arrived to have set Cooldown on.
        /// </summary>
        CooldownElapsed,

        /// <summary>
        /// Not row-targeted - applies to every row: Icon -> None, Status -> *(cleared)*.
        /// Raised when the Captain role is (re)assigned to an instance (see SPEC §11.5), so a
        /// full journal replay starts from a clean slate rather than layering onto whatever
        /// state a previous Captain (or a manual demo run) left behind. The event's SystemName
        /// is meaningless for this kind and should be ignored/empty.
        /// </summary>
        Reset,

        /// <summary>
        /// Not a route-mutating event at all - RouteSequencer ignores it entirely (see
        /// RouteSequencer.ApplyRowEvent). Raised the instant a CarrierLocation line for the
        /// tracked carrier is picked up via live journal tailing (the FileSystemWatcher path)
        /// - never during the one-off historical replay a fresh Captain assignment does, and
        /// never for the passive session-start snapshot (same _hasSeenJumpRequest gate as
        /// Arrived/CooldownElapsed - see SPEC §11.5). The event's SystemName is the system the
        /// carrier just arrived at. RouteViewModel listens for this directly (alongside, not
        /// instead of, RouteSequencer's own subscription to the same trigger) to drive the
        /// "Auto Copy To Clipboard" feature (SPEC §5.6): if enabled, it copies the *next* row's
        /// System text to the clipboard - deliberately ahead of the delayed Arrived/Cooldown UI
        /// transition, since the point is to have the next system ready to paste immediately.
        /// </summary>
        LiveCarrierLocation
    }
}
