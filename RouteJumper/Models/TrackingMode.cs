namespace RouteJumper.Models
{
    /// <summary>
    /// Which kind of vessel RouteJumper is tracking a route's progress against - a Fleet Carrier
    /// (the Roles/Controls tabs, Auto Pilot) or a solo commander's own ship (the Track tab, no
    /// automation). Owned by MainViewModel and persisted; exactly one mode is active at a time.
    /// </summary>
    public enum TrackingMode
    {
        FleetCarrier,
        Ship
    }
}
