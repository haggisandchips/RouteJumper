namespace RouteJumper.Models
{
    /// <summary>
    /// A single named, persisted macro (SPEC §6.3) - the script text itself, plus enough about
    /// the instance it was recorded against to decide whether Play should still be offered
    /// (SourceProcessId is only valid for as long as that process keeps running; it's not
    /// re-derived or matched by commander name the way Captain/Engineer role restoration is,
    /// since a macro's recorded window-relative coordinates are meaningless against a different
    /// process entirely - SourceCommanderName is kept purely for display).
    /// </summary>
    public class RecordedMacro
    {
        public string Name { get; set; } = string.Empty;

        public string ScriptText { get; set; } = string.Empty;

        public int SourceProcessId { get; set; }

        public string SourceCommanderName { get; set; } = string.Empty;

        public DateTime RecordedAtUtc { get; set; }
    }
}
