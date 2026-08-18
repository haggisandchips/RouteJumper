using RouteJumper.Models;
using RouteJumper.Services;
using RouteJumper.Tests.TestSupport;
using RouteJumper.ViewModels;
using Xunit;

namespace RouteJumper.Tests.ViewModels
{
    public class SpanshImportViewModelTests
    {
        private static SpanshImportViewModel Create(
            TempDirectory dir,
            FakeSpanshRouteService? routeService = null,
            Func<IReadOnlyList<SpanshRouteJump>, bool>? applyRoute = null,
            string? knownCurrentSystem = null,
            bool defaultToOvercharge = false) => new(
                routeService ?? new FakeSpanshRouteService(),
                applyRoute ?? (_ => true),
                new AppConfigStore(dir.Path),
                knownCurrentSystem,
                defaultToOvercharge);

        // ===================== Neutron Plotter tab - pre-fill (Source from the CMDR's own current
        // system; Range is never pre-filled - see SpanshImportViewModel's own constructor doc
        // comment for why) and CanExecute gating =====================

        [Fact]
        public void Constructor_NeutronRange_AlwaysStartsBlankRequiringManualEntry()
        {
            // Loadout's own MaxJumpRange reflects only whatever fuel/cargo the ship happened to
            // be carrying when last logged, not necessarily what the CMDR wants to plan the route
            // around - so, unlike Source below, it's never presumed; the CMDR always types Range
            // in by hand.
            using var dir = new TempDirectory();
            var vm = Create(dir);

            Assert.Equal(string.Empty, vm.NeutronRange);
        }

        [Fact]
        public void Constructor_NeutronEfficiency_DefaultsTo60()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);

            Assert.Equal("60", vm.NeutronEfficiency);
        }

        [Fact]
        public void Constructor_KnownCurrentSystem_PreFillsNeutronSourceAsAlreadySelected()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir, knownCurrentSystem: "Sol");

            Assert.NotNull(vm.NeutronSource.Selected);
            Assert.Equal("Sol", vm.NeutronSource.Selected!.Value.Name);
            Assert.Equal("Sol", vm.NeutronSource.Query);
        }

        [Fact]
        public void Constructor_NoKnownCurrentSystem_NeutronSourceStartsUnselected()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);

            Assert.Null(vm.NeutronSource.Selected);
        }

        [Fact]
        public void NeutronSource_TypingAfterPreFill_ClearsSelection()
        {
            // Same "always requires an actual pick, typing further clears it" rule every other
            // SpanshSystemPickerViewModel field already follows - confirms the pre-filled Source
            // stays genuinely editable rather than being a locked-in value.
            using var dir = new TempDirectory();
            var vm = Create(dir, knownCurrentSystem: "Sol");

            vm.NeutronSource.Query = "Sola";

            Assert.Null(vm.NeutronSource.Selected);
        }

        [Fact]
        public void NeutronCalculateCommand_MissingSourceOrDestination_CannotExecute()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir, knownCurrentSystem: "Sol");
            vm.NeutronRange = "50";

            Assert.False(vm.NeutronCalculateCommand.CanExecute(null));
        }

        [Fact]
        public void NeutronCalculateCommand_RangeBlank_CannotExecute()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            vm.NeutronSource.Selected = new SpanshSystemSuggestion("Sol", null, "Sol");
            vm.NeutronDestination.Selected = new SpanshSystemSuggestion("Sirius", 1, "Sirius");

            Assert.Equal(string.Empty, vm.NeutronRange);
            Assert.False(vm.NeutronCalculateCommand.CanExecute(null));

            vm.NeutronRange = "50";

            Assert.True(vm.NeutronCalculateCommand.CanExecute(null));
        }

        [Fact]
        public void NeutronCalculateCommand_EfficiencyBlank_CannotExecute()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir, knownCurrentSystem: "Sol");
            vm.NeutronDestination.Selected = new SpanshSystemSuggestion("Sirius", 1, "Sirius");
            vm.NeutronRange = "50";

            vm.NeutronEfficiency = "  ";

            Assert.False(vm.NeutronCalculateCommand.CanExecute(null));
        }

        [Fact]
        public void NeutronCalculateCommand_SourceDestinationRangeEfficiencyAllSet_CanExecute()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir, knownCurrentSystem: "Sol");
            vm.NeutronDestination.Selected = new SpanshSystemSuggestion("Sirius", 1, "Sirius");
            vm.NeutronRange = "50";

            Assert.True(vm.NeutronCalculateCommand.CanExecute(null));
        }

        // ===================== Normal/Overcharge supercharge choice - defaults from the CMDR's
        // own ship's FrameShiftDrive slot (EliteInstanceViewModel.HasOverchargedFsd), but is a
        // plain, always-editable radio choice from there on =====================

        [Fact]
        public void Constructor_DefaultToOverchargeFalse_IsOverchargeStartsFalse()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir, defaultToOvercharge: false);

            Assert.False(vm.IsOvercharge);
        }

        [Fact]
        public void Constructor_DefaultToOverchargeTrue_IsOverchargeStartsTrue()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir, defaultToOvercharge: true);

            Assert.True(vm.IsOvercharge);
        }

        [Fact]
        public void IsOvercharge_AlwaysEditableRegardlessOfDefault()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir, defaultToOvercharge: true);

            vm.IsOvercharge = false;

            Assert.False(vm.IsOvercharge);
        }

        // ===================== CalculateNeutronAsync - only the parts reachable without waiting
        // through a real PollInterval delay (StartNeutronRouteAsync's own outright-rejection path
        // fails before the poll loop's first Task.Delay ever runs) =====================

        [Fact]
        public async Task CalculateNeutronAsync_StartRouteRejectedOutright_SurfacesSpanshsOwnReason()
        {
            using var dir = new TempDirectory();
            var service = new FakeSpanshRouteService { StartNeutronException = new InvalidOperationException("range must be greater than 10 LY") };
            var vm = Create(dir, service, knownCurrentSystem: "Sol");
            vm.NeutronDestination.Selected = new SpanshSystemSuggestion("Sirius", 1, "Sirius");
            vm.NeutronRange = "1";

            await vm.CalculateNeutronAsync();

            Assert.Equal("Failed: range must be greater than 10 LY", vm.NeutronStatusMessage);
            Assert.False(vm.IsNeutronCalculating);
        }

        [Fact]
        public async Task CalculateNeutronAsync_PassesSourceNameDestinationNameRangeAndEfficiency()
        {
            using var dir = new TempDirectory();
            var service = new FakeSpanshRouteService { StartNeutronException = new InvalidOperationException("stop here") };
            var vm = Create(dir, service, knownCurrentSystem: "Sol");
            vm.NeutronDestination.Selected = new SpanshSystemSuggestion("Sirius", 1, "Sirius");
            vm.NeutronRange = "45.2";
            vm.NeutronEfficiency = "75";

            await vm.CalculateNeutronAsync();

            var request = Assert.Single(service.NeutronRequests);
            Assert.Equal("Sol", request.SourceName);
            Assert.Equal("Sirius", request.DestinationName);
            Assert.Equal("45.2", request.Range);
            Assert.Equal("75", request.Efficiency);
            Assert.Equal(4, request.SuperchargeMultiplier);
        }

        [Fact]
        public async Task CalculateNeutronAsync_Overcharge_PassesMultiplierSix()
        {
            using var dir = new TempDirectory();
            var service = new FakeSpanshRouteService { StartNeutronException = new InvalidOperationException("stop here") };
            var vm = Create(dir, service, knownCurrentSystem: "Sol", defaultToOvercharge: true);
            vm.NeutronDestination.Selected = new SpanshSystemSuggestion("Sirius", 1, "Sirius");
            vm.NeutronRange = "45.2";

            await vm.CalculateNeutronAsync();

            var request = Assert.Single(service.NeutronRequests);
            Assert.Equal(6, request.SuperchargeMultiplier);
        }

        // ===================== Capitalize (job status wording, e.g. "queued" -> "Queued") =====================

        [Theory]
        [InlineData("queued", "Queued")]
        [InlineData("running", "Running")]
        [InlineData("Already Capitalized", "Already Capitalized")]
        [InlineData("a", "A")]
        [InlineData("", "")]
        public void Capitalize_UppercasesOnlyFirstCharacter(string input, string expected)
        {
            Assert.Equal(expected, SpanshImportViewModel.Capitalize(input));
        }

        // ===================== CreateCachingSearch (Integrations > Spansh, "maintain a cache of
        // queried terms whilst the modal is open and do not rerun queries") =====================

        [Fact]
        public async Task CreateCachingSearch_SameQueryTwice_OnlyCallsUnderlyingSearchOnce()
        {
            var callCount = 0;
            Task<IReadOnlyList<SpanshSystemSuggestion>> Underlying(string q, CancellationToken ct)
            {
                callCount++;
                return Task.FromResult<IReadOnlyList<SpanshSystemSuggestion>>(new[] { new SpanshSystemSuggestion("1", 1, q) });
            }

            var cached = SpanshImportViewModel.CreateCachingSearch(Underlying);

            var first = await cached("Sol", CancellationToken.None);
            var second = await cached("Sol", CancellationToken.None);

            Assert.Equal(1, callCount);
            Assert.Equal(first, second);
        }

        [Fact]
        public async Task CreateCachingSearch_SameQueryDifferentCase_TreatedAsSameCacheEntry()
        {
            var callCount = 0;
            Task<IReadOnlyList<SpanshSystemSuggestion>> Underlying(string q, CancellationToken ct)
            {
                callCount++;
                return Task.FromResult<IReadOnlyList<SpanshSystemSuggestion>>(Array.Empty<SpanshSystemSuggestion>());
            }

            var cached = SpanshImportViewModel.CreateCachingSearch(Underlying);

            await cached("Sol", CancellationToken.None);
            await cached("sol", CancellationToken.None);
            await cached("SOL", CancellationToken.None);

            Assert.Equal(1, callCount);
        }

        [Fact]
        public async Task CreateCachingSearch_DifferentQueries_CallsUnderlyingForEach()
        {
            var callCount = 0;
            Task<IReadOnlyList<SpanshSystemSuggestion>> Underlying(string q, CancellationToken ct)
            {
                callCount++;
                return Task.FromResult<IReadOnlyList<SpanshSystemSuggestion>>(Array.Empty<SpanshSystemSuggestion>());
            }

            var cached = SpanshImportViewModel.CreateCachingSearch(Underlying);

            await cached("Sol", CancellationToken.None);
            await cached("Deciat", CancellationToken.None);
            await cached("Sola", CancellationToken.None);

            Assert.Equal(3, callCount);
        }

        [Fact]
        public async Task CreateCachingSearch_SharedAcrossTwoCallers_SecondCallerHitsSameCache()
        {
            // Mirrors how SpanshImportViewModel wires one cached delegate shared by both the
            // Source and Destination pickers - searching "Sol" in one field must not force a
            // second network round trip when the other field searches "Sol" too.
            var callCount = 0;
            Task<IReadOnlyList<SpanshSystemSuggestion>> Underlying(string q, CancellationToken ct)
            {
                callCount++;
                return Task.FromResult<IReadOnlyList<SpanshSystemSuggestion>>(new[] { new SpanshSystemSuggestion("1", 1, q) });
            }

            var cached = SpanshImportViewModel.CreateCachingSearch(Underlying);

            await cached("Sol", CancellationToken.None); // "Source" field
            await cached("Sol", CancellationToken.None); // "Destination" field

            Assert.Equal(1, callCount);
        }

        [Fact]
        public async Task CreateCachingSearch_ThrowingSearch_NotCached_RetriedNextTime()
        {
            var callCount = 0;
            Task<IReadOnlyList<SpanshSystemSuggestion>> Underlying(string q, CancellationToken ct)
            {
                callCount++;
                if (callCount == 1)
                {
                    throw new OperationCanceledException();
                }

                return Task.FromResult<IReadOnlyList<SpanshSystemSuggestion>>(Array.Empty<SpanshSystemSuggestion>());
            }

            var cached = SpanshImportViewModel.CreateCachingSearch(Underlying);

            await Assert.ThrowsAsync<OperationCanceledException>(() => cached("Sol", CancellationToken.None));
            await cached("Sol", CancellationToken.None);

            Assert.Equal(2, callCount);
        }
    }
}
