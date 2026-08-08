namespace RouteJumper.Models
{
    /// <summary>
    /// The fixed set of named actions a key can be bound to on the Controls tab (SPEC §6.1).
    /// Deliberately a small, closed set matching Elite Dangerous's own panel-navigation
    /// vocabulary, not an open-ended list - these are what a recorded macro's script refers to
    /// by name (see MacroScriptBuilder/MacroPlayer) instead of a raw key, so a script stays
    /// readable and keeps working if the user later rebinds a key.
    /// </summary>
    public enum ControlAction
    {
        Up,
        Down,
        Left,
        Right,
        Select,
        PrevPanel,
        NextPanel,
        Exit,
        RightPanel
    }
}
