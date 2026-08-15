using System.IO;
using System.Text.Json;
using RouteJumper.Models;
using RouteJumper.Services.Logging;

namespace RouteJumper.Services
{
    /// <summary>
    /// Shared FSDTarget/NavRoute.json opportunistic cache-seeding logic for the Route tab's
    /// Distance/Star Type columns (SPEC §4.9) - used by both ShipRouteJournalWatcher (Ship mode)
    /// and CarrierRouteJournalWatcher (Fleet Carrier mode, watching the Captain's own ship
    /// targeting independently of the carrier's own jump events), since a commander's FSDTarget/
    /// NavRoute.json behave identically regardless of which mode is tracking their route. Entirely
    /// opportunistic - nothing about either watcher's own route-progress tracking depends on any
    /// of this; a missing/null lookup service, or a NavRoute.json that can't be read right now,
    /// are both silent, safe no-ops.
    /// </summary>
    internal static class StarSystemCacheSeeder
    {
        /// <summary>
        /// Given an already-parsed FSDTarget event's root element and the targeted system's own
        /// name (the caller already extracted this for its own row-event purposes too) - seeds
        /// Star Type from the event's own StarClass field, and/or triggers a NavRoute.json read
        /// (see <see cref="SeedFromNavRoute"/>) if RemainingJumpsInRoute is present. A no-op if
        /// <paramref name="lookupService"/> is null.
        /// </summary>
        public static void SeedFromFsdTarget(
            JsonElement fsdTargetRoot, string targetSystem, string journalPath, IStarSystemLookupService? lookupService)
        {
            if (lookupService is null)
            {
                return;
            }

            if (fsdTargetRoot.TryGetProperty("StarClass", out var scEl) && scEl.GetString() is { } starClass)
            {
                var starType = StarClassNames.ToDisplayName(starClass);
                Log.Info("Journal", $"FSDTarget seeded star type for \"{targetSystem}\": {starType}.");
                lookupService.SeedStarType(targetSystem, starType);
            }

            // Present only while a multi-jump in-game route is actively plotted - its presence
            // (any value, including 0) is the signal that NavRoute.json is fresh and worth
            // reading right now, which hands over exact coordinates (and each hop's own star) for
            // every system in that plotted route - including procedurally-generated systems EDSM
            // may have no record of at all, exactly the neutron-highway case NavRoute.json alone
            // can't help the route-*tracking* engine with (SPEC §5.7/§8.3), but can help this
            // coordinate lookup with.
            if (fsdTargetRoot.TryGetProperty("RemainingJumpsInRoute", out _))
            {
                SeedFromNavRoute(journalPath, lookupService);
            }
        }

        /// <summary>
        /// Reads NavRoute.json (beside the journal file, same convention as every other Frontier
        /// companion file) and seeds coordinates/star-type for every system in its "Route" array.
        /// A silent no-op if the file can't be read/parsed right now (e.g. the game is mid-write
        /// to it - a transient, retriable condition: the next qualifying FSDTarget simply tries
        /// again). Deliberately synchronous, small file I/O on whatever thread called it (always a
        /// journal watcher's own background FileSystemWatcher thread, never the UI thread).
        /// </summary>
        public static void SeedFromNavRoute(string journalPath, IStarSystemLookupService lookupService)
        {
            var directory = Path.GetDirectoryName(journalPath);
            if (directory is null)
            {
                return;
            }

            try
            {
                using var stream = new FileStream(
                    Path.Combine(directory, "NavRoute.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var doc = JsonDocument.Parse(stream);

                if (!doc.RootElement.TryGetProperty("Route", out var route) || route.ValueKind != JsonValueKind.Array)
                {
                    return;
                }

                var seededCount = 0;
                foreach (var entry in route.EnumerateArray())
                {
                    if (!entry.TryGetProperty("StarSystem", out var nameEl) || nameEl.GetString() is not { } systemName)
                    {
                        continue;
                    }

                    if (entry.TryGetProperty("StarPos", out var posEl)
                        && posEl.ValueKind == JsonValueKind.Array
                        && posEl.GetArrayLength() == 3)
                    {
                        var pos = posEl.EnumerateArray().Select(e => e.GetDouble()).ToArray();
                        lookupService.SeedCoordinates(systemName, new GalacticCoordinates(pos[0], pos[1], pos[2]));
                        seededCount++;
                    }

                    if (entry.TryGetProperty("StarClass", out var classEl) && classEl.GetString() is { } starClass)
                    {
                        lookupService.SeedStarType(systemName, StarClassNames.ToDisplayName(starClass));
                    }
                }

                // One summary line, not one per system - a plotted neutron highway can easily
                // name dozens of systems in a single NavRoute.json, and only the count (what this
                // pass actually accomplished) is useful here; each individual system's own
                // resolved value is already visible via the Route tab itself.
                Log.Info("Journal", $"NavRoute.json seeded {seededCount} system(s).");
            }
            catch (IOException)
            {
                // Expected/transient - e.g. the game is mid-write to the file right now. Not
                // logged: the next qualifying FSDTarget/NavRoute line simply tries again, and this
                // can happen often enough during active route (re)plotting that logging every miss
                // would just be noise, not a real diagnostic signal.
            }
            catch (JsonException)
            {
            }
        }
    }
}
