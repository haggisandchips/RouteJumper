namespace RouteJumper.Services
{
    /// <summary>
    /// Maps a journal `StarClass` code (FSDTarget, NavRoute.json, Scan's own StarType field) to
    /// the same human-readable format EDSM's own `subType` field uses, so a Star Type cell reads
    /// consistently regardless of which source resolved it (see RouteRowEnrichmentService).
    ///
    /// The main-sequence and white-dwarf mappings below were confirmed against real EDSM API
    /// responses (O/B/A/F/G/K/M via named real systems - Altair, Bellatrix/Achenar, Procyon, and
    /// Wolf 359 among them - each returning exactly the pattern used here; O itself follows that
    /// same confirmed "{code} ({colour}) Star" pattern by inference, not independently confirmed).
    /// "Neutron Star"/"Black Hole"/"Supermassive Black Hole" are unambiguous, universally-agreed
    /// community terms (also confirmed for the latter via Sagittarius A* itself). White dwarf
    /// subclasses (DA, DB, DQ, ...) all follow one confirmed mechanical pattern - the parenthetical
    /// simply echoes the journal code back - so that's handled generically rather than enumerated.
    ///
    /// Anything not covered here (Wolf-Rayet variants, T Tauri, carbon stars, and other rarer
    /// exotics) falls back to the raw journal code rather than guessing at an unverified format -
    /// still useful information, just not reformatted.
    /// </summary>
    internal static class StarClassNames
    {
        private static readonly Dictionary<string, string> KnownNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["O"] = "O (Blue) Star",
            ["B"] = "B (Blue-White) Star",
            ["A"] = "A (Blue-White) Star",
            ["F"] = "F (White) Star",
            ["G"] = "G (White-Yellow) Star",
            ["K"] = "K (Yellow-Orange) Star",
            ["M"] = "M (Red dwarf) Star",
            ["N"] = "Neutron Star",
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
                return $"White Dwarf ({starClass}) Star";
            }

            return starClass;
        }
    }
}
