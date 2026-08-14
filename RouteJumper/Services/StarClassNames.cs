namespace RouteJumper.Services
{
    /// <summary>
    /// Maps a journal `StarClass` code (FSDTarget, NavRoute.json, Scan's own StarType field) to
    /// the same human-readable format EDSM's own `subType` field uses (with its own redundant
    /// trailing "Star" word dropped too - see EdsmStarSystemLookupService.StripRedundantStarWord -
    /// so a Star Type cell reads consistently regardless of which source resolved it, and the
    /// word "Star" is never repeated a second time inside a column already headed "Star Type").
    ///
    /// The main-sequence and white-dwarf mappings below were confirmed against real EDSM API
    /// responses (O/B/A/F/G/K/M via named real systems - Altair, Bellatrix/Achenar, Procyon, and
    /// Wolf 359 among them - each returning exactly the "{code} ({colour}) Star" pattern this
    /// drops the trailing word from; O itself follows that same confirmed pattern by inference,
    /// not independently confirmed). "Neutron"/"Black Hole"/"Supermassive Black Hole" are
    /// unambiguous, universally-agreed community terms (the latter two confirmed for real via
    /// Sagittarius A* itself, and neither actually contains the word "Star" to begin with).
    /// White dwarf subclasses (DA, DB, DQ, ...) all follow one confirmed mechanical pattern - the
    /// parenthetical simply echoes the journal code back - so that's handled generically rather
    /// than enumerated.
    ///
    /// Anything not covered here (Wolf-Rayet variants, T Tauri, carbon stars, and other rarer
    /// exotics) falls back to the raw journal code rather than guessing at an unverified format -
    /// still useful information, just not reformatted.
    /// </summary>
    internal static class StarClassNames
    {
        private static readonly Dictionary<string, string> KnownNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["O"] = "O (Blue)",
            ["B"] = "B (Blue-White)",
            ["A"] = "A (Blue-White)",
            ["F"] = "F (White)",
            ["G"] = "G (White-Yellow)",
            ["K"] = "K (Yellow-Orange)",
            ["M"] = "M (Red dwarf)",
            ["N"] = "Neutron",
            ["H"] = "Black Hole",
            ["SupermassiveBlackHole"] = "Supermassive Black Hole",
        };

        public static string ToDisplayName(string starClass)
        {
            if (KnownNames.TryGetValue(starClass, out var name))
            {
                return name;
            }

            if (starClass.StartsWith("D", StringComparison.OrdinalIgnoreCase))
            {
                return $"White Dwarf ({starClass})";
            }

            return starClass;
        }
    }
}
