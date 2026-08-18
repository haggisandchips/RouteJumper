using RouteJumper.Models;

namespace RouteJumper.Services.Spansh
{
    /// <summary>The ship-derived numeric fields Spansh's /api/generic/route request needs, resolved by ShipBuildDerivation.Derive.</summary>
    internal readonly record struct ShipBuildParameters(
        double FuelPower,
        double FuelMultiplier,
        double OptimalMass,
        double BaseMass,
        double TankSize,
        double InternalTankSize,
        double MaxFuelPerJump,
        double RangeBoost,
        int SuperchargeMultiplier,
        int InjectionMultiplier);

    /// <summary>Outcome of ShipBuildDerivation.Derive - either a resolved ShipBuildParameters, or a human-readable reason it couldn't be resolved (surfaced verbatim as the Galaxy Plotter tab's own status message).</summary>
    internal sealed class ShipBuildDerivationResult
    {
        private ShipBuildDerivationResult(bool success, ShipBuildParameters? parameters, string? errorMessage)
        {
            Success = success;
            Parameters = parameters;
            ErrorMessage = errorMessage;
        }

        internal bool Success { get; }
        internal ShipBuildParameters? Parameters { get; }
        internal string? ErrorMessage { get; }

        internal static ShipBuildDerivationResult Ok(ShipBuildParameters parameters) => new(true, parameters, null);
        internal static ShipBuildDerivationResult Failed(string errorMessage) => new(false, null, errorMessage);
    }

    /// <summary>
    /// Turns a ship's LoadoutSnapshot into the ship-derived numeric fields Spansh's own
    /// /api/generic/route request needs (fuel_power, optimal_mass, base_mass, ...) - a pure,
    /// no-I/O reproduction of Spansh's own client-side parseSLEF() algorithm (confirmed against
    /// Spansh's production JS bundle), not a fresh derivation of Elite Dangerous' jump-range
    /// physics. See SlefSerializer for the SLEF envelope built from the same LoadoutSnapshot.
    /// </summary>
    internal static class ShipBuildDerivation
    {
        /// <summary>Spansh's own "supercharge_multiplier" for a regular neutron/white dwarf boost - shared with SpanshImportViewModel's Neutron Plotter tab so there's one authoritative copy.</summary>
        internal const int RegularSuperchargeMultiplier = 4;

        /// <summary>Spansh's own "supercharge_multiplier" for an overcharged FSD booster - see RegularSuperchargeMultiplier.</summary>
        internal const int OverchargeSuperchargeMultiplier = 6;

        /// <summary>Spansh's own default "injection_multiplier" - no known Frame Shift Drive variant overrides this.</summary>
        internal const int DefaultInjectionMultiplier = 2;

        internal static ShipBuildDerivationResult Derive(LoadoutSnapshot loadout)
        {
            LoadoutModule? fsdModule = null;
            foreach (var module in loadout.Modules)
            {
                if (string.Equals(module.Slot, "FrameShiftDrive", StringComparison.OrdinalIgnoreCase))
                {
                    fsdModule = module;
                    break;
                }
            }

            if (fsdModule is not { } fsd)
            {
                return ShipBuildDerivationResult.Failed("No Frame Shift Drive found in this ship's loadout.");
            }

            if (!FrameShiftDriveCatalog.TryGetStandard(fsd.Item, out var stats))
            {
                return ShipBuildDerivationResult.Failed($"Unrecognised Frame Shift Drive module ({fsd.Item}).");
            }

            var optimalMass = stats.OptMass;
            var maxFuelPerJump = stats.MaxFuel;
            if (fsd.Engineering is { } engineering)
            {
                foreach (var modifier in engineering.Modifiers)
                {
                    if (string.Equals(modifier.Label, "FSDOptimalMass", StringComparison.OrdinalIgnoreCase))
                    {
                        optimalMass = modifier.Value;
                    }
                    else if (string.Equals(modifier.Label, "MaxFuelPerJump", StringComparison.OrdinalIgnoreCase))
                    {
                        maxFuelPerJump = modifier.Value;
                    }
                }
            }

            var superchargeMultiplier = fsd.Item.EndsWith("_overchargebooster_mkii", StringComparison.OrdinalIgnoreCase)
                ? OverchargeSuperchargeMultiplier
                : RegularSuperchargeMultiplier;

            var rangeBoost = FrameShiftDriveCatalog.TryGetGuardianBoosterJumpBoost(loadout.Modules, out var jumpBoost) ? jumpBoost : 0;

            var parameters = new ShipBuildParameters(
                FuelPower: stats.FuelPower,
                FuelMultiplier: stats.FuelMul,
                OptimalMass: optimalMass,
                BaseMass: loadout.UnladenMass + loadout.FuelCapacityReserve,
                TankSize: loadout.FuelCapacityMain,
                InternalTankSize: loadout.FuelCapacityReserve,
                MaxFuelPerJump: maxFuelPerJump,
                RangeBoost: rangeBoost,
                SuperchargeMultiplier: superchargeMultiplier,
                InjectionMultiplier: DefaultInjectionMultiplier);

            return ShipBuildDerivationResult.Ok(parameters);
        }
    }
}
