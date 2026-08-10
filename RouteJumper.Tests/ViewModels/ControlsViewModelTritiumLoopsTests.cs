using RouteJumper.ViewModels;
using Xunit;

namespace RouteJumper.Tests.ViewModels
{
    /// <summary>
    /// ComputeTritiumLoops (SPEC §6.4/criterion 38) - widened to internal purely for direct
    /// testing of this pure, divide-by-capacity calculation without needing a live Auto
    /// Pilot run or a running game instance.
    /// </summary>
    public class ControlsViewModelTritiumLoopsTests
    {
        private static EliteInstanceViewModel Instance(int? cargoCapacity, int? currentTritium, int? carrierFuelLevel) => new(
            processId: 1,
            commanderName: "Jameson",
            fid: "F1",
            journalFileName: "Journal.log",
            windowHandle: (IntPtr)1,
            windowPosition: "(0,0)",
            monitorInfo: "Monitor",
            cargoCapacity: cargoCapacity,
            currentCargo: 0,
            currentTritium: currentTritium,
            currentSystem: null,
            currentStation: null,
            carrierName: null,
            carrierSystem: null,
            carrierBody: null,
            journalFilePath: null,
            carrierId: null,
            carrierFuelLevel: carrierFuelLevel);

        [Fact]
        public void NullInstance_ReturnsZero()
        {
            Assert.Equal(0, ControlsViewModel.ComputeTritiumLoops(null));
        }

        [Fact]
        public void UnknownCargoCapacity_ReturnsZero()
        {
            var instance = Instance(cargoCapacity: null, currentTritium: 0, carrierFuelLevel: 500);
            Assert.Equal(0, ControlsViewModel.ComputeTritiumLoops(instance));
        }

        [Fact]
        public void ZeroCargoCapacity_ReturnsZero()
        {
            var instance = Instance(cargoCapacity: 0, currentTritium: 0, carrierFuelLevel: 500);
            Assert.Equal(0, ControlsViewModel.ComputeTritiumLoops(instance));
        }

        [Fact]
        public void UnknownCarrierFuelLevel_ReturnsZeroRatherThanTreatingAsEmpty()
        {
            // The historical bug this guards against: an unknown fuel level must never be
            // silently treated as 0 (empty), which would wildly overstate loops needed.
            var instance = Instance(cargoCapacity: 100, currentTritium: 0, carrierFuelLevel: null);
            Assert.Equal(0, ControlsViewModel.ComputeTritiumLoops(instance));
        }

        [Fact]
        public void FullDepotAndHoldAlreadyTopped_ReturnsZero()
        {
            var instance = Instance(cargoCapacity: 100, currentTritium: 100, carrierFuelLevel: 1000);
            Assert.Equal(0, ControlsViewModel.ComputeTritiumLoops(instance));
        }

        [Fact]
        public void EmptyDepotAndHold_ComputesExpectedLoopCount()
        {
            // capacity 100, carrier needs 1000, onboard 0: carrierNeeded=1000, totalNeeded=1000+100-0=1100, ceil(1100/100)=11
            var instance = Instance(cargoCapacity: 100, currentTritium: 0, carrierFuelLevel: 0);
            Assert.Equal(11, ControlsViewModel.ComputeTritiumLoops(instance));
        }

        [Fact]
        public void PartialFuelAndOnboardTritium_NettedOffTotalNeeded()
        {
            // capacity 50, carrierNeeded = 1000-900=100, totalNeeded = 100+50-20=130, ceil(130/50)=3
            var instance = Instance(cargoCapacity: 50, currentTritium: 20, carrierFuelLevel: 900);
            Assert.Equal(3, ControlsViewModel.ComputeTritiumLoops(instance));
        }

        [Fact]
        public void OnboardTritiumExceedsNeeded_ReturnsZeroNotNegative()
        {
            // capacity 100, carrierNeeded = 1000-950=50, totalNeeded = max(0, 50+100-500) -> 0
            var instance = Instance(cargoCapacity: 100, currentTritium: 500, carrierFuelLevel: 950);
            Assert.Equal(0, ControlsViewModel.ComputeTritiumLoops(instance));
        }

        [Fact]
        public void FuelAboveDepotCapacity_CarrierNeededClampsToZero()
        {
            // Defensive: FuelLevel is documented 0-1000t, but the formula should not go negative
            // even if it somehow exceeded 1000.
            var instance = Instance(cargoCapacity: 100, currentTritium: 0, carrierFuelLevel: 1200);
            Assert.Equal(1, ControlsViewModel.ComputeTritiumLoops(instance)); // ceil((0+100-0)/100) = 1, own hold still needs topping off
        }
    }
}
