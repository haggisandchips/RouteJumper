using System.IO;
using RouteJumper.Services.Logging;

namespace RouteJumper.Services
{
    /// <summary>
    /// Reads `routejumper.conf` - a plain-text, one-"Key=Value"-per-line config file beside the
    /// SQLite settings database in <see cref="AppPaths.DataDirectory"/> - creating it with default
    /// values the first time it's read, if it doesn't exist yet. Deliberately separate from
    /// `AppSettingsStore` (`routejumper.db`): this holds configuration a person might reasonably
    /// want to hand-edit in a text editor (e.g. to point at a non-default journal folder), not
    /// internal app state like route text or window bounds.
    ///
    /// Every read opens and re-parses the file fresh - no caching - the same short-lived-access
    /// pattern `AppSettingsStore` already uses for its own connections. This means a config file
    /// hand-edited while the app is running takes effect on the very next read (e.g. the next
    /// Roles tab refresh), with no restart required.
    /// </summary>
    public class AppConfigStore
    {
        private const string JournalDirectoryKey = "JournalDirectory";
        private const string LogRetentionDaysKey = "LogRetentionDays";
        private const string LogMaxFileSizeMbKey = "LogMaxFileSizeMB";
        private const string LogMaxTotalSizeMbKey = "LogMaxTotalSizeMB";
        private const string EdsmUnresolvedRetryHoursKey = "EdsmUnresolvedRetryHours";

        private const int DefaultLogRetentionDays = 7;
        private const int DefaultLogMaxFileSizeMb = 10;
        private const int DefaultLogMaxTotalSizeMb = 100;
        private const int DefaultEdsmUnresolvedRetryHours = 12;

        private readonly string _configPath;

        public AppConfigStore() : this(AppPaths.DataDirectory)
        {
        }

        /// <summary>Test-only seam: lets RouteJumper.Tests point the store at a temp directory instead of the real per-user AppData location.</summary>
        internal AppConfigStore(string directory)
        {
            Directory.CreateDirectory(directory);

            _configPath = Path.Combine(directory, "routejumper.conf");
        }

        /// <summary>
        /// The folder ED:FC Auto Pilot scans for Elite Dangerous journal files (`Journal.*.log`).
        /// Defaults to Frontier's own standard location, written into a freshly-created
        /// `routejumper.conf` the first time this is read if the file doesn't exist yet; blank
        /// or missing thereafter also falls back to the same default, so a file that exists but
        /// was hand-edited down to nothing doesn't break scanning.
        /// </summary>
        public string JournalDirectory
        {
            get
            {
                var values = ReadOrCreateDefault();
                return values.TryGetValue(JournalDirectoryKey, out var path) && !string.IsNullOrWhiteSpace(path)
                    ? path
                    : DefaultJournalDirectory;
            }
        }

        private static string DefaultJournalDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Saved Games", "Frontier Developments", "Elite Dangerous");

        /// <summary>How many days' worth of Logs\routejumper-*.log files FileLogSink keeps before deleting them - hand-editable here like JournalDirectory, "can revisit these later" defaults per SPEC.</summary>
        public int LogRetentionDays => ReadIntOrDefault(LogRetentionDaysKey, DefaultLogRetentionDays);

        /// <summary>Per-file size cap (MB) before FileLogSink rolls to a new segment for the same day.</summary>
        public int LogMaxFileSizeMb => ReadIntOrDefault(LogMaxFileSizeMbKey, DefaultLogMaxFileSizeMb);

        /// <summary>Total size cap (MB) across every log file - FileLogSink deletes the oldest files (never the one currently being written to) once exceeded.</summary>
        public int LogMaxTotalSizeMb => ReadIntOrDefault(LogMaxTotalSizeMbKey, DefaultLogMaxTotalSizeMb);

        /// <summary>
        /// How long EdsmStarSystemLookupService waits before retrying a system whose most recent
        /// EDSM lookup came back genuinely unresolved (EDSM confirmed it has no record - not a
        /// transient network/HTTP failure, which is always retried immediately) - across restarts,
        /// via EdsmLookupAttemptStore, not just within one running session. EDSM's crowdsourced
        /// coverage is very unlikely to change within a matter of hours, so this exists purely to
        /// cut down on pointless repeat requests, not to ever permanently give up on a system.
        /// </summary>
        public int EdsmUnresolvedRetryHours => ReadIntOrDefault(EdsmUnresolvedRetryHoursKey, DefaultEdsmUnresolvedRetryHours);

        private int ReadIntOrDefault(string key, int defaultValue)
        {
            var values = ReadOrCreateDefault();
            return values.TryGetValue(key, out var raw) && int.TryParse(raw, out var parsed) && parsed > 0
                ? parsed
                : defaultValue;
        }

        private Dictionary<string, string> ReadOrCreateDefault()
        {
            if (!File.Exists(_configPath))
            {
                // Persistence is a nice-to-have, not core functionality - same
                // degrades-to-nothing-persisted philosophy AppSettingsStore uses for its own
                // I/O failures, rather than the app failing to start over it.
                try
                {
                    File.WriteAllLines(_configPath, new[]
                    {
                        "# ED:FC Auto Pilot configuration - safe to hand-edit while the app is closed",
                        "# (or running - config is re-read on every Roles tab refresh).",
                        $"{JournalDirectoryKey}={DefaultJournalDirectory}",
                        "# Log housekeeping (Logs\\routejumper-*.log) - takes effect within the next 30",
                        "# minutes, or on the next log file rollover, without a restart required.",
                        $"{LogRetentionDaysKey}={DefaultLogRetentionDays}",
                        $"{LogMaxFileSizeMbKey}={DefaultLogMaxFileSizeMb}",
                        $"{LogMaxTotalSizeMbKey}={DefaultLogMaxTotalSizeMb}",
                        "# Hours to wait before retrying a system EDSM had no record of last time.",
                        $"{EdsmUnresolvedRetryHoursKey}={DefaultEdsmUnresolvedRetryHours}"
                    });
                }
                catch (IOException ex)
                {
                    Log.Warn("Config", "Could not create routejumper.conf - falling back to defaults.", ex);
                }
                catch (UnauthorizedAccessException ex)
                {
                    Log.Warn("Config", "Could not create routejumper.conf - falling back to defaults.", ex);
                }

                return new Dictionary<string, string> { [JournalDirectoryKey] = DefaultJournalDirectory };
            }

            var values = new Dictionary<string, string>();
            try
            {
                foreach (var line in File.ReadAllLines(_configPath))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                    {
                        continue;
                    }

                    var separatorIndex = trimmed.IndexOf('=');
                    if (separatorIndex < 0)
                    {
                        continue;
                    }

                    values[trimmed[..separatorIndex].Trim()] = trimmed[(separatorIndex + 1)..].Trim();
                }
            }
            catch (IOException ex)
            {
                Log.Warn("Config", "Could not read routejumper.conf - falling back to defaults.", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                Log.Warn("Config", "Could not read routejumper.conf - falling back to defaults.", ex);
            }

            return values;
        }
    }
}
