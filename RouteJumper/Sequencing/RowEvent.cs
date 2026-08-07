namespace RouteJumper.Sequencing
{
    /// <summary>
    /// A single row-addressable event: identifies which row it applies to by the route's own
    /// System text (not by row index/position), since real-world event sources (e.g. a
    /// carrier's journal) know the system name, not the table position. See SPEC §13.1.
    /// </summary>
    public sealed class RowEvent : EventArgs
    {
        public RowEvent(RowEventKind kind, string systemName)
        {
            Kind = kind;
            SystemName = systemName;
        }

        public RowEventKind Kind { get; }

        public string SystemName { get; }
    }
}
