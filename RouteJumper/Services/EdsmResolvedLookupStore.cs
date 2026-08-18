using System.IO;
using Microsoft.Data.Sqlite;
using RouteJumper.Services.Logging;

namespace RouteJumper.Services
{
    /// <summary>
    /// Stores resolved EDSM/journal-seeded system lookups (coordinates, star type, system
    /// address - see EdsmStarSystemLookupService's Coords/StarType/SystemAddress "kind"
    /// discriminators) keyed by (Kind, SystemKey).
    ///
    /// Deliberately its own table in `routejumper.db` (not the generic Settings(Key,Value) one
    /// AppSettingsStore owns, which these used to be shoehorned into as prefixed keys like
    /// "EdsmCoords:SOL") - same rationale as EdsmLookupAttemptStore's own UnresolvedLookups
    /// table: this data has a different access pattern (a point lookup keyed by
    /// (Kind, SystemKey) on essentially every route row) that benefits from its own schema, and
    /// the composite primary key is itself the only index needed.
    ///
    /// Same short-lived-connection, best-effort-degrades-to-nothing-persisted philosophy
    /// AppSettingsStore/AppConfigStore/EdsmLookupAttemptStore already use for their own I/O.
    /// </summary>
    public class EdsmResolvedLookupStore
    {
        private const string LogCategory = "Edsm";

        private readonly string _connectionString;

        public EdsmResolvedLookupStore() : this(AppPaths.DataDirectory)
        {
        }

        /// <summary>Test-only seam: lets RouteJumper.Tests point the store at a temp directory instead of the real per-user AppData location.</summary>
        internal EdsmResolvedLookupStore(string directory)
        {
            Directory.CreateDirectory(directory);

            _connectionString = $"Data Source={Path.Combine(directory, "routejumper.db")}";

            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "CREATE TABLE IF NOT EXISTS ResolvedLookups (" +
                    "Kind TEXT NOT NULL, SystemKey TEXT NOT NULL, Value TEXT NOT NULL, " +
                    "PRIMARY KEY (Kind, SystemKey));";
                command.ExecuteNonQuery();
            }
            catch (SqliteException ex)
            {
                Log.Warn(LogCategory, "Could not create/open the ResolvedLookups table - resolved EDSM/journal-seeded lookups will not persist across a restart this session.", ex);
            }
        }

        /// <summary>Null if this (kind, system) has never been resolved, or the read itself failed.</summary>
        public string? GetValue(string kind, string systemKey)
        {
            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT Value FROM ResolvedLookups WHERE Kind = $kind AND SystemKey = $key;";
                command.Parameters.AddWithValue("$kind", kind);
                command.Parameters.AddWithValue("$key", systemKey);

                return command.ExecuteScalar() as string;
            }
            catch (SqliteException ex)
            {
                Log.Warn(LogCategory, $"Failed to read the resolved lookup for {kind}:{systemKey}.", ex);
                return null;
            }
        }

        public void SetValue(string kind, string systemKey, string value)
        {
            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "INSERT INTO ResolvedLookups (Kind, SystemKey, Value) VALUES ($kind, $key, $value) " +
                    "ON CONFLICT(Kind, SystemKey) DO UPDATE SET Value = excluded.Value;";
                command.Parameters.AddWithValue("$kind", kind);
                command.Parameters.AddWithValue("$key", systemKey);
                command.Parameters.AddWithValue("$value", value);
                command.ExecuteNonQuery();
            }
            catch (SqliteException ex)
            {
                Log.Warn(LogCategory, $"Failed to write the resolved lookup for {kind}:{systemKey}.", ex);
            }
        }

        private SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection(_connectionString);
            connection.Open();
            return connection;
        }
    }
}
