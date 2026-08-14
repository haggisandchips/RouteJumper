using RouteJumper.Models;
using RouteJumper.Services;
using RouteJumper.Tests.TestSupport;
using RouteJumper.ViewModels;
using Xunit;

namespace RouteJumper.Tests.Services
{
    public class RouteRowEnrichmentServiceTests
    {
        private static RouteRowViewModel Row(string system) => new() { SystemText = system };

        [Fact]
        public async Task PopulateAsync_ChainsLegDistancesFromOriginThroughEachRow()
        {
            var fake = new FakeStarSystemLookupService();
            fake.Coordinates["Sol"] = new GalacticCoordinates(0, 0, 0);
            fake.Coordinates["A"] = new GalacticCoordinates(3, 4, 0); // 5 ly from Sol
            fake.Coordinates["B"] = new GalacticCoordinates(3, 4, 3); // 3 ly from A
            var rows = new[] { Row("A"), Row("B") };
            var service = new RouteRowEnrichmentService(fake);

            await service.PopulateAsync(rows, "Sol");

            Assert.Equal(5.0, rows[0].Distance!.Value, precision: 6);
            Assert.Equal(3.0, rows[1].Distance!.Value, precision: 6);
        }

        [Fact]
        public async Task PopulateAsync_NullOrigin_LeavesOnlyRow1DistanceNull()
        {
            var fake = new FakeStarSystemLookupService();
            fake.Coordinates["A"] = new GalacticCoordinates(0, 0, 0);
            fake.Coordinates["B"] = new GalacticCoordinates(3, 4, 0);
            var rows = new[] { Row("A"), Row("B") };
            var service = new RouteRowEnrichmentService(fake);

            await service.PopulateAsync(rows, originSystemName: null);

            Assert.Null(rows[0].Distance);
            Assert.Equal(5.0, rows[1].Distance!.Value, precision: 6);
        }

        [Fact]
        public async Task PopulateAsync_UnresolvedMidRouteSystem_BlanksItsOwnAndTheNextRowsDistance()
        {
            var fake = new FakeStarSystemLookupService();
            fake.Coordinates["Sol"] = new GalacticCoordinates(0, 0, 0);
            fake.Coordinates["A"] = new GalacticCoordinates(3, 4, 0);
            // "X" deliberately has no entry - unresolvable, like a system EDSM has never heard of.
            fake.Coordinates["B"] = new GalacticCoordinates(10, 10, 10);
            var rows = new[] { Row("A"), Row("X"), Row("B") };
            var service = new RouteRowEnrichmentService(fake);

            await service.PopulateAsync(rows, "Sol");

            Assert.Equal(5.0, rows[0].Distance!.Value, precision: 6); // Sol -> A, unaffected
            Assert.Null(rows[1].Distance); // X itself unresolved
            Assert.Null(rows[2].Distance); // B's "previous" (X) is unresolved too
        }

        [Fact]
        public async Task PopulateAsync_RepeatedSystemName_ResolvesStarTypeOnceButAppliesToBothRows()
        {
            var fake = new FakeStarSystemLookupService();
            fake.StarTypes["A"] = "K (Yellow-Orange) Star";
            var rows = new[] { Row("A"), Row("A") };
            var service = new RouteRowEnrichmentService(fake);

            await service.PopulateAsync(rows, originSystemName: null);

            Assert.Equal("K (Yellow-Orange) Star", rows[0].StarType);
            Assert.Equal("K (Yellow-Orange) Star", rows[1].StarType);
            Assert.Single(fake.StarTypeCallOrder);
        }

        [Fact]
        public async Task PopulateAsync_StarTypesPopulateProgressively()
        {
            var fake = new FakeStarSystemLookupService();
            fake.StarTypes["A"] = "TypeA";
            fake.StarTypes["B"] = "TypeB";
            var gate = new TaskCompletionSource();
            fake.StarTypeGates["B"] = gate;
            var rows = new[] { Row("A"), Row("B") };
            var service = new RouteRowEnrichmentService(fake);

            var populateTask = service.PopulateAsync(rows, originSystemName: null);

            // Row A resolves synchronously (no gate); row B's lookup is still held up - proves
            // rows populate one at a time rather than all appearing together at the very end.
            Assert.Equal("TypeA", rows[0].StarType);
            Assert.Null(rows[1].StarType);

            gate.SetResult();
            await populateTask;

            Assert.Equal("TypeB", rows[1].StarType);
        }

        [Fact]
        public async Task PopulateAsync_CancelledMidPopulation_ThrowsAndLeavesAlreadyPopulatedRowsIntact()
        {
            var fake = new FakeStarSystemLookupService();
            fake.StarTypes["A"] = "TypeA";
            var gate = new TaskCompletionSource();
            fake.StarTypeGates["B"] = gate;
            var rows = new[] { Row("A"), Row("B") };
            var service = new RouteRowEnrichmentService(fake);
            using var cts = new CancellationTokenSource();

            var populateTask = service.PopulateAsync(rows, originSystemName: null, cts.Token);
            Assert.Equal("TypeA", rows[0].StarType);

            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => populateTask);
            Assert.Equal("TypeA", rows[0].StarType); // untouched by the cancellation
            Assert.Null(rows[1].StarType);
        }

        [Fact]
        public async Task PopulateAsync_EmptyRows_IsNoOp()
        {
            var fake = new FakeStarSystemLookupService();
            var service = new RouteRowEnrichmentService(fake);

            await service.PopulateAsync(Array.Empty<RouteRowViewModel>(), "Sol");
        }
    }
}
