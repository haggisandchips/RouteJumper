using RouteJumper.Models;
using RouteJumper.Services.Spansh;
using Xunit;

namespace RouteJumper.Tests.Services.Spansh
{
    public class ShipBuildDerivationTests
    {
        private static LoadoutSnapshot Loadout(params LoadoutModule[] modules) =>
            new("anaconda", modules, UnladenMass: 1000, FuelCapacityMain: 32, FuelCapacityReserve: 0.63);

        [Fact]
        public void Derive_NoFrameShiftDriveInModules_Fails()
        {
            var loadout = Loadout(new LoadoutModule("MainEngines", "int_engine_size6_class5", null));

            var result = ShipBuildDerivation.Derive(loadout);

            Assert.False(result.Success);
            Assert.Contains("No Frame Shift Drive", result.ErrorMessage);
        }

        [Fact]
        public void Derive_UnrecognisedFrameShiftDriveItem_Fails()
        {
            var loadout = Loadout(new LoadoutModule("FrameShiftDrive", "int_hyperdrive_totally_made_up", null));

            var result = ShipBuildDerivation.Derive(loadout);

            Assert.False(result.Success);
            Assert.Contains("Unrecognised", result.ErrorMessage);
        }

        [Fact]
        public void Derive_StandardFsd_ResolvesStockStatsAndDefaultMultipliers()
        {
            // Int_Hyperdrive_Size6_Class5 stock stats, vendored from EDCD/coriolis-data.
            var loadout = Loadout(new LoadoutModule("FrameShiftDrive", "Int_Hyperdrive_Size6_Class5", null));

            var result = ShipBuildDerivation.Derive(loadout);

            Assert.True(result.Success);
            var p = result.Parameters!.Value;
            Assert.Equal(ShipBuildDerivation.RegularSuperchargeMultiplier, p.SuperchargeMultiplier);
            Assert.Equal(ShipBuildDerivation.DefaultInjectionMultiplier, p.InjectionMultiplier);
            Assert.Equal(1000 + 0.63, p.BaseMass);
            Assert.Equal(32, p.TankSize);
            Assert.Equal(0.63, p.InternalTankSize);
            Assert.Equal(0, p.RangeBoost);
        }

        [Fact]
        public void Derive_OverchargeBoosterMkIIFsd_UsesOverchargeSuperchargeMultiplier()
        {
            var loadout = Loadout(new LoadoutModule(
                "FrameShiftDrive", "int_hyperdrive_overcharge_size8_class5_overchargebooster_mkii", null));

            var result = ShipBuildDerivation.Derive(loadout);

            Assert.True(result.Success);
            var p = result.Parameters!.Value;
            Assert.Equal(ShipBuildDerivation.OverchargeSuperchargeMultiplier, p.SuperchargeMultiplier);
            // Confirmed live against Spansh's own bundled JS - exact stock stats for this module.
            Assert.Equal(4670, p.OptimalMass);
            Assert.Equal(6.8, p.MaxFuelPerJump);
            Assert.Equal(0.011, p.FuelMultiplier);
            Assert.Equal(2.5025, p.FuelPower);
        }

        [Fact]
        public void Derive_EngineeredFsd_OverridesOptimalMassAndMaxFuelPerJumpFromModifiers()
        {
            var engineering = new LoadoutModuleEngineering(
                "FSD_LongRange", 5, 0.98, null,
                new List<LoadoutModuleModifier>
                {
                    new("FSDOptimalMass", 1150.0),
                    new("MaxFuelPerJump", 9.9),
                    new("FSDHeatRate", 180.0)
                });
            var loadout = Loadout(new LoadoutModule("FrameShiftDrive", "Int_Hyperdrive_Size6_Class5", engineering));

            var result = ShipBuildDerivation.Derive(loadout);

            Assert.True(result.Success);
            var p = result.Parameters!.Value;
            Assert.Equal(1150.0, p.OptimalMass);
            Assert.Equal(9.9, p.MaxFuelPerJump);
        }

        [Fact]
        public void Derive_GuardianFsdBoosterFitted_ResolvesRangeBoostByClass()
        {
            var loadout = Loadout(
                new LoadoutModule("FrameShiftDrive", "Int_Hyperdrive_Size6_Class5", null),
                new LoadoutModule("Slot08_Size1", "Int_GuardianFSDBooster_Size5", null));

            var result = ShipBuildDerivation.Derive(loadout);

            Assert.True(result.Success);
            Assert.Equal(10.5, result.Parameters!.Value.RangeBoost);
        }

        [Fact]
        public void Derive_NoGuardianFsdBooster_RangeBoostIsZero()
        {
            var loadout = Loadout(new LoadoutModule("FrameShiftDrive", "Int_Hyperdrive_Size6_Class5", null));

            var result = ShipBuildDerivation.Derive(loadout);

            Assert.Equal(0, result.Parameters!.Value.RangeBoost);
        }
    }
}
