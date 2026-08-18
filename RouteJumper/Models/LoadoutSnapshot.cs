namespace RouteJumper.Models
{
    /// <summary>
    /// The subset of a ship's most recent Loadout journal event needed to build a Spansh SLEF
    /// ship build (Services/Spansh/SlefSerializer, ShipBuildDerivation) for the Galaxy Plotter tab
    /// - distinct from EliteInstanceScanner.JournalSummary, which only keeps the two scalar
    /// Loadout fields (CargoCapacity, MaxJumpRange) the rest of the app already needs. See
    /// EliteInstanceScanner.ReadLoadoutSnapshot.
    /// </summary>
    public readonly record struct LoadoutSnapshot(
        string Ship,
        IReadOnlyList<LoadoutModule> Modules,
        double UnladenMass,
        double FuelCapacityMain,
        double FuelCapacityReserve);

    /// <summary>One entry of a Loadout event's own Modules array.</summary>
    public readonly record struct LoadoutModule(
        string Slot,
        string Item,
        LoadoutModuleEngineering? Engineering);

    /// <summary>A module's Engineering block, present only when that module has been engineered.</summary>
    public readonly record struct LoadoutModuleEngineering(
        string? BlueprintName,
        int Level,
        double Quality,
        string? ExperimentalEffect,
        IReadOnlyList<LoadoutModuleModifier> Modifiers);

    /// <summary>One entry of an engineered module's own Modifiers array - only Label/Value are needed here (see ShipBuildDerivation), OriginalValue/LessIsGood are not captured.</summary>
    public readonly record struct LoadoutModuleModifier(string Label, double Value);
}
