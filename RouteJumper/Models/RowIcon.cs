namespace RouteJumper.Models
{
    /// <summary>
    /// State of the icon shown in the (blank-headed) first column of the Route table.
    /// </summary>
    public enum RowIcon
    {
        /// <summary>No icon shown.</summary>
        None,

        /// <summary>Green triangle pointing right - row is currently active/in progress.</summary>
        InProgress,

        /// <summary>Green tick - row has completed its jump sequence.</summary>
        Complete
    }
}
