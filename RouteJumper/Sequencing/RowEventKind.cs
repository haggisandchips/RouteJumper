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
        /// <summary>
        /// Status = "Targeted" for the targeted row - Ship mode only (raised by
        /// ShipRouteJournalWatcher off the ship's own FSDTarget event; nothing in Fleet Carrier
        /// mode raises this). Matches like Plotted (any not-yet-complete row, no status
        /// precondition of its own) - but RouteSequencer defers actually applying it while any
        /// row currently shows "Jumping" or "Cooldown", instead of applying it immediately: real
        /// journal data shows the game's own multi-route auto-target behaviour typically fires
        /// the *next* hop's FSDTarget while the *current* hop is still charging or cooling down,
        /// which would otherwise paint the wrong (not-yet-current) row as "Targeted" prematurely.
        /// The deferred system name is applied once CooldownElapsed actually fires (see
        /// RouteSequencer.FlushDeferredTargeted) - by which point the route has settled and the
        /// right row is unambiguous. This also naturally covers a CMDR manually re-targeting
        /// mid-jump out of habit, not just the game's own automatic behaviour.
        ///
        /// If the targeted system doesn't match any row at all - e.g. the CMDR manually targeted
        /// an off-route system, realised it was too far for one jump, and plotted a multi-jump
        /// in-game route to somewhere else instead, whose own first-hop FSDTarget names an
        /// intermediate system the pasted route never mentions - whatever row was previously
        /// showing "Targeted" no longer reflects reality and is cleared back to blank (see
        /// RouteSequencer.ClearAnyTargeted). Its Icon is left alone: that row is still genuinely
        /// the next system the route expects to reach, only the stale "Targeted" label was wrong.
        /// </summary>
        Targeted,

        /// <summary>
        /// Status = "Plotting" for the targeted row - unlike every other kind here, not raised
        /// from anything observed in the journal at all: AutoPilotController raises this itself,
        /// the instant it begins playing the Captain's macro to plot this row's jump, through the
        /// same shared trigger the journal watcher uses (see MainViewModel's wiring). Cleared the
        /// moment the real CarrierJumpRequest actually arrives (Plotted, below, overwrites it).
        /// There is no way to know in advance how long the macro itself, or the game's own
        /// request/acknowledgement, will take, so this status carries no PhaseEndUtc at all -
        /// RouteRowViewModel shows an indeterminate progress cue for it instead of a countdown
        /// (§4.4).
        /// </summary>
        Plotting,

        /// <summary>Status = "Plotted" for the targeted row.</summary>
        Plotted,

        /// <summary>
        /// Fleet Carrier mode: derived from Plotted, Status = "Jumping" for the targeted row once
        /// 3 minutes before its carrier's DepartureTime is reached. Ship mode: raised immediately
        /// off the ship's own StartJump event, with no lead time to derive - real journal data
        /// shows StartJump only ever fires once a jump is already, genuinely underway. Matches a
        /// row already showing "Plotted" (Fleet Carrier) or "Targeted" (Ship, the normal case) as
        /// well as a still-blank row (Ship mode fallback, in case FSDTarget was never observed for
        /// this hop) - see RouteSequencer.FindTargetIndex.
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
        /// Derived from Arrived: clears Status on whichever row is currently showing "Cooldown"
        /// (the row Arrived put into it - see RouteSequencer.ApplyCooldownElapsed, which finds it
        /// by its Cooldown status directly rather than by this event's own SystemName, to stay
        /// correct even if the route revisits the same system name more than once), once a
        /// further 4-minute cooldown period has elapsed since Arrived (5 minutes total since the
        /// carrier's own CarrierLocation timestamp). A no-op if there was no next row for Arrived
        /// to have set Cooldown on, or nothing is currently showing it.
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
        /// never as part of the one-off historical catch-up a fresh Captain assignment does
        /// (CarrierRouteJournalWatcher.ComputeCatchUpState), which applies a single derived
        /// result rather than firing an event per historical line at all. The event's
        /// SystemName is the system the carrier just arrived at. RouteViewModel listens for
        /// this directly (alongside, not instead of,
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
