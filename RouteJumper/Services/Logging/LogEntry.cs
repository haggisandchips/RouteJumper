namespace RouteJumper.Services.Logging
{
    /// <summary>One logged line - carries its own local timestamp (formatted for both the on-disk file and the live Logs window, so the two always agree) rather than either one deriving it independently.</summary>
    public readonly record struct LogEntry(DateTime TimestampLocal, LogLevel Level, string Category, string Message)
    {
        public string Format() =>
            $"{TimestampLocal:yyyy-MM-dd HH:mm:ss.fff} [{LevelText}] {Category}: {Message}";

        private string LevelText => Level switch
        {
            LogLevel.Debug => "DEBUG",
            LogLevel.Info => "INFO ",
            LogLevel.Warn => "WARN ",
            LogLevel.Error => "ERROR",
            _ => "?    ",
        };
    }
}
