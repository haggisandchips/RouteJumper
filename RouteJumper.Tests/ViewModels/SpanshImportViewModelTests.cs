using System.IO;
using RouteJumper.Models;
using RouteJumper.Services;
using RouteJumper.Services.Spansh;
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
            Func<IReadOnlyList<SpanshRouteJump>, RouteType, bool>? applyRoute = null,
            string? knownCurrentSystem = null,
            string? knownCarrierSystem = null,
            bool defaultToOvercharge = false,
            string? knownJournalFilePath = null,
            int? knownCurrentCargo = null,
            Func<string, Task<LoadoutSnapshot?>>? readLoadoutSnapshot = null) => new(
                routeService ?? new FakeSpanshRouteService(),
                applyRoute ?? ((_, _) => true),
                new AppConfigStore(dir.Path),
                knownCurrentSystem,
                knownCarrierSystem,
                defaultToOvercharge,
                knownJournalFilePath,
                knownCurrentCargo,
                readLoadoutSnapshot);

        /// <summary>A ship build ShipBuildDerivation.Derive can always resolve - a bare, unengineered standard FSD.</summary>
        private static LoadoutSnapshot ValidLoadout() => new(
            "anaconda",
            new[] { new LoadoutModule("FrameShiftDrive", "Int_Hyperdrive_Size6_Class5", null) },
            UnladenMass: 1000,
            FuelCapacityMain: 32,
            FuelCapacityReserve: 0.63);

        // ===================== Fleet Carrier tab - pre-fill Source from the Captain's own fleet
        // carrier's real current location. Unlike the Neutron Plotter tab's own Source (below),
        // this needs a real Spansh-resolved id, so it's a background search rather than an
        // immediately-applied local value - PrefillFleetCarrierSourceAsync is internal (not
        // private) specifically so these tests can await it directly instead of racing the
        // constructor's own fire-and-forget call. =====================

        [Fact]
        public async Task PrefillFleetCarrierSourceAsync_ExactNameMatchFound_SetsSourceSelected()
        {
            using var dir = new TempDirectory();
            var carrierSuggestion = new SpanshSystemSuggestion("10477373803", 10477373803, "Sol");
            var service = new FakeSpanshRouteService { SearchResults = new[] { carrierSuggestion } };
            var vm = Create(dir, service);

            await vm.PrefillFleetCarrierSourceAsync(service.SearchSystemNamesAsync, "Sol");

            Assert.Equal(carrierSuggestion, vm.Source.Selected);
        }

        [Fact]
        public async Task PrefillFleetCarrierSourceAsync_NoMatchingName_LeavesSourceUnselected()
        {
            using var dir = new TempDirectory();
            var service = new FakeSpanshRouteService { SearchResults = new[] { new SpanshSystemSuggestion("1", 1, "Deciat") } };
            var vm = Create(dir, service);

            await vm.PrefillFleetCarrierSourceAsync(service.SearchSystemNamesAsync, "Sol");

            Assert.Null(vm.Source.Selected);
        }

        [Fact]
        public async Task PrefillFleetCarrierSourceAsync_CmdrAlreadyPickedSource_DoesNotOverwrite()
        {
            using var dir = new TempDirectory();
            var carrierSuggestion = new SpanshSystemSuggestion("10477373803", 10477373803, "Sol");
            var service = new FakeSpanshRouteService { SearchResults = new[] { carrierSuggestion } };
            var vm = Create(dir, service);
            var manualPick = new SpanshSystemSuggestion("2", 2, "Deciat");
            vm.Source.Selected = manualPick;

            await vm.PrefillFleetCarrierSourceAsync(service.SearchSystemNamesAsync, "Sol");

            Assert.Equal(manualPick, vm.Source.Selected);
        }

        [Fact]
        public async Task PrefillFleetCarrierSourceAsync_CmdrAlreadyTyped_DoesNotOverwrite()
        {
            using var dir = new TempDirectory();
            var carrierSuggestion = new SpanshSystemSuggestion("10477373803", 10477373803, "Sol");
            var service = new FakeSpanshRouteService { SearchResults = new[] { carrierSuggestion } };
            var vm = Create(dir, service);
            vm.Source.Query = "Ded";

            await vm.PrefillFleetCarrierSourceAsync(service.SearchSystemNamesAsync, "Sol");

            Assert.Null(vm.Source.Selected);
        }

        [Fact]
        public void Constructor_KnownCarrierSystem_KicksOffBackgroundPrefillThatSetsSource()
        {
            // FakeSpanshRouteService resolves synchronously (Task.FromResult, no real I/O to yield
            // on), so the fire-and-forget prefill the constructor kicks off has already run to
            // completion by the time the constructor itself returns.
            using var dir = new TempDirectory();
            var carrierSuggestion = new SpanshSystemSuggestion("10477373803", 10477373803, "Sol");
            var service = new FakeSpanshRouteService { SearchResults = new[] { carrierSuggestion } };

            var vm = Create(dir, service, knownCarrierSystem: "Sol");

            Assert.Equal(carrierSuggestion, vm.Source.Selected);
        }

        [Fact]
        public void Constructor_NoKnownCarrierSystem_SourceStartsUnselected()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);

            Assert.Null(vm.Source.Selected);
        }

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

        // ===================== Galaxy Plotter tab - background loadout re-read on construction
        // (LoadGalaxyLoadoutAsync), Source pre-fill (PrefillGalaxySourceAsync, same background-
        // resolve-to-id mechanism as the Fleet Carrier tab's own Source), Cargo pre-fill, and
        // CanExecute gating =====================

        [Fact]
        public async Task Constructor_KnownJournalFilePathResolvesValidLoadout_ClearsStatusMessageOnceLoaded()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir, knownJournalFilePath: "irrelevant.log", readLoadoutSnapshot: _ => Task.FromResult<LoadoutSnapshot?>(ValidLoadout()));

            // The constructor's own LoadGalaxyLoadoutAsync call is fire-and-forget; awaiting it
            // directly (internal, not private) avoids racing the background task the way
            // Constructor_KnownCarrierSystem_KicksOffBackgroundPrefillThatSetsSource already
            // relies on FakeSpanshRouteService resolving synchronously for.
            await vm.LoadGalaxyLoadoutAsync(_ => Task.FromResult<LoadoutSnapshot?>(ValidLoadout()), "irrelevant.log");

            Assert.Equal(string.Empty, vm.GalaxyStatusMessage);
        }

        [Fact]
        public void Constructor_NoKnownJournalFilePath_ShowsExplanatoryMessage()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);

            Assert.Contains("No running instance", vm.GalaxyStatusMessage);
            Assert.False(vm.GalaxyCalculateCommand.CanExecute(null));
        }

        [Fact]
        public async Task LoadGalaxyLoadoutAsync_NoLoadoutEverLogged_ShowsExplanatoryMessage()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);

            await vm.LoadGalaxyLoadoutAsync(_ => Task.FromResult<LoadoutSnapshot?>(null), "irrelevant.log");

            Assert.Contains("No ship loadout logged yet", vm.GalaxyStatusMessage);
            Assert.False(vm.GalaxyCalculateCommand.CanExecute(null));
        }

        [Fact]
        public async Task LoadGalaxyLoadoutAsync_DerivationFails_ShowsShipBuildDerivationsOwnErrorMessage()
        {
            using var dir = new TempDirectory();
            var noFsd = new LoadoutSnapshot("sidewinder", Array.Empty<LoadoutModule>(), 10, 2, 0.04);
            var vm = Create(dir);

            await vm.LoadGalaxyLoadoutAsync(_ => Task.FromResult<LoadoutSnapshot?>(noFsd), "irrelevant.log");

            Assert.Contains("No Frame Shift Drive", vm.GalaxyStatusMessage);
            Assert.False(vm.GalaxyCalculateCommand.CanExecute(null));
        }

        [Fact]
        public async Task LoadGalaxyLoadoutAsync_ReaderThrows_ShowsGenericMessageRatherThanThrowing()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);

            await vm.LoadGalaxyLoadoutAsync(_ => throw new IOException("locked"), "irrelevant.log");

            Assert.Contains("Could not read", vm.GalaxyStatusMessage);
        }

        [Fact]
        public void Constructor_KnownCurrentCargo_PreFillsGalaxyCargo()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir, knownCurrentCargo: 128);

            Assert.Equal("128", vm.GalaxyCargo);
        }

        [Fact]
        public void Constructor_NoKnownCurrentCargo_GalaxyCargoDefaultsToZero()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);

            Assert.Equal("0", vm.GalaxyCargo);
        }

        [Fact]
        public async Task PrefillGalaxySourceAsync_ExactNameMatchFound_SetsGalaxySourceSelected()
        {
            using var dir = new TempDirectory();
            var suggestion = new SpanshSystemSuggestion("10477373803", 10477373803, "Sol");
            var service = new FakeSpanshRouteService { SearchResults = new[] { suggestion } };
            var vm = Create(dir, service);

            await vm.PrefillGalaxySourceAsync(service.SearchSystemNamesAsync, "Sol");

            Assert.Equal(suggestion, vm.GalaxySource.Selected);
        }

        [Fact]
        public async Task PrefillGalaxySourceAsync_CmdrAlreadyPickedSource_DoesNotOverwrite()
        {
            using var dir = new TempDirectory();
            var suggestion = new SpanshSystemSuggestion("10477373803", 10477373803, "Sol");
            var service = new FakeSpanshRouteService { SearchResults = new[] { suggestion } };
            var vm = Create(dir, service);
            var manualPick = new SpanshSystemSuggestion("2", 2, "Deciat");
            vm.GalaxySource.Selected = manualPick;

            await vm.PrefillGalaxySourceAsync(service.SearchSystemNamesAsync, "Sol");

            Assert.Equal(manualPick, vm.GalaxySource.Selected);
        }

        [Fact]
        public async Task GalaxyCalculateCommand_LoadoutResolvedButNoDestination_CannotExecute()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            vm.GalaxySource.Selected = new SpanshSystemSuggestion("1", 1, "Sol");
            await vm.LoadGalaxyLoadoutAsync(_ => Task.FromResult<LoadoutSnapshot?>(ValidLoadout()), "irrelevant.log");

            Assert.False(vm.GalaxyCalculateCommand.CanExecute(null));
        }

        [Fact]
        public async Task GalaxyCalculateCommand_CargoBlank_CannotExecute()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            vm.GalaxySource.Selected = new SpanshSystemSuggestion("1", 1, "Sol");
            vm.GalaxyDestination.Selected = new SpanshSystemSuggestion("2", 2, "Sirius");
            await vm.LoadGalaxyLoadoutAsync(_ => Task.FromResult<LoadoutSnapshot?>(ValidLoadout()), "irrelevant.log");

            vm.GalaxyCargo = "  ";

            Assert.False(vm.GalaxyCalculateCommand.CanExecute(null));
        }

        [Fact]
        public async Task GalaxyCalculateCommand_SourceDestinationLoadoutCargoAllSet_CanExecute()
        {
            using var dir = new TempDirectory();
            var vm = Create(dir);
            vm.GalaxySource.Selected = new SpanshSystemSuggestion("1", 1, "Sol");
            vm.GalaxyDestination.Selected = new SpanshSystemSuggestion("2", 2, "Sirius");
            await vm.LoadGalaxyLoadoutAsync(_ => Task.FromResult<LoadoutSnapshot?>(ValidLoadout()), "irrelevant.log");

            Assert.True(vm.GalaxyCalculateCommand.CanExecute(null));
        }

        [Fact]
        public async Task CalculateGalaxyAsync_BuildsRequestFromDerivedParametersAndToggles()
        {
            using var dir = new TempDirectory();
            var service = new FakeSpanshRouteService();
            var vm = Create(dir, service);
            vm.GalaxySource.Selected = new SpanshSystemSuggestion("1", 1, "Sol");
            vm.GalaxyDestination.Selected = new SpanshSystemSuggestion("2", 2, "Sirius");
            await vm.LoadGalaxyLoadoutAsync(_ => Task.FromResult<LoadoutSnapshot?>(ValidLoadout()), "irrelevant.log");
            vm.GalaxyCargo = "16";
            vm.GalaxyReserveTankSize = "2";
            vm.GalaxyUseInjections = true;
            vm.GalaxyAlgorithm = "pessimistic";
            service.GalaxyResult = SpanshRouteJobStatus.Completed(Array.Empty<SpanshRouteJump>());

            await vm.CalculateGalaxyAsync();

            var request = Assert.Single(service.GalaxyRequests);
            Assert.Equal("1", request.SourceId);
            Assert.Equal("2", request.DestinationId);
            Assert.Equal("16", request.Cargo);
            Assert.Equal("2", request.ReserveSize);
            Assert.True(request.UseInjections);
            Assert.Equal("pessimistic", request.Algorithm);
            Assert.Equal(ShipBuildDerivation.RegularSuperchargeMultiplier, request.SuperchargeMultiplier);
            Assert.Equal("Route applied.", vm.GalaxyStatusMessage);
        }

        // ===================== Each tab tags its own RouteType when calling _applyRoute
        // (RouteViewModel.ImportFromSpansh's own second parameter) - Fleet Carrier Plain, Neutron
        // Neutron, Galaxy Galaxy - so the Route table knows which extra columns (if any) to show
        // and persist for the route it just imported. =====================

        [Fact]
        public async Task CalculateAsync_AppliesRouteWithPlainRouteType()
        {
            using var dir = new TempDirectory();
            var service = new FakeSpanshRouteService { FleetCarrierResult = SpanshRouteJobStatus.Completed(new[] { new SpanshRouteJump(1, "Sol", 0, 0, 0) }) };
            RouteType? appliedType = null;
            var vm = Create(dir, service, applyRoute: (_, type) => { appliedType = type; return true; });
            vm.Source.Selected = new SpanshSystemSuggestion("1", 1, "Sol");
            vm.Destination.Selected = new SpanshSystemSuggestion("2", 2, "Sirius");

            await vm.CalculateAsync();

            Assert.Equal(RouteType.Plain, appliedType);
        }

        [Fact]
        public async Task CalculateNeutronAsync_AppliesRouteWithNeutronRouteType()
        {
            using var dir = new TempDirectory();
            var service = new FakeSpanshRouteService { NeutronResult = SpanshRouteJobStatus.Completed(new[] { new SpanshRouteJump(1, "Sol", 0, 0, 0) }) };
            RouteType? appliedType = null;
            var vm = Create(dir, service, applyRoute: (_, type) => { appliedType = type; return true; }, knownCurrentSystem: "Sol");
            vm.NeutronDestination.Selected = new SpanshSystemSuggestion("Sirius", 1, "Sirius");
            vm.NeutronRange = "50";

            await vm.CalculateNeutronAsync();

            Assert.Equal(RouteType.Neutron, appliedType);
        }

        [Fact]
        public async Task CalculateGalaxyAsync_AppliesRouteWithGalaxyRouteType()
        {
            using var dir = new TempDirectory();
            var service = new FakeSpanshRouteService { GalaxyResult = SpanshRouteJobStatus.Completed(new[] { new SpanshRouteJump(1, "Sol", 0, 0, 0) }) };
            RouteType? appliedType = null;
            var vm = Create(dir, service, applyRoute: (_, type) => { appliedType = type; return true; });
            vm.GalaxySource.Selected = new SpanshSystemSuggestion("1", 1, "Sol");
            vm.GalaxyDestination.Selected = new SpanshSystemSuggestion("2", 2, "Sirius");
            await vm.LoadGalaxyLoadoutAsync(_ => Task.FromResult<LoadoutSnapshot?>(ValidLoadout()), "irrelevant.log");

            await vm.CalculateGalaxyAsync();

            Assert.Equal(RouteType.Galaxy, appliedType);
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
