namespace RouteJumper.Models
{
    /// <summary>Result of RouteViewModel.ImportFromNavRoute - see its own doc comment.</summary>
    public enum NavRouteImportOutcome
    {
        /// <summary>RouteText was populated from NavRoute.json's plotted route and the tab returned to Edit state, ready for Save.</summary>
        Success,

        /// <summary>NavRoute.json (in the configured journal folder) is missing, unreadable, or describes no route beyond wherever the CMDR already was when it was written (nothing to import).</summary>
        NoRoutePlotted
    }
}
