namespace RouteJumper.Models
{
    /// <summary>
    /// How the currently-saved route was produced - drives which of the Route table's extra,
    /// Spansh-only columns (RouteViewModel.IsNeutronRoute/IsGalaxyRoute) are shown, and whether
    /// clicking Edit warns that continuing will discard them (RouteView.OnEditClick). Set only by
    /// RouteViewModel.ImportFromSpansh, and reset back to Plain by every Save() - see
    /// RouteViewModel's own doc comments for why Save() is the single choke point for this.
    /// </summary>
    public enum RouteType
    {
        /// <summary>Pasted/typed by hand, imported via Import Current Route, trimmed via Trim for FC, or a Fleet Carrier Spansh import - no extra per-row data.</summary>
        Plain,

        /// <summary>Calculated via Spansh's Neutron Plotter tab - rows carry a Jumps (ordinary hops since the previous waypoint) value.</summary>
        Neutron,

        /// <summary>Calculated via Spansh's Galaxy Plotter tab - rows carry MustRefuel/MustInject/HasNeutron flags.</summary>
        Galaxy
    }
}
