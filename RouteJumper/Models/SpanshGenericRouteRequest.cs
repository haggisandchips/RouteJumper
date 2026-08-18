namespace RouteJumper.Models
{
    /// <summary>
    /// Everything the Galaxy Plotter tab needs to POST to Spansh's /api/generic/route (the
    /// Spansh menu - SpanshRouteService.StartGenericRouteAsync). The ship-derived numeric fields
    /// (FuelPower..RangeBoost, SuperchargeMultiplier, InjectionMultiplier) come from
    /// Services/Spansh/ShipBuildDerivation. There is deliberately no ship_build/SLEF field here -
    /// confirmed live that Spansh's own server-side route computation never parses it (a real
    /// request with ship_build set to a bare "{}", and a second with the field omitted entirely,
    /// both queued and computed identically using only the numeric fields below), so building and
    /// sending one would be pure unused overhead. ReserveSize/Cargo are plain trimmed text, not
    /// double - mirrors StartNeutronRouteAsync's own Range/Efficiency (no client-side numeric
    /// validation; Spansh's own response reports what's wrong with anything malformed).
    /// </summary>
    public sealed record SpanshGenericRouteRequest(
        string SourceId,
        string DestinationId,
        bool IsSupercharged,
        bool UseSupercharge,
        bool UseInjections,
        bool UseInjectionsWhenRequired,
        bool ExcludeSecondary,
        bool RefuelEveryScoopable,
        double FuelPower,
        double FuelMultiplier,
        double OptimalMass,
        double BaseMass,
        double TankSize,
        double InternalTankSize,
        string ReserveSize,
        double MaxFuelPerJump,
        double RangeBoost,
        string Cargo,
        string Algorithm,
        int SuperchargeMultiplier,
        int InjectionMultiplier);
}
