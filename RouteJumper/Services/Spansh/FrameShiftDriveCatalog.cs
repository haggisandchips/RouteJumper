using System.Reflection;
using System.Text.Json;
using RouteJumper.Models;

namespace RouteJumper.Services.Spansh
{
    /// <summary>Stock stats for one Frame Shift Drive module symbol - see FrameShiftDriveCatalog.</summary>
    internal readonly record struct FrameShiftDriveStats(double OptMass, double MaxFuel, double FuelMul, double FuelPower);

    /// <summary>
    /// Static Frame Shift Drive / Guardian FSD Booster stat lookups the Galaxy Plotter tab needs
    /// (Services\Spansh\ShipBuildDerivation) to turn a ship's Loadout event into Spansh's own
    /// /api/generic/route request fields - reproduces the same lookup Spansh's own client-side
    /// parseSLEF() does against its own bundled module data.
    ///
    /// Backed by two embedded JSON resources (Data\FrameShiftDrives.json,
    /// Data\GuardianFsdBoosters.json), vendored and trimmed (symbol/optmass/maxfuel/fuelmul/
    /// fuelpower, and symbol/class/jumpboost respectively - cost/mass/rating/etc. dropped) from
    /// EDCD's own public, community-maintained module data:
    /// https://raw.githubusercontent.com/EDCD/coriolis-data/master/modules/standard/frame_shift_drive.json
    /// https://raw.githubusercontent.com/EDCD/coriolis-data/master/modules/internal/guardian_fsd_booster.json
    /// Confirmed to already include every overcharge-FSD variant (including the
    /// "..._overchargebooster_mkii" combo module) with stats matching Spansh's own bundled data
    /// exactly, so - unlike Spansh's own client, which needs a small hand-maintained override
    /// table for that one module - a single lookup into the vendored standard table covers every
    /// known Frame Shift Drive.
    /// </summary>
    internal static class FrameShiftDriveCatalog
    {
        private static readonly Lazy<IReadOnlyDictionary<string, FrameShiftDriveStats>> StandardFsds = new(LoadStandardFsds);
        private static readonly Lazy<IReadOnlyDictionary<string, double>> GuardianBoosterJumpBoosts = new(LoadGuardianBoosterJumpBoosts);

        /// <summary>Case-insensitive lookup by a Loadout module's own Item id (e.g. "Int_Hyperdrive_Size6_Class5").</summary>
        internal static bool TryGetStandard(string item, out FrameShiftDriveStats stats) =>
            StandardFsds.Value.TryGetValue(item, out stats);

        /// <summary>
        /// Scans <paramref name="modules"/> for an Item matching "^int_guardianfsdbooster"
        /// (case-insensitive) and returns its jump-range boost (ly) if found and recognised.
        /// False/0 if no Guardian FSD Booster is fitted, or its Item isn't in the vendored table.
        /// </summary>
        internal static bool TryGetGuardianBoosterJumpBoost(IReadOnlyList<LoadoutModule> modules, out double jumpBoost)
        {
            foreach (var module in modules)
            {
                if (module.Item.StartsWith("int_guardianfsdbooster", StringComparison.OrdinalIgnoreCase)
                    && GuardianBoosterJumpBoosts.Value.TryGetValue(module.Item, out jumpBoost))
                {
                    return true;
                }
            }

            jumpBoost = 0;
            return false;
        }

        private static IReadOnlyDictionary<string, FrameShiftDriveStats> LoadStandardFsds()
        {
            var result = new Dictionary<string, FrameShiftDriveStats>(StringComparer.OrdinalIgnoreCase);

            using var doc = LoadEmbeddedJson("FrameShiftDrives.json");
            if (doc is null || !doc.RootElement.TryGetProperty("fsd", out var fsd) || fsd.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var entry in fsd.EnumerateArray())
            {
                if (entry.TryGetProperty("symbol", out var symbolEl) && symbolEl.GetString() is { } symbol
                    && entry.TryGetProperty("optmass", out var optMassEl) && optMassEl.TryGetDouble(out var optMass)
                    && entry.TryGetProperty("maxfuel", out var maxFuelEl) && maxFuelEl.TryGetDouble(out var maxFuel)
                    && entry.TryGetProperty("fuelmul", out var fuelMulEl) && fuelMulEl.TryGetDouble(out var fuelMul)
                    && entry.TryGetProperty("fuelpower", out var fuelPowerEl) && fuelPowerEl.TryGetDouble(out var fuelPower))
                {
                    // Indexer assignment (last wins), not Add/ToDictionary - the vendored source
                    // data has been observed to carry the occasional duplicate symbol with
                    // near-identical stats; silently keeping the last one is safer than throwing.
                    result[symbol] = new FrameShiftDriveStats(optMass, maxFuel, fuelMul, fuelPower);
                }
            }

            return result;
        }

        private static IReadOnlyDictionary<string, double> LoadGuardianBoosterJumpBoosts()
        {
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            using var doc = LoadEmbeddedJson("GuardianFsdBoosters.json");
            if (doc is null || !doc.RootElement.TryGetProperty("gfsb", out var gfsb) || gfsb.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var entry in gfsb.EnumerateArray())
            {
                if (entry.TryGetProperty("symbol", out var symbolEl) && symbolEl.GetString() is { } symbol
                    && entry.TryGetProperty("jumpboost", out var jumpBoostEl) && jumpBoostEl.TryGetDouble(out var jumpBoost))
                {
                    result[symbol] = jumpBoost;
                }
            }

            return result;
        }

        private static JsonDocument? LoadEmbeddedJson(string fileName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = $"RouteJumper.Services.Spansh.Data.{fileName}";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            return stream is null ? null : JsonDocument.Parse(stream);
        }
    }
}
