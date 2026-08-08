namespace RouteJumper.Sequencing
{
    /// <summary>
    /// Which single-step route action a row-addressable event (see <see cref="RowEvent"/>)
    /// maps to. Deliberately named after the route's own vocabulary (Status values), not after
    /// any journal event.
    ///
    /// Plotted/Arrived are raised the instant their real-world event is observed. Jumping/
    /// CooldownElapsed are *derived* - raised only once the real-world clock reaches a time
    /// computed from a Plotted/Arrived event (3 minutes before DepartureTime, and 4 minutes
    /// after Arrived - itself 1 minute after the carrier's own arrival timestamp - respectively)
    /// - but are still real events by the time they reach RouteSequencer: whoever raises them
    /// (see CarrierRouteJournalWatcher) is responsible for only doing so once that time has
    /// actually arrived, immediately if it's already past. RouteSequencer itself has no notion
    /// of "later" - it only ever reacts to "now".
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
        /// The composite "arrived" step: targeted row's icon -> Complete, Status ->
        /// *(cleared)*; if a next row exists, that row's icon -> InProgress, and - only if
        /// <see cref="RowEvent.IsLive"/> is true - *its* Status -> "Cooldown". Cooldown is
        /// deliberately never set while replaying a journal's history (e.g. the catch-up a fresh
        /// Captain assignment does) - only for a genuinely live-observed arrival, since a row
        /// merely being caught up to "already arrived" has no cooldown that can be shown
        /// reliably after the fact. Nothing is put into Cooldown at all if there's no next row.
        /// Raised 1 minute after the carrier's own CarrierLocation timestamp, not immediately on
        /// that event.
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
        /// Not row-targeted - applies to every row: Icon -> None, Status -> *(cleared)*. Raised
        /// when the Captain role is (re)assigned to an instance, so a full journal replay starts
        /// from a clean slate rather than layering onto whatever state a previous Captain left
        /// behind. The event's SystemName is meaningless for this kind and should be
        /// ignored/empty.
        /// </summary>
        Reset,

        /// <summary>
        /// Not a route-mutating event at all - RouteSequencer ignores it entirely (see
        /// RouteSequencer.ApplyRowEvent). Raised the instant a CarrierLocation line for the
        /// tracked carrier is picked up via live journal tailing (the FileSystemWatcher path) -
        /// never during the one-off historical replay a fresh Captain assignment does, and never
        /// for the passive session-start snapshot (same _hasSeenJumpRequest gate as
        /// Arrived/CooldownElapsed). The event's SystemName is the system the carrier just
        /// arrived at. RouteViewModel listens for this directly (alongside, not instead of,
        /// RouteSequencer's own subscription to the same trigger) to drive "Auto Copy To
        /// Clipboard": if enabled, it copies the *next* row's System text to the clipboard -
        /// deliberately ahead of the delayed Arrived/Cooldown UI transition, since the point is
        /// to have the next system ready to paste immediately.
        /// </summary>
        LiveCarrierLocation,

        /// <summary>
        /// Raised the instant a CarrierJumpCancelled event for the tracked carrier is observed
        /// (live or replayed). Not name-targeted - CarrierJumpCancelled carries no SystemName of
        /// its own, so this reverts whichever row is currently the one in-progress row with
        /// Status "Plotted" or "Jumping" back to a blank status, leaving its Icon as InProgress
        /// so it's ready for a fresh CarrierJumpRequest. See RouteSequencer.ApplyJumpCancelled.
        /// The event's SystemName is meaningless for this kind and should be ignored/empty.
        /// </summary>
        JumpCancelled
    }
}
