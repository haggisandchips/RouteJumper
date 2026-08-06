namespace RouteJumper.Sequencing
{
    /// <summary>
    /// One discrete action in the jump sequence (e.g. "set row 3 status to Plotting").
    /// Kept as a named object, rather than a bare Action, so each step can be logged,
    /// inspected, or matched up to a specific trigger if needed later.
    /// </summary>
    public class SequenceStep
    {
        public SequenceStep(string name, Action execute)
        {
            Name = name;
            Execute = execute;
        }

        public string Name { get; }

        public Action Execute { get; }
    }
}
