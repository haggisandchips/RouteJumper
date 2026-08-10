using RouteJumper.ViewModels;
using Xunit;

namespace RouteJumper.Tests.ViewModels
{
    public class EliteInstanceViewModelTests
    {
        private static EliteInstanceViewModel Create(
            int? cargoCapacity = null,
            int? currentCargo = null,
            int? currentTritium = null,
            string? currentSystem = null,
            string? currentStation = null,
            string? carrierName = null,
            string? carrierSystem = null,
            string? carrierBody = null,
            int? carrierFuelLevel = null,
            IntPtr windowHandle = default) => new(
                processId: 1,
                commanderName: "Jameson",
                fid: "F123",
                journalFileName: "Journal.01.log",
                windowHandle: windowHandle,
                windowPosition: "(0,0)",
                monitorInfo: "Monitor 1",
                cargoCapacity: cargoCapacity,
                currentCargo: currentCargo,
                currentTritium: currentTritium,
                currentSystem: currentSystem,
                currentStation: currentStation,
                carrierName: carrierName,
                carrierSystem: carrierSystem,
                carrierBody: carrierBody,
                journalFilePath: @"C:\journal.log",
                carrierId: null,
                carrierFuelLevel: carrierFuelLevel);

        [Fact]
        public void WindowHandleDisplay_Zero_IsUnknown()
        {
            Assert.Equal("Unknown", Create(windowHandle: IntPtr.Zero).WindowHandleDisplay);
        }

        [Fact]
        public void WindowHandleDisplay_NonZero_IsHex()
        {
            Assert.Equal("0xABCD", Create(windowHandle: (IntPtr)0xABCD).WindowHandleDisplay);
        }

        [Fact]
        public void CargoDisplay_BothKnown_IncludesTritiumInTotal()
        {
            var vm = Create(cargoCapacity: 100, currentCargo: 30, currentTritium: 20);
            Assert.Equal("50 / 100t", vm.CargoDisplay);
        }

        [Fact]
        public void CargoDisplay_NoTritiumYet_TotalIsJustCargo()
        {
            var vm = Create(cargoCapacity: 100, currentCargo: 30, currentTritium: null);
            Assert.Equal("30 / 100t", vm.CargoDisplay);
        }

        [Fact]
        public void CargoDisplay_CapacityUnknown_ShowsUnknownCapacity()
        {
            var vm = Create(cargoCapacity: null, currentCargo: 30);
            Assert.Equal("30t / Unknown", vm.CargoDisplay);
        }

        [Fact]
        public void CargoDisplay_CargoUnknown_ShowsUnknownCurrent()
        {
            var vm = Create(cargoCapacity: 100, currentCargo: null);
            Assert.Equal("Unknown / 100t", vm.CargoDisplay);
        }

        [Fact]
        public void CargoDisplay_NeitherKnown_IsUnknown()
        {
            Assert.Equal("Unknown", Create().CargoDisplay);
        }

        [Fact]
        public void TritiumDisplay_Positive_ShowsAmount()
        {
            Assert.Equal("20t tritium", Create(currentTritium: 20).TritiumDisplay);
        }

        [Fact]
        public void TritiumDisplay_ZeroOrNull_IsEmpty()
        {
            Assert.Equal(string.Empty, Create(currentTritium: 0).TritiumDisplay);
            Assert.Equal(string.Empty, Create(currentTritium: null).TritiumDisplay);
        }

        [Fact]
        public void AvailableCargoCapacity_BothKnown_IsDifference()
        {
            Assert.Equal(70, Create(cargoCapacity: 100, currentCargo: 30).AvailableCargoCapacity);
        }

        [Fact]
        public void AvailableCargoCapacity_EitherUnknown_IsNull()
        {
            Assert.Null(Create(cargoCapacity: null, currentCargo: 30).AvailableCargoCapacity);
            Assert.Null(Create(cargoCapacity: 100, currentCargo: null).AvailableCargoCapacity);
        }

        [Fact]
        public void CanBeEngineer_PositiveAvailableCapacity_IsTrue()
        {
            Assert.True(Create(cargoCapacity: 100, currentCargo: 30).CanBeEngineer);
        }

        [Fact]
        public void CanBeEngineer_ZeroAvailableCapacity_IsFalse()
        {
            Assert.False(Create(cargoCapacity: 100, currentCargo: 100).CanBeEngineer);
        }

        [Fact]
        public void CanBeEngineer_UnknownCapacity_IsFalse()
        {
            Assert.False(Create(cargoCapacity: null, currentCargo: 0).CanBeEngineer);
        }

        [Fact]
        public void LocationDisplay_SystemAndStation_CombinesThem()
        {
            Assert.Equal("Sol — Abraham Lincoln", Create(currentSystem: "Sol", currentStation: "Abraham Lincoln").LocationDisplay);
        }

        [Fact]
        public void LocationDisplay_SystemOnly_IsJustSystem()
        {
            Assert.Equal("Sol", Create(currentSystem: "Sol", currentStation: null).LocationDisplay);
        }

        [Fact]
        public void LocationDisplay_NeitherKnown_IsUnknown()
        {
            Assert.Equal("Unknown", Create().LocationDisplay);
        }

        [Fact]
        public void CarrierFuelDisplay_Known_ShowsOutOf1000()
        {
            Assert.Equal("600/1000t fuel", Create(carrierFuelLevel: 600).CarrierFuelDisplay);
        }

        [Fact]
        public void CarrierFuelDisplay_Unknown_IsEmpty()
        {
            Assert.Equal(string.Empty, Create().CarrierFuelDisplay);
        }

        [Fact]
        public void CarrierDisplay_NoNameOrSystem_ShowsNoneDetected()
        {
            Assert.Equal("None detected this session", Create().CarrierDisplay);
        }

        [Fact]
        public void CarrierDisplay_NameKnownLocationUnknown()
        {
            Assert.Equal("Serenity (location unknown)", Create(carrierName: "Serenity").CarrierDisplay);
        }

        [Fact]
        public void CarrierDisplay_NameUnknownLocationKnown_UsesUnnamedCarrier()
        {
            Assert.Equal("Unnamed carrier — Deciat", Create(carrierSystem: "Deciat").CarrierDisplay);
        }

        [Fact]
        public void CarrierDisplay_NameAndSystemAndBody_CombinesAll()
        {
            var vm = Create(carrierName: "Serenity", carrierSystem: "Deciat", carrierBody: "Deciat A");
            Assert.Equal("Serenity — Deciat, Deciat A", vm.CarrierDisplay);
        }

        [Fact]
        public void CarrierDisplay_NoBody_OmitsBody()
        {
            var vm = Create(carrierName: "Serenity", carrierSystem: "Deciat", carrierBody: null);
            Assert.Equal("Serenity — Deciat", vm.CarrierDisplay);
        }

        [Fact]
        public void IsCaptainAndIsEngineer_AreMutableAndRaisePropertyChanged()
        {
            var vm = Create();
            var raised = new List<string?>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            vm.IsCaptain = true;
            vm.IsEngineer = true;

            Assert.True(vm.IsCaptain);
            Assert.True(vm.IsEngineer);
            Assert.Contains(nameof(EliteInstanceViewModel.IsCaptain), raised);
            Assert.Contains(nameof(EliteInstanceViewModel.IsEngineer), raised);
        }
    }
}
