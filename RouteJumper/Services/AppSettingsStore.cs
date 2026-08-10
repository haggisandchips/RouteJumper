using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace RouteJumper.Services
{
    /// <summary>
    /// Persistent key-value store backed by SQLite, in %LocalAppData%\EDFCAutoPilot - the
    /// standard per-user, non-roaming location for this kind of local application state
    /// (settings/cache, not user documents, and not meant to follow the user across machines
    /// via roaming profiles).
    ///
    /// Deliberately a generic Key/Value table rather than a dedicated table per setting - what's
    /// persisted today (route text, window bounds, Captain/Engineer FIDs) is a first instance,
    /// not the full/final list, so new values can be added later without a schema migration.
    /// Every operation opens and closes its own short-lived connection (SQLite handles that
    /// cheaply, and this class is only ever used for small, infrequent reads/writes - a handful
    /// of settings, not a data-heavy workload) rather than holding one connection open for the
    /// app's lifetime.
    /// </summary>
    public class AppSettingsStore
    {
        private readonly string _connectionString;

        public AppSettingsStore() : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EDFCAutoPilot"))
        {
        }

        /// <summary>Test-only seam: lets RouteJumper.Tests point the store at a temp directory instead of the real per-user AppData location.</summary>
        internal AppSettingsStore(string directory)
        {
            Directory.CreateDirectory(directory);

            _connectionString = $"Data Source={Path.Combine(directory, "routejumper.db")}";

            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE IF NOT EXISTS Settings (Key TEXT PRIMARY KEY, Value TEXT NOT NULL);";
                command.ExecuteNonQuery();
            }
            catch (SqliteException)
            {
                // Persistence is a nice-to-have, not core functionality - if the store can't
                // even be opened/created (e.g. a permissions issue), every Get falls back to
                // "nothing persisted" and every Set silently no-ops, rather than the app failing
                // to start over it.
            }
        }

        public string? GetString(string key)
        {
            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT Value FROM Settings WHERE Key = $key;";
                command.Parameters.AddWithValue("$key", key);
                return command.ExecuteScalar() as string;
            }
            catch (SqliteException)
            {
                return null;
            }
        }

        public void SetString(string key, string value)
        {
            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "INSERT INTO Settings (Key, Value) VALUES ($key, $value) " +
                    "ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;";
                command.Parameters.AddWithValue("$key", key);
                command.Parameters.AddWithValue("$value", value);
                command.ExecuteNonQuery();
            }
            catch (SqliteException)
            {
            }
        }

        public double? GetDouble(string key) =>
            double.TryParse(GetString(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;

        public void SetDouble(string key, double value) =>
            SetString(key, value.ToString(CultureInfo.InvariantCulture));

        public bool GetBool(string key, bool defaultValue = false) =>
            GetString(key) is { } text ? text == "1" : defaultValue;

        public void SetBool(string key, bool value) => SetString(key, value ? "1" : "0");

        private SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection(_connectionString);
            connection.Open();
            return connection;
        }
    }
}
