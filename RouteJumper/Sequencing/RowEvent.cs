namespace RouteJumper.Sequencing
{
    /// <summary>
    /// A single row-addressable event: identifies which row it applies to by the route's own
    /// System text (not by row index/position), since real-world event sources (e.g. a
    /// carrier's journal) know the system name, not the table position.
    /// </summary>
    public sealed class RowEvent : EventArgs
    {
        public RowEvent(RowEventKind kind, string systemName, bool isLive = false, DateTime? phaseEndUtc = null)
        {
            Kind = kind;
            SystemName = systemName;
            IsLive = isLive;
            PhaseEndUtc = phaseEndUtc;
        }

        public RowEventKind Kind { get; }

        public string SystemName { get; }

        /// <summary>
        /// True only for an event raised from a genuinely live-tailed journal line (a real-time
        /// FileSystemWatcher pickup), never for one raised while replaying a journal's history
        /// (e.g. the one-off catch-up read a fresh Captain assignment does). See
        /// RowEventKind.Arrived for the one place this currently changes behavior.
        /// </summary>
        public bool IsLive { get; }

        /// <summary>
        /// For Plotted/Jumping/Arrived only: the real-world UTC instant this event's resulting
        /// Status will itself end - i.e. when the *next* transition (Jumping, the eventual
        /// arrival, or Cooldown clearing) is actually scheduled to fire. Null if unknown (a
        /// journal timestamp couldn't be parsed) or not meaningful for this Kind. Purely cosmetic
        /// - RouteSequencer copies it onto RouteRowViewModel.PhaseEndUtc for the Route tab's
        /// Status-column countdown progress bar (§4.4); it plays no part in the route-progress
        /// state machine itself, which never reads it.
        /// </summary>
        public DateTime? PhaseEndUtc { get; }
    }
}
