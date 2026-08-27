namespace RouteJumper.Services.Companion
{
    /// <summary>
    /// The four kinds of event the companion site (SPEC §13) shows in its live feed. Deliberately
    /// its own enum, not RowEventKind - there's no 1:1 mapping (Refueled/Panic have no
    /// RowEventKind equivalent at all, and most RowEventKinds - Targeted/Jumping/CooldownElapsed/
    /// Reset/... - have nothing worth showing here).
    /// </summary>
    public enum CompanionEventKind
    {
        /// <summary>A jump was plotted (RowEventKind.Plotted).</summary>
        Plotted,

        /// <summary>The carrier arrived at a system (RowEventKind.Arrived).</summary>
        Arrived,

        /// <summary>The Engineer's refuel macro completed and a fresh deposit was confirmed (AutoPilotController.EngineerRefuelSucceeded).</summary>
        Refueled,

        /// <summary>Auto Pilot's "panic mode" stopped the run (AutoPilotController.PanicOccurred).</summary>
        Panic
    }
}
