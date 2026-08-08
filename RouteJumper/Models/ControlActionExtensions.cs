namespace RouteJumper.Models
{
    /// <summary>
    /// The token each <see cref="ControlAction"/> is known by in a recorded macro script (SPEC
    /// §6.3) - matches SPEC §6.1's own key-binding table wording exactly (e.g. "PREV_PANEL"), so
    /// the same vocabulary is used everywhere the action is named: the key-binding list, a
    /// script's text, and this mapping.
    /// </summary>
    public static class ControlActionExtensions
    {
        public static string ToActionName(this ControlAction action) => action switch
        {
            ControlAction.Up => "UP",
            ControlAction.Down => "DOWN",
            ControlAction.Left => "LEFT",
            ControlAction.Right => "RIGHT",
            ControlAction.Select => "SELECT",
            ControlAction.PrevPanel => "PREV_PANEL",
            ControlAction.NextPanel => "NEXT_PANEL",
            ControlAction.Exit => "EXIT",
            ControlAction.RightPanel => "RIGHT_PANEL",
            _ => action.ToString().ToUpperInvariant()
        };

        public static bool TryParseActionName(string name, out ControlAction action)
        {
            foreach (var candidate in Enum.GetValues<ControlAction>())
            {
                if (candidate.ToActionName() == name)
                {
                    action = candidate;
                    return true;
                }
            }

            action = default;
            return false;
        }
    }
}
