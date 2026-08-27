using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using RouteJumper.Services.Logging;

namespace RouteJumper.Services.Companion
{
    /// <summary>
    /// Locally remembers every companion session id (SPEC §13) that's finished (completed or
    /// panicked) but not yet deleted from Firestore, along with the real-world instant it becomes
    /// safe to delete - CompanionSessionPublisher's own self-cleanup mechanism, since Firestore's
    /// TTL feature turned out to require the paid Blaze plan even for a single delete (no free
    /// allowance at all, unlike ordinary client-triggered deletes, which do have one). The desktop
    /// app is the *only* writer, so it's also the only thing that can ever know which sessions
    /// exist to begin with - CompanionSessionPublisher.CleanUpExpiredSessionsAsync reads this table
    /// once per launch and deletes (via plain Firestore REST DELETE calls, covered by the ordinary
    /// free quota) whatever's actually due, removing each row here only once its own Firestore
    /// documents are confirmed gone.
    ///
    /// Deliberately its own table in `routejumper.db`, not the generic Settings(Key,Value) one -
    /// same rationale as EdsmResolvedLookupStore/EdsmLookupAttemptStore's own tables: a distinct
    /// access pattern (insert-once, range-scan-by-deadline, delete-once) that benefits from its
    /// own schema.
    ///
    /// Same short-lived-connection, best-effort-degrades-to-nothing-persisted philosophy every
    /// other store in this app already uses for its own I/O.
    /// </summary>
    public class CompanionSessionStore
    {
        private const string LogCategory = "Companion";

        private readonly string _connectionString;

        public CompanionSessionStore() : this(AppPaths.DataDirectory)
        {
        }

        /// <summary>Test-only seam: lets RouteJumper.Tests point the store at a temp directory instead of the real per-user AppData location.</summary>
        internal CompanionSessionStore(string directory)
        {
            Directory.CreateDirectory(directory);

            _connectionString = $"Data Source={Path.Combine(directory, "routejumper.db")}";

            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "CREATE TABLE IF NOT EXISTS CompanionSessions (" +
                    "SessionId TEXT PRIMARY KEY, DeleteAfterUtc TEXT NOT NULL);";
                command.ExecuteNonQuery();
            }
            catch (SqliteException ex)
            {
                Log.Warn(LogCategory, "Could not create/open the CompanionSessions table - finished companion sessions will not be cleaned up automatically this session.", ex);
            }
        }

        /// <summary>Called once a companion session ends (CompanionSessionPublisher.EndSession) - records it as due for deletion once <paramref name="deleteAfterUtc"/> passes.</summary>
        public void RecordPendingDeletion(Guid sessionId, DateTime deleteAfterUtc)
        {
            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "INSERT INTO CompanionSessions (SessionId, DeleteAfterUtc) VALUES ($id, $deleteAfter) " +
                    "ON CONFLICT(SessionId) DO UPDATE SET DeleteAfterUtc = excluded.DeleteAfterUtc;";
                command.Parameters.AddWithValue("$id", sessionId.ToString());
                command.Parameters.AddWithValue("$deleteAfter", deleteAfterUtc.ToString("O", CultureInfo.InvariantCulture));
                command.ExecuteNonQuery();
            }
            catch (SqliteException ex)
            {
                Log.Warn(LogCategory, $"Failed to record companion session {sessionId} for later deletion.", ex);
            }
        }

        /// <summary>Every session whose own DeleteAfterUtc has already passed - empty (never null) on any read failure, so a transient I/O error just means "nothing to clean up this launch" rather than a crash.</summary>
        public IReadOnlyList<Guid> GetSessionsDueForDeletion(DateTime nowUtc)
        {
            var due = new List<Guid>();
            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT SessionId FROM CompanionSessions WHERE DeleteAfterUtc <= $now;";
                command.Parameters.AddWithValue("$now", nowUtc.ToString("O", CultureInfo.InvariantCulture));

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (Guid.TryParse(reader.GetString(0), out var sessionId))
                    {
                        due.Add(sessionId);
                    }
                }
            }
            catch (SqliteException ex)
            {
                Log.Warn(LogCategory, "Failed to read sessions due for deletion.", ex);
            }

            return due;
        }

        /// <summary>Called once a session's Firestore documents are confirmed deleted - stops it being retried on the next launch.</summary>
        public void Remove(Guid sessionId)
        {
            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM CompanionSessions WHERE SessionId = $id;";
                command.Parameters.AddWithValue("$id", sessionId.ToString());
                command.ExecuteNonQuery();
            }
            catch (SqliteException ex)
            {
                Log.Warn(LogCategory, $"Failed to remove companion session {sessionId} from the pending-deletion list.", ex);
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
