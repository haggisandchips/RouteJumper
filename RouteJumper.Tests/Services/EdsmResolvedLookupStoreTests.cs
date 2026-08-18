using RouteJumper.Services;
using RouteJumper.Tests.TestSupport;
using Xunit;

namespace RouteJumper.Tests.Services
{
    public class EdsmResolvedLookupStoreTests
    {
        [Fact]
        public void GetValue_NeverSet_ReturnsNull()
        {
            using var dir = new TempDirectory();
            var store = new EdsmResolvedLookupStore(dir.Path);

            Assert.Null(store.GetValue("Coords", "SOL"));
        }

        [Fact]
        public void SetValue_ThenGetValue_RoundTrips()
        {
            using var dir = new TempDirectory();
            var store = new EdsmResolvedLookupStore(dir.Path);

            store.SetValue("StarType", "SOL", "G");

            Assert.Equal("G", store.GetValue("StarType", "SOL"));
        }

        [Fact]
        public void SetValue_CalledTwiceForSameKey_OverwritesWithLatestValue()
        {
            using var dir = new TempDirectory();
            var store = new EdsmResolvedLookupStore(dir.Path);

            store.SetValue("Coords", "SOL", "0|0|0");
            store.SetValue("Coords", "SOL", "1|2|3");

            Assert.Equal("1|2|3", store.GetValue("Coords", "SOL"));
        }

        [Fact]
        public void SetValue_DifferentKindsForSameSystemKey_AreIndependent()
        {
            using var dir = new TempDirectory();
            var store = new EdsmResolvedLookupStore(dir.Path);

            store.SetValue("Coords", "SOL", "0|0|0");
            store.SetValue("StarType", "SOL", "G");

            Assert.Equal("0|0|0", store.GetValue("Coords", "SOL"));
            Assert.Equal("G", store.GetValue("StarType", "SOL"));
        }

        [Fact]
        public void SetValue_EventuallyVisibleFromAFreshStoreInstance()
        {
            // Proves the write actually reaches disk, not just some in-memory state on the
            // instance that wrote it - each store call opens/closes its own connection, so this
            // mirrors a real app restart the same way EdsmStarSystemLookupServiceTests' own
            // "...VisibleFromAFreshServiceInstance" tests do.
            using var dir = new TempDirectory();
            var store = new EdsmResolvedLookupStore(dir.Path);
            store.SetValue("SystemAddress", "SOL", "10477373803");

            var freshStore = new EdsmResolvedLookupStore(dir.Path);

            Assert.Equal("10477373803", freshStore.GetValue("SystemAddress", "SOL"));
        }
    }
}
