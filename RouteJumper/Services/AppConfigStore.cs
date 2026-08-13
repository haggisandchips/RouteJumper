using System.IO;

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
        /// The folder RouteJumper scans for Elite Dangerous journal files (`Journal.*.log`).
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
                        "# RouteJumper configuration - safe to hand-edit while the app is closed",
                        "# (or running - config is re-read on every Roles tab refresh).",
                        $"{JournalDirectoryKey}={DefaultJournalDirectory}"
                    });
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
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
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return values;
        }
    }
}
