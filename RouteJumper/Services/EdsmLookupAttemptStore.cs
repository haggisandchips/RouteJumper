using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using RouteJumper.Services.Logging;

namespace RouteJumper.Services
{
    /// <summary>
    /// Records the UTC instant of the most recent *unresolved* EDSM lookup attempt for a given
    /// system name and lookup kind (coordinates or star type) - backs
    /// EdsmStarSystemLookupService's own EdsmUnresolvedRetryHours cooldown (AppConfigStore), so a
    /// system EDSM confirmed it has no record for isn't queried again for a while, even across an
    /// app restart. EDSM's crowdsourced coverage is very unlikely to meaningfully change within a
    /// matter of hours, so repeatedly re-querying it for the same still-missing system is wasted
    /// load with essentially no chance of a different answer.
    ///
    /// Deliberately its own table in `routejumper.db` (not the generic Settings(Key,Value) one
    /// AppSettingsStore owns, and not shoehorned into prefixed keys the way the *resolved* EDSM
    /// cache already is) - this data has a different access pattern (a point lookup keyed by
    /// (Kind, SystemKey), written only for failed attempts, read on essentially every lookup that
    /// misses the resolved cache) that benefits from its own schema. The composite primary key
    /// `(Kind, SystemKey)` is itself the only index this needs - every read and write here goes
    /// through it directly, so a separate index would just be redundant overhead.
    ///
    /// Same short-lived-connection, best-effort-degrades-to-nothing-persisted philosophy
    /// AppSettingsStore/AppConfigStore already use for their own I/O.
    /// </summary>
    public class EdsmLookupAttemptStore
    {
        private const string LogCategory = "Edsm";

        private readonly string _connectionString;

        public EdsmLookupAttemptStore() : this(AppPaths.DataDirectory)
        {
        }

        /// <summary>Test-only seam: lets RouteJumper.Tests point the store at a temp directory instead of the real per-user AppData location.</summary>
        internal EdsmLookupAttemptStore(string directory)
        {
            Directory.CreateDirectory(directory);

            _connectionString = $"Data Source={Path.Combine(directory, "routejumper.db")}";

            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "CREATE TABLE IF NOT EXISTS UnresolvedLookups (" +
                    "Kind TEXT NOT NULL, SystemKey TEXT NOT NULL, LastAttemptUtc TEXT NOT NULL, " +
                    "PRIMARY KEY (Kind, SystemKey));";
                command.ExecuteNonQuery();
            }
            catch (SqliteException ex)
            {
                Log.Warn(LogCategory, "Could not create/open the UnresolvedLookups table - the EDSM retry cooldown will not persist across a restart this session.", ex);
            }
        }

        /// <summary>Null if this (kind, system) has never been recorded as unresolved, or the read itself failed.</summary>
        public DateTime? GetLastAttemptUtc(string kind, string systemKey)
        {
            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT LastAttemptUtc FROM UnresolvedLookups WHERE Kind = $kind AND SystemKey = $key;";
                command.Parameters.AddWithValue("$kind", kind);
                command.Parameters.AddWithValue("$key", systemKey);

                var raw = command.ExecuteScalar() as string;
                return raw != null && DateTime.TryParse(
                        raw, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
                    ? parsed
                    : null;
            }
            catch (SqliteException ex)
            {
                Log.Warn(LogCategory, $"Failed to read the EDSM attempt timestamp for {kind}:{systemKey}.", ex);
                return null;
            }
        }

        public void SetLastAttemptUtc(string kind, string systemKey, DateTime utc)
        {
            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "INSERT INTO UnresolvedLookups (Kind, SystemKey, LastAttemptUtc) VALUES ($kind, $key, $ts) " +
                    "ON CONFLICT(Kind, SystemKey) DO UPDATE SET LastAttemptUtc = excluded.LastAttemptUtc;";
                command.Parameters.AddWithValue("$kind", kind);
                command.Parameters.AddWithValue("$key", systemKey);
                command.Parameters.AddWithValue("$ts", utc.ToString("O", CultureInfo.InvariantCulture));
                command.ExecuteNonQuery();
            }
            catch (SqliteException ex)
            {
                Log.Warn(LogCategory, $"Failed to write the EDSM attempt timestamp for {kind}:{systemKey}.", ex);
            }
        }

        /// <summary>Clears a (kind, system)'s recorded unresolved attempt, if any - called once it actually resolves (a later successful lookup, or a local journal-derived seed), so a stale "attempted recently" row never outlives the resolution it was tracking.</summary>
        public void Clear(string kind, string systemKey)
        {
            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM UnresolvedLookups WHERE Kind = $kind AND SystemKey = $key;";
                command.Parameters.AddWithValue("$kind", kind);
                command.Parameters.AddWithValue("$key", systemKey);
                command.ExecuteNonQuery();
            }
            catch (SqliteException ex)
            {
                Log.Warn(LogCategory, $"Failed to clear the EDSM attempt timestamp for {kind}:{systemKey}.", ex);
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
