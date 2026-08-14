using RouteJumper.Services;
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

    /// <summary>
    /// CapLoopsToFitTimeBudget (SPEC §6.4) - caps the "ideal" loop count down so the refuel
    /// script's own estimated execution time (MacroPlayer.EstimateDurationMs) fits inside the
    /// real-world Cooldown window, rather than risking still being mid-script when the Captain's
    /// own next plot comes due and cancels it - which Auto Pilot's "panic mode" (§4.7) now treats
    /// as an immediate hard stop.
    /// </summary>
    public class ControlsViewModelCapLoopsToFitTimeBudgetTests
    {
        private const string RepeatScript = "REPEAT {TRITIUM_LOOPS}\n    UP\n    WAIT 1000\nEND";

        [Fact]
        public void ZeroIdealLoops_ReturnsZeroWithoutEstimatingAnything()
        {
            // A script that would throw/misbehave if ever actually estimated proves this path
            // short-circuits before touching the script at all.
            var capped = ControlsViewModel.CapLoopsToFitTimeBudget("REPEAT {TRITIUM_LOOPS}\n UP\nEND", idealLoops: 0, autoWaitMs: 0);

            Assert.Equal(0, capped);
        }

        [Fact]
        public void WellWithinBudget_ReturnsIdealLoopsUnchanged()
        {
            var capped = ControlsViewModel.CapLoopsToFitTimeBudget(RepeatScript, idealLoops: 5, autoWaitMs: 0);

            Assert.Equal(5, capped);
        }

        [Fact]
        public void FarExceedsBudget_CapsDownToExactlyWhatFits()
        {
            const int autoWaitMs = 0;
            var perLoopMs = MacroPlayer.EstimateDurationMs(RepeatScript.Replace("{TRITIUM_LOOPS}", "1"), autoWaitMs);
            var expectedCap = 285_000 / perLoopMs; // 4:45, integer division = floor

            var capped = ControlsViewModel.CapLoopsToFitTimeBudget(RepeatScript, idealLoops: 10_000, autoWaitMs);

            Assert.Equal(expectedCap, capped);
            Assert.True(capped < 10_000);
            // The capped script must genuinely fit - and one more loop must not.
            Assert.True(MacroPlayer.EstimateDurationMs(RepeatScript.Replace("{TRITIUM_LOOPS}", capped.ToString()), autoWaitMs) <= 285_000);
            Assert.True(MacroPlayer.EstimateDurationMs(RepeatScript.Replace("{TRITIUM_LOOPS}", (capped + 1).ToString()), autoWaitMs) > 285_000);
        }

        [Fact]
        public void EvenZeroLoopsWouldNotFit_ReturnsZero()
        {
            // The script's own fixed overhead (outside the REPEAT) alone already exceeds budget.
            var hugeFixedOverhead = "WAIT 999999\nREPEAT {TRITIUM_LOOPS}\n    UP\nEND";

            var capped = ControlsViewModel.CapLoopsToFitTimeBudget(hugeFixedOverhead, idealLoops: 50, autoWaitMs: 0);

            Assert.Equal(0, capped);
        }

        [Fact]
        public void PlaceholderNotDrivingAnyTimingCost_ReturnsIdealLoopsUnchanged()
        {
            // {TRITIUM_LOOPS} present, but not feeding anything that affects estimated duration
            // (e.g. only ever appears in a comment) - nothing meaningful to cap against.
            var capped = ControlsViewModel.CapLoopsToFitTimeBudget("# {TRITIUM_LOOPS}\nUP", idealLoops: 7, autoWaitMs: 0);

            Assert.Equal(7, capped);
        }
    }
}
