namespace RouteJumper.Services
{
    /// <summary>
    /// Maps a journal `StarClass` code (FSDTarget, NavRoute.json, Scan's own StarType field) to
    /// the same human-readable format EDSM's own `subType` field uses (with its own redundant
    /// trailing "Star" word dropped too - see EdsmStarSystemLookupService.StripRedundantStarWord -
    /// so a Star Type cell reads consistently regardless of which source resolved it, and the
    /// word "Star" is never repeated a second time inside a column already headed "Star Type").
    ///
    /// This mapping is also the app's single source of truth for what gets *cached*: the journal
    /// path caches the raw code as-is, and the EDSM path (which never returns a code, only
    /// display text) recovers the same code from EDSM's text via <see cref="TryGetCode"/> before
    /// caching it - see EdsmStarSystemLookupService. Both paths therefore always render through
    /// this same table at read time, so a later fix/addition here retroactively improves every
    /// already-cached system, with nothing to invalidate.
    ///
    /// The main-sequence and white-dwarf mappings below were confirmed against real EDSM API
    /// responses (O/B/A/F/G/K/M via named real systems - Altair, Bellatrix/Achenar, Procyon, and
    /// Wolf 359 among them - each returning exactly the "{code} ({colour}) Star" pattern this
    /// drops the trailing word from; O itself follows that same confirmed pattern by inference,
    /// not independently confirmed). The brown dwarf classes (L, T, Y) follow the identical
    /// "{code} ({descriptor}) Star" shape - L and Y independently confirmed live against Luhman 16
    /// ("L (Brown dwarf) Star") and WISE 0855-0714 ("Y (Brown dwarf) Star"), T by the same pattern.
    /// "Neutron"/"Black Hole"/"Supermassive Black Hole" are
    /// unambiguous, universally-agreed community terms (the latter two confirmed for real via
    /// Sagittarius A* itself, and neither actually contains the word "Star" to begin with).
    /// White dwarf subclasses (DA, DB, DQ, ...) all follow one confirmed mechanical pattern - the
    /// parenthetical simply echoes the journal code back - so that's handled generically rather
    /// than enumerated; confirmed against EDSM's own advanced-search category labels (e.g.
    /// "White Dwarf (DA) Star", "White Dwarf (DAV) Star").
    ///
    /// T Tauri, Herbig Ae/Be, and the Wolf-Rayet family are likewise confirmed against EDSM's own
    /// advanced-search category labels ("T Tauri Star", "Herbig Ae/Be Star", "Wolf-Rayet Star" /
    /// "Wolf-Rayet N/NC/C/O Star"). The carbon-star family (C, CS, CN, CJ, CH, CHd) and MS/S are
    /// also confirmed there as bare "{code} Star" labels - i.e. EDSM's own display text for these
    /// already *is* the code, so the mapping is an identity, kept explicit (rather than falling
    /// through to the raw-code fallback below by accident) so the reverse lookup has something
    /// deliberate to match against.
    ///
    /// Anything not covered here (the underscore-suffixed giant/supergiant codes some journal
    /// documentation lists, and any other not-yet-confirmed exotic) falls back to the raw journal
    /// code rather than guessing at an unverified format - still useful information, just not
    /// reformatted.
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
            ["L"] = "L (Brown dwarf)",
            ["T"] = "T (Brown dwarf)",
            ["Y"] = "Y (Brown dwarf)",
            ["N"] = "Neutron",
            ["H"] = "Black Hole",
            ["SupermassiveBlackHole"] = "Supermassive Black Hole",

            ["TTS"] = "T Tauri",
            ["AeBe"] = "Herbig Ae/Be",

            ["W"] = "Wolf-Rayet",
            ["WN"] = "Wolf-Rayet N",
            ["WNC"] = "Wolf-Rayet NC",
            ["WC"] = "Wolf-Rayet C",
            ["WO"] = "Wolf-Rayet O",

            ["C"] = "C",
            ["CS"] = "CS",
            ["CN"] = "CN",
            ["CJ"] = "CJ",
            ["CH"] = "CH",
            ["CHd"] = "CHd",
            ["MS"] = "MS",
            ["S"] = "S",
        };

        /// <summary>Reverse of <see cref="KnownNames"/> - built once, matched case-insensitively (see TryGetCode). Values in KnownNames are unique by construction, so this inversion is lossless.</summary>
        private static readonly Dictionary<string, string> DisplayNameToCode =
            KnownNames.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

        private const string WhiteDwarfPrefix = "White Dwarf (";
        private const string WhiteDwarfSuffix = ")";

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

        /// <summary>
        /// Recovers the canonical journal `StarClass` code for an already-formatted display name
        /// (as EDSM's own `subType` field returns, minus its redundant trailing "Star" word - see
        /// EdsmStarSystemLookupService.StripRedundantStarWord) - the reverse of
        /// <see cref="ToDisplayName"/>, needed because EDSM never returns a raw code itself, only
        /// this display text. Returns false for anything not recognized (an unmapped/exotic
        /// EDSM description) - the caller falls back to caching the display text itself, the same
        /// safe degrade an unrecognized journal code already gets from ToDisplayName.
        /// </summary>
        internal static bool TryGetCode(string displayName, out string code)
        {
            if (DisplayNameToCode.TryGetValue(displayName, out var known))
            {
                code = known;
                return true;
            }

            if (displayName.StartsWith(WhiteDwarfPrefix, StringComparison.OrdinalIgnoreCase)
                && displayName.EndsWith(WhiteDwarfSuffix, StringComparison.Ordinal))
            {
                code = displayName[WhiteDwarfPrefix.Length..^WhiteDwarfSuffix.Length];
                return true;
            }

            code = string.Empty;
            return false;
        }
    }
}
