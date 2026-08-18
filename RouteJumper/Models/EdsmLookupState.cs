namespace RouteJumper.Models
{
    /// <summary>
    /// Tri-state resolution status for one row's own EDSM lookup (coordinates or star type) -
    /// drives the "Plot needed"/"Target needed" placeholder text shown in place of a blank
    /// Distance/Star Type cell (SPEC §4.9). Purely a UI signal, recomputed fresh by
    /// RouteRowEnrichmentService on every completed enrichment pass (Save, RefreshEnrichment) -
    /// never persisted, matching the fact that an unresolved name itself is never cached to
    /// SQLite and is retried on the next Save/restore (SPEC §4.9/§7).
    /// </summary>
    public enum EdsmLookupState
    {
        /// <summary>No completed pass has covered this row's own system yet - still in flight, or Save/restore hasn't run yet. Default value.</summary>
        Resolving = 0,

        /// <summary>The most recently completed pass found this row's own system's data.</summary>
        Resolved,

        /// <summary>The most recently completed pass could not find this row's own system's data on EDSM (or the lookup itself failed) - won't be retried until the next Save/RefreshEnrichment.</summary>
        Unavailable
    }
}
