namespace RouteJumper.Services.Logging
{
    /// <summary>
    /// App-wide logging facade - every call is a cheap, non-blocking enqueue onto the background
    /// FileLogSink (see that class) plus a synchronous EntryLogged event, never any disk I/O on
    /// the calling thread. Deliberately static: logging is a cross-cutting concern needed from
    /// services, journal watchers (background threads), and ViewModels alike, none of which have
    /// (or should need) a constructor-injected logger reference for this. Initialize is called
    /// once at startup (App.xaml.cs); every Log.* call before that is a silent no-op for the file
    /// sink (EntryLogged still fires, so a Logs window opened before Initialize - not a real
    /// scenario in practice - would still work).
    /// </summary>
    public static class Log
    {
        private static FileLogSink? _sink;

        /// <summary>Raised synchronously on the calling thread for every logged entry - LogsWindow marshals this onto its own Dispatcher, since it can fire from any background thread (journal watchers, HTTP calls, ...).</summary>
        public static event EventHandler<LogEntry>? EntryLogged;

        public static void Initialize(FileLogSink sink) => _sink = sink;

        public static void Debug(string category, string message) => Write(LogLevel.Debug, category, message);

        public static void Info(string category, string message) => Write(LogLevel.Info, category, message);

        public static void Warn(string category, string message) => Write(LogLevel.Warn, category, message);

        public static void Warn(string category, string message, Exception exception) =>
            Write(LogLevel.Warn, category, $"{message} ({exception.GetType().Name}: {exception.Message})");

        public static void Error(string category, string message) => Write(LogLevel.Error, category, message);

        public static void Error(string category, string message, Exception exception) =>
            Write(LogLevel.Error, category, $"{message} ({exception.GetType().Name}: {exception.Message})");

        private static void Write(LogLevel level, string category, string message)
        {
            var entry = new LogEntry(DateTime.Now, level, category, message);
            _sink?.Enqueue(entry);
            EntryLogged?.Invoke(null, entry);
        }
    }
}
