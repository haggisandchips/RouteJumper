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
        public async Task PopulateAsync_MultipleDistinctSystems_ResolvesStarTypesInOneBatchedCallNotOnePerSystem()
        {
            // The reported bug: star types must be resolved via one batched call covering every
            // distinct system in the route, not one request per system.
            var fake = new FakeStarSystemLookupService();
            fake.StarTypes["A"] = "TypeA";
            fake.StarTypes["B"] = "TypeB";
            fake.StarTypes["C"] = "TypeC";
            var rows = new[] { Row("A"), Row("B"), Row("C") };
            var service = new RouteRowEnrichmentService(fake);

            await service.PopulateAsync(rows, originSystemName: null);

            Assert.Equal("TypeA", rows[0].StarType);
            Assert.Equal("TypeB", rows[1].StarType);
            Assert.Equal("TypeC", rows[2].StarType);
            var batch = Assert.Single(fake.StarTypeBatchRequests); // one call, not three
            Assert.Equal(new[] { "A", "B", "C" }, batch);
        }

        [Fact]
        public async Task PopulateAsync_CancelledDuringStarTypeResolution_LeavesAlreadyResolvedDistancesIntact()
        {
            // Distance resolves as its own earlier phase (PopulateDistancesAsync), before star
            // type's own batched call even starts - a cancellation mid-star-type-batch must never
            // corrupt state an earlier, already-completed phase already resolved.
            var fake = new FakeStarSystemLookupService();
            fake.Coordinates["Sol"] = new GalacticCoordinates(0, 0, 0);
            fake.Coordinates["A"] = new GalacticCoordinates(3, 4, 0);
            var gate = new TaskCompletionSource();
            fake.StarTypeGates["A"] = gate;
            var rows = new[] { Row("A") };
            var service = new RouteRowEnrichmentService(fake);
            using var cts = new CancellationTokenSource();

            var populateTask = service.PopulateAsync(rows, "Sol", cts.Token);
            Assert.Equal(5.0, rows[0].Distance!.Value, precision: 6); // distance phase already completed

            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => populateTask);
            Assert.Equal(5.0, rows[0].Distance!.Value, precision: 6); // untouched by the star-type cancellation
            Assert.Null(rows[0].StarType);
        }

        [Fact]
        public async Task PopulateAsync_EmptyRows_IsNoOp()
        {
            var fake = new FakeStarSystemLookupService();
            var service = new RouteRowEnrichmentService(fake);

            await service.PopulateAsync(Array.Empty<RouteRowViewModel>(), "Sol");
        }

        [Fact]
        public async Task PopulateAsync_UnresolvedMidRouteSystem_OnlyTheCulpritRowIsMarkedUnavailable()
        {
            // The exact reported bug: row X's own coordinates are unresolved, which blanks its
            // OWN Distance and cascades into row X+1's Distance too - but only row X's own data is
            // actually the problem. Row X+1's OwnCoordinatesState must stay Resolved (its own
            // system is fine) so it never shows the "Plot needed" placeholder that belongs to X.
            var fake = new FakeStarSystemLookupService();
            fake.Coordinates["Sol"] = new GalacticCoordinates(0, 0, 0);
            fake.Coordinates["A"] = new GalacticCoordinates(3, 4, 0);
            // "X" deliberately has no entry - the genuine culprit.
            fake.Coordinates["B"] = new GalacticCoordinates(10, 10, 10);
            var rows = new[] { Row("A"), Row("X"), Row("B") };
            var service = new RouteRowEnrichmentService(fake);

            await service.PopulateAsync(rows, "Sol");

            Assert.Equal(EdsmLookupState.Resolved, rows[0].OwnCoordinatesState);
            Assert.False(rows[0].IsDistancePlaceholder);

            Assert.Equal(EdsmLookupState.Unavailable, rows[1].OwnCoordinatesState); // X: the true culprit
            Assert.True(rows[1].IsDistancePlaceholder);

            Assert.Equal(EdsmLookupState.Resolved, rows[2].OwnCoordinatesState); // B: own data fine
            Assert.Null(rows[2].Distance); // still blank, blocked by X
            Assert.False(rows[2].IsDistancePlaceholder); // but NOT the culprit - no placeholder
        }

        [Fact]
        public async Task PopulateAsync_Row1BlockedByUnresolvedOrigin_NoPlaceholderShown()
        {
            var fake = new FakeStarSystemLookupService();
            // Origin deliberately unresolved - row 1's own system is fine.
            fake.Coordinates["A"] = new GalacticCoordinates(0, 0, 0);
            var rows = new[] { Row("A") };
            var service = new RouteRowEnrichmentService(fake);

            await service.PopulateAsync(rows, originSystemName: "UnknownOrigin");

            Assert.Null(rows[0].Distance);
            Assert.Equal(EdsmLookupState.Resolved, rows[0].OwnCoordinatesState);
            Assert.False(rows[0].IsDistancePlaceholder);
        }

        [Fact]
        public async Task PopulateAsync_CoordinatesKnownButStarTypeUnresolved_ShowsTargetNeededPlaceholder()
        {
            var fake = new FakeStarSystemLookupService();
            fake.Coordinates["A"] = new GalacticCoordinates(0, 0, 0);
            // No StarTypes entry for "A" - coordinates resolved, star type is not.
            var rows = new[] { Row("A") };
            var service = new RouteRowEnrichmentService(fake);

            await service.PopulateAsync(rows, originSystemName: null);

            Assert.Equal(EdsmLookupState.Resolved, rows[0].OwnCoordinatesState);
            Assert.Equal(EdsmLookupState.Unavailable, rows[0].OwnStarTypeState);
            Assert.True(rows[0].IsStarTypePlaceholder);
            Assert.Equal("Target needed", rows[0].StarTypeDisplay);
        }

        [Fact]
        public async Task PopulateAsync_CoordinatesUnresolved_DoesNotFlagStarTypeAsPlaceholderToo()
        {
            // Per design: when coordinates are missing, Distance shows "Plot needed" but Star
            // Type is left exactly as-is (no piggyback "Target needed") - plotting a route fixes
            // both via the same NavRoute.json seeding, so Star Type doesn't need its own callout.
            var fake = new FakeStarSystemLookupService();
            // Neither Coordinates nor StarTypes has an entry for "X".
            var rows = new[] { Row("X") };
            var service = new RouteRowEnrichmentService(fake);

            await service.PopulateAsync(rows, originSystemName: null);

            Assert.True(rows[0].IsDistancePlaceholder);
            Assert.False(rows[0].IsStarTypePlaceholder);
        }

        [Fact]
        public void ApplyCachedValues_CacheHit_AppliesInstantlyWithNoNetworkCall()
        {
            var fake = new FakeStarSystemLookupService();
            fake.Coordinates["Sol"] = new GalacticCoordinates(0, 0, 0);
            fake.Coordinates["A"] = new GalacticCoordinates(3, 4, 0);
            fake.StarTypes["A"] = "K (Yellow-Orange)";
            var rows = new[] { Row("A") };
            var service = new RouteRowEnrichmentService(fake);

            service.ApplyCachedValues(rows, "Sol");

            Assert.Equal(5.0, rows[0].Distance!.Value, precision: 6);
            Assert.Equal("K (Yellow-Orange)", rows[0].StarType);
            Assert.Equal(EdsmLookupState.Resolved, rows[0].OwnCoordinatesState);
            Assert.Equal(EdsmLookupState.Resolved, rows[0].OwnStarTypeState);
            Assert.Empty(fake.StarTypeCallOrder); // pure cache read - GetMainStarTypeAsync never called
        }

        [Fact]
        public void ApplyCachedValues_UncachedRowBreaksChain_DoesNotSkipOverTheGap()
        {
            var fake = new FakeStarSystemLookupService();
            fake.Coordinates["Sol"] = new GalacticCoordinates(0, 0, 0);
            // "X" deliberately not cached - row 2's leg can't be computed across the gap.
            fake.Coordinates["B"] = new GalacticCoordinates(10, 10, 10);
            var rows = new[] { Row("X"), Row("B") };
            var service = new RouteRowEnrichmentService(fake);

            service.ApplyCachedValues(rows, "Sol");

            Assert.Null(rows[0].Distance);
            Assert.Null(rows[1].Distance); // not incorrectly chained from Sol, skipping over X
        }

        [Fact]
        public void ApplyCachedValues_UncachedRow_NeverMarksUnavailable()
        {
            // A cache miss in ApplyCachedValues doesn't distinguish "still resolving" from
            // "confirmed unavailable" - only a completed PopulateAsync pass (which actually calls
            // EDSM) may set Unavailable. ApplyCachedValues must leave existing state untouched.
            var fake = new FakeStarSystemLookupService();
            var rows = new[] { Row("Unseeded") };
            var service = new RouteRowEnrichmentService(fake);

            service.ApplyCachedValues(rows, originSystemName: null);

            Assert.Equal(EdsmLookupState.Resolving, rows[0].OwnCoordinatesState);
            Assert.Equal(EdsmLookupState.Resolving, rows[0].OwnStarTypeState);
        }
    }
}
