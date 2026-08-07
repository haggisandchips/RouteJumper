namespace RouteJumper.Services
{
    /// <summary>
    /// Shared by anything that reads Elite Dangerous journal lines (the instance scanner, the
    /// Captain journal watcher): pulls the "event" value out of a raw line without a full JSON
    /// parse, so lines for event types the caller doesn't care about are skipped cheaply.
    /// </summary>
    internal static class JournalEventName
    {
        public static string? Extract(string line)
        {
            const string marker = "\"event\":\"";
            var start = line.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
            {
                return null;
            }

            start += marker.Length;
            var end = line.IndexOf('"', start);
            return end < 0 ? null : line[start..end];
        }
    }
}
