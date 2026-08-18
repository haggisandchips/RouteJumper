namespace RouteJumper.Models
{
    /// <summary>
    /// One hop of a fleet-carrier route calculated by Spansh (the Spansh menu -
    /// $.result.jumps in SpanshRouteService.GetJobResultAsync's response). Id64 is the system's
    /// real, stable Elite Dangerous system address - kept (and cached, see
    /// IStarSystemLookupService.SeedSystemAddress) even though nothing currently displays it,
    /// the same "as we go" caching principle NavRoute.json/FSDTarget already seed it from.
    /// </summary>
    public readonly record struct SpanshRouteJump(long Id64, string Name, double X, double Y, double Z)
    {
        public GalacticCoordinates Coordinates => new(X, Y, Z);
    }
}
