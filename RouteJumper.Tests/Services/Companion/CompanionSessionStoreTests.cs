using RouteJumper.Services.Companion;
using RouteJumper.Tests.TestSupport;
using Xunit;

namespace RouteJumper.Tests.Services.Companion
{
    public class CompanionSessionStoreTests
    {
        [Fact]
        public void GetSessionsDueForDeletion_NothingRecorded_ReturnsEmpty()
        {
            using var dir = new TempDirectory();
            var store = new CompanionSessionStore(dir.Path);

            Assert.Empty(store.GetSessionsDueForDeletion(DateTime.UtcNow));
        }

        [Fact]
        public void RecordPendingDeletion_DeleteAfterInThePast_IsDue()
        {
            using var dir = new TempDirectory();
            var store = new CompanionSessionStore(dir.Path);
            var sessionId = Guid.NewGuid();

            store.RecordPendingDeletion(sessionId, DateTime.UtcNow.AddHours(-1));

            Assert.Contains(sessionId, store.GetSessionsDueForDeletion(DateTime.UtcNow));
        }

        [Fact]
        public void RecordPendingDeletion_DeleteAfterInTheFuture_IsNotYetDue()
        {
            using var dir = new TempDirectory();
            var store = new CompanionSessionStore(dir.Path);
            var sessionId = Guid.NewGuid();

            store.RecordPendingDeletion(sessionId, DateTime.UtcNow.AddHours(1));

            Assert.DoesNotContain(sessionId, store.GetSessionsDueForDeletion(DateTime.UtcNow));
        }

        [Fact]
        public void RecordPendingDeletion_CalledTwiceForSameSession_OverwritesWithLatestDeadline()
        {
            using var dir = new TempDirectory();
            var store = new CompanionSessionStore(dir.Path);
            var sessionId = Guid.NewGuid();

            store.RecordPendingDeletion(sessionId, DateTime.UtcNow.AddHours(-5));
            store.RecordPendingDeletion(sessionId, DateTime.UtcNow.AddHours(1));

            Assert.DoesNotContain(sessionId, store.GetSessionsDueForDeletion(DateTime.UtcNow));
        }

        [Fact]
        public void Remove_StopsItBeingReturnedAsDue()
        {
            using var dir = new TempDirectory();
            var store = new CompanionSessionStore(dir.Path);
            var sessionId = Guid.NewGuid();
            store.RecordPendingDeletion(sessionId, DateTime.UtcNow.AddHours(-1));

            store.Remove(sessionId);

            Assert.Empty(store.GetSessionsDueForDeletion(DateTime.UtcNow));
        }

        [Fact]
        public void Remove_NeverRecorded_DoesNothing()
        {
            using var dir = new TempDirectory();
            var store = new CompanionSessionStore(dir.Path);

            var exception = Record.Exception(() => store.Remove(Guid.NewGuid()));

            Assert.Null(exception);
        }

        [Fact]
        public void RecordPendingDeletion_EventuallyVisibleFromAFreshStoreInstance()
        {
            // Proves the write actually reaches disk, not just some in-memory state on the
            // instance that wrote it - mirrors EdsmResolvedLookupStoreTests' own equivalent.
            using var dir = new TempDirectory();
            var store = new CompanionSessionStore(dir.Path);
            var sessionId = Guid.NewGuid();
            store.RecordPendingDeletion(sessionId, DateTime.UtcNow.AddHours(-1));

            var freshStore = new CompanionSessionStore(dir.Path);

            Assert.Contains(sessionId, freshStore.GetSessionsDueForDeletion(DateTime.UtcNow));
        }
    }
}
