namespace RouteJumper.Models
{
    /// <summary>Result of RouteViewModel.TrimToJumpRange - see its own doc comment.</summary>
    public enum RouteTrimOutcome
    {
        /// <summary>RouteText was populated with the trimmed row list and the tab returned to Edit state, ready for Save - see RouteTrimResult.RemovedCount for how many rows that dropped.</summary>
        Success,

        /// <summary>No route is currently saved (Rows is empty) - nothing to trim.</summary>
        NoRoute,

        /// <summary>At least one row's own coordinates aren't yet known from EDSM/the local cache, so leg distances can't be computed reliably enough to trim by.</summary>
        CoordinatesUnavailable,

        /// <summary>No Captain is currently assigned (Roles tab) - trimming needs the carrier's own real current location (below) to anchor the first hop efficiently, which requires knowing whose carrier that is.</summary>
        CaptainNotAssigned,

        /// <summary>Captain is assigned, but their carrier's current system isn't known yet this session (CarrierStats/CarrierJump/CarrierLocation not yet observed) - open Carrier Management in-game to establish it.</summary>
        CarrierLocationUnknown
    }

    /// <summary>Outcome plus, for Success, how many rows the trim actually dropped (0 if every row was already within jump range).</summary>
    public readonly record struct RouteTrimResult(RouteTrimOutcome Outcome, int RemovedCount = 0);
}
