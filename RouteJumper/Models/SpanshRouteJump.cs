namespace RouteJumper.Models
{
    /// <summary>
    /// One hop of a route calculated by Spansh (the Spansh menu - $.result.jumps in
    /// SpanshRouteService.GetJobResultAsync/GetGenericJobResultAsync's response, or
    /// $.result.system_jumps in GetNeutronJobResultAsync's). Id64 is the system's real, stable
    /// Elite Dangerous system address - kept (and cached, see
    /// IStarSystemLookupService.SeedSystemAddress) even though nothing currently displays it,
    /// the same "as we go" caching principle NavRoute.json/FSDTarget already seed it from.
    ///
    /// <paramref name="Jumps"/> (Neutron Plotter only - ordinary hops since the previous
    /// waypoint) and <paramref name="MustRefuel"/>/<paramref name="MustInject"/>/
    /// <paramref name="HasNeutron"/> (Galaxy Plotter only) are trailing optional fields so the
    /// Fleet Carrier tab's own jumps (which carry none of them) keep constructing this the same
    /// way as before - see RouteViewModel.ImportFromSpansh/RouteType for how they reach the
    /// Route table's own extra columns.
    /// </summary>
    public readonly record struct SpanshRouteJump(
        long Id64,
        string Name,
        double X,
        double Y,
        double Z,
        int? Jumps = null,
        bool? MustRefuel = null,
        bool? MustInject = null,
        bool? HasNeutron = null)
    {
        public GalacticCoordinates Coordinates => new(X, Y, Z);
    }
}
