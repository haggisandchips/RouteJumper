using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using RouteJumper.Services.Logging;
using RouteJumper.ViewModels;

namespace RouteJumper.Services
{
    /// <summary>
    /// Finds running EliteDangerous64.exe instances and, for each one, works out which journal
    /// file it owns (there's no direct OS-level link between a process and a journal file, so
    /// this matches each process to the journal whose Fileheader timestamp is closest to the
    /// process's start time), then reads that journal's Commander event for FID/name and reads
    /// the process's window position/monitor via Win32. The folder searched for journal files
    /// comes from <see cref="AppConfigStore.JournalDirectory"/>, re-read on every scan.
    /// </summary>
    public class EliteInstanceScanner : IEliteInstanceScanner
    {
        private const string ProcessName = "EliteDangerous64";
        private static readonly TimeSpan JournalMatchTolerance = TimeSpan.FromMinutes(5);

        private readonly AppConfigStore _config;

        public EliteInstanceScanner(AppConfigStore config)
        {
            _config = config;
        }

        public Task<IReadOnlyList<EliteInstanceViewModel>> ScanAsync() => Task.Run(Scan);

        private IReadOnlyList<EliteInstanceViewModel> Scan()
        {
            using var processes = new ProcessList(Process.GetProcessesByName(ProcessName));
            if (processes.Items.Count == 0)
            {
                Log.Debug("Scan", "No running Elite Dangerous instances found.");
                return Array.Empty<EliteInstanceViewModel>();
            }

            // Re-read fresh every scan (AppConfigStore never caches) - a routejumper.conf edited
            // by hand while the app is running takes effect on the very next Refresh.
            var journalCandidates = GetJournalFilesWithTimestamps(_config.JournalDirectory);
            var assignedJournals = new HashSet<string>();
            var results = new List<EliteInstanceViewModel>();

            foreach (var process in processes.Items.OrderBy(p => SafeStartTime(p)))
            {
                var journalPath = MatchJournal(process, journalCandidates, assignedJournals);
                if (journalPath != null)
                {
                    assignedJournals.Add(journalPath);
                }

                results.Add(BuildInstanceInfo(process, journalPath));
            }

            Log.Debug("Scan", $"Found {results.Count} running Elite Dangerous instance(s).");
            return results;
        }

        private static DateTime? SafeStartTime(Process process)
        {
            try
            {
                return process.StartTime;
            }
            catch
            {
                return null;
            }
        }

        private static List<(string Path, DateTime TimestampUtc)> GetJournalFilesWithTimestamps(string journalDirectory)
        {
            if (!Directory.Exists(journalDirectory))
            {
                return new List<(string, DateTime)>();
            }

            var result = new List<(string, DateTime)>();
            foreach (var path in Directory.GetFiles(journalDirectory, "Journal.*.log"))
            {
                var timestamp = TryReadFileheaderTimestampUtc(path);
                if (timestamp.HasValue)
                {
                    result.Add((path, timestamp.Value));
                }
            }

            return result;
        }

        internal static string? MatchJournal(
            Process process,
            List<(string Path, DateTime TimestampUtc)> candidates,
            HashSet<string> assigned)
        {
            var startTime = SafeStartTime(process);
            if (startTime is null)
            {
                return null;
            }

            var startUtc = startTime.Value.ToUniversalTime();

            string? bestPath = null;
            var bestDelta = TimeSpan.MaxValue;

            foreach (var (path, timestampUtc) in candidates)
            {
                if (assigned.Contains(path))
                {
                    continue;
                }

                var delta = (timestampUtc - startUtc).Duration();
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    bestPath = path;
                }
            }

            return bestDelta <= JournalMatchTolerance ? bestPath : null;
        }

        private static EliteInstanceViewModel BuildInstanceInfo(Process process, string? journalPath)
        {
            var summary = journalPath != null ? ReadJournalSummary(journalPath) : default;

            var (windowHandle, windowPosition, monitorInfo) = TryReadWindowAndMonitor(process);

            return new EliteInstanceViewModel(
                process.Id,
                summary.CommanderName ?? "Unknown",
                summary.Fid ?? "Unknown",
                journalPath != null ? Path.GetFileName(journalPath) : "Not found",
                windowHandle,
                windowPosition,
                monitorInfo,
                summary.CargoCapacity,
                summary.CurrentCargo,
                summary.CurrentTritium,
                summary.CurrentSystem,
                summary.CurrentStation,
                summary.CarrierName,
                summary.CarrierSystem,
                summary.CarrierBody,
                journalPath,
                summary.CarrierId,
                summary.CarrierFuelLevel,
                summary.CarrierLastDepositUtc,
                summary.MaxJumpRange,
                summary.HasOverchargedFsd);
        }

        private static DateTime? TryReadFileheaderTimestampUtc(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                var firstLine = reader.ReadLine();
                if (firstLine == null)
                {
                    return null;
                }

                using var doc = JsonDocument.Parse(firstLine);
                if (doc.RootElement.TryGetProperty("timestamp", out var ts))
                {
                    var text = ts.GetString();
                    if (text != null && DateTime.TryParse(
                            text,
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                            out var parsed))
                    {
                        return parsed;
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (JsonException)
            {
            }

            return null;
        }

        /// <summary>
        /// Events read from the journal in one pass; "latest occurrence wins" for every field,
        /// since Commander/FID rarely change within a session but cargo, location and carrier
        /// position genuinely do.
        /// </summary>
        private static readonly HashSet<string> RelevantEvents = new()
        {
            "Commander", "Loadout", "Cargo", "Location", "Docked", "Undocked",
            "FSDJump", "CarrierStats", "CarrierJump", "CarrierLocation",
            "CargoTransfer", "CarrierDepositFuel", "MarketBuy", "MarketSell"
        };

        /// <summary>Result of a single full pass over a journal file. Internal (not private) so RouteJumper.Tests can exercise ReadJournalSummary directly.</summary>
        internal readonly struct JournalSummary
        {
            public string? CommanderName { get; init; }
            public string? Fid { get; init; }
            public int? CargoCapacity { get; init; }

            /// <summary>
            /// The ship's own maximum jump range (ly), from the most recent Loadout event's
            /// MaxJumpRange field - Frontier computes this for a full fuel tank with whatever
            /// cargo is currently loaded, not necessarily zero cargo (there's no separate
            /// "unladen range" field in the journal). Confirmed in practice to read higher than
            /// the ship's real fully-fuelled, zero-cargo range, so it does *not* pre-fill the
            /// Spansh dialog's Neutron Plotter Range field (§4.12) - the CMDR types that in by
            /// hand instead. Captured here regardless, ready for a future proper unladen-range
            /// calculation to build on.
            /// </summary>
            public double? MaxJumpRange { get; init; }

            /// <summary>
            /// True if the most recent Loadout event's FrameShiftDrive slot is filled with an
            /// overcharged FSD booster (an <c>Item</c> ending in "_overchargebooster_mkii",
            /// case-insensitive - the only known variant as of writing is
            /// <c>int_hyperdrive_overcharge_size8_class5_overchargebooster_mkii</c>, but matching
            /// on the suffix alone rather than the full id is deliberately future-proof against
            /// further size/class variants). Used to default the Spansh dialog's Neutron Plotter
            /// tab (§4.12) to the overcharge supercharge multiplier rather than the regular one.
            /// </summary>
            public bool HasOverchargedFsd { get; init; }

            /// <summary>
            /// Defaults to 0, not null - see ReadJournalSummary for why "no Cargo event seen
            /// yet" means an empty hold, not unknown data.
            /// </summary>
            public int? CurrentCargo { get; init; }

            /// <summary>
            /// Tritium currently tracked aboard the ship's cargo hold, shown transparently on the
            /// card rather than silently subtracted - see ReadJournalSummary. Defaults to 0 under
            /// the same condition as CurrentCargo.
            /// </summary>
            public int? CurrentTritium { get; init; }

            /// <summary>Commander's current system/station - null station means "not docked".</summary>
            public string? CurrentSystem { get; init; }
            public string? CurrentStation { get; init; }

            /// <summary>
            /// The commander's own fleet carrier, if detected this session. CarrierName only
            /// appears if the Carrier Management panel was opened this session (CarrierStats is
            /// not logged automatically), so it's common for this to be null even for an owner -
            /// that's a real journal limitation, not a bug. CarrierSystem/CarrierBody are resolved
            /// against the confirmed owned CarrierID (see ReadJournalSummary) - a commander's
            /// journal can also contain CarrierLocation/CarrierJump entries for a *squadron*
            /// carrier they don't own, which must not be shown as if it were theirs.
            /// </summary>
            public string? CarrierName { get; init; }
            public string? CarrierSystem { get; init; }
            public string? CarrierBody { get; init; }

            /// <summary>
            /// The CarrierID this session's CarrierSystem/CarrierBody were resolved against -
            /// see the resolution rule below. Used to match live CarrierJumpRequest/
            /// CarrierLocation events to "this commander's own carrier".
            /// </summary>
            public long? CarrierId { get; init; }

            /// <summary>
            /// The carrier's own tritium fuel tank level (0-1000t), from whichever of
            /// CarrierStats' FuelLevel or CarrierDepositFuel's Total was most recently seen for
            /// the resolved owned CarrierID - both report an absolute level, not a delta, so no
            /// incremental tracking is needed the way ship-hold tritium requires. Null if neither
            /// has been seen for this carrier yet this session.
            /// </summary>
            public int? CarrierFuelLevel { get; init; }

            /// <summary>
            /// UTC timestamp of the most recent CarrierDepositFuel event for the resolved owned
            /// CarrierID - see EliteInstanceViewModel.CarrierLastDepositUtc for why this, not a
            /// before/after CarrierFuelLevel comparison, is what AutoPilotController's Engineer-
            /// refuel panic check relies on. Null if no deposit into this carrier has been seen
            /// yet this session.
            /// </summary>
            public DateTime? CarrierLastDepositUtc { get; init; }
        }

        internal static JournalSummary ReadJournalSummary(string path)
        {
            string? commanderName = null;
            string? fid = null;
            int? cargoCapacity = null;
            double? maxJumpRange = null;
            bool hasOverchargedFsd = false;

            // The ship's Cargo event's own Count includes tritium, so it can't be used directly -
            // what matters for fleet carrier jump planning is free space *for* tritium, and
            // tritium already aboard isn't relevant tonnage. latestRawShipCargo
            // is that raw total (latest "Cargo"/Ship event wins, as with every other field here);
            // trackedTritium is the best-known tritium quantity currently aboard the ship's cargo
            // hold, kept in sync incrementally by every event that can move tritium in or out of
            // it (see the switch below) - not just the Cargo event's own Inventory breakdown,
            // which isn't always present (a Cargo event immediately following a
            // CargoTransfer/MarketBuy can carry only a bare Count, no Inventory at all).
            // The final CurrentCargo is computed once, after the full pass, as
            // latestRawShipCargo - trackedTritium.
            // Defaults to 0, not "unknown" - Frontier only logs a Cargo event when the hold's
            // contents actually change, so a ship that has carried nothing at all this session
            // (the common case right after undocking with an empty hold) never gets one; no
            // event is itself evidence of an empty hold, not missing data.
            var latestRawShipCargo = 0;
            var trackedTritium = 0;

            string? currentSystem = null;
            string? currentStation = null;
            string? carrierName = null;
            long? ownedCarrierId = null;

            // Keyed by CarrierID, since a commander's journal can contain location pings for
            // carriers they don't own (e.g. a squadron carrier) alongside their own - "latest
            // event wins" per carrier, then resolved against ownedCarrierId once the full pass
            // is done and CarrierStats (if it fired at all) has been seen.
            var carrierLocationsById = new Dictionary<long, (string System, string? Body)>();

            // Same ownership-agnostic-until-resolved pattern as carrierLocationsById - both
            // CarrierStats (FuelLevel) and CarrierDepositFuel (Total) report an absolute fuel
            // level for whichever carrier their own CarrierID names, not necessarily this
            // commander's own.
            var carrierFuelById = new Dictionary<long, int>();

            // Same ownership-agnostic-until-resolved pattern as carrierFuelById above - the
            // timestamp of the most recent deposit into whichever CarrierID it names, resolved
            // against ownedCarrierId once the full pass is done. This is what lets a refuel be
            // confirmed by "did a deposit genuinely happen just now" rather than "did the fuel
            // number end up higher than whatever was last known" - the latter goes stale the
            // moment a jump (which consumes fuel with no journal event of its own) happens
            // between the last known reading and this deposit.
            var carrierLastDepositUtcById = new Dictionary<long, DateTime>();

            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);

                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    var eventName = JournalEventName.Extract(line);
                    if (eventName is null || !RelevantEvents.Contains(eventName))
                    {
                        continue;
                    }

                    JsonDocument doc;
                    try
                    {
                        doc = JsonDocument.Parse(line);
                    }
                    catch (JsonException)
                    {
                        continue;
                    }

                    using (doc)
                    {
                        var root = doc.RootElement;

                        switch (eventName)
                        {
                            case "Commander":
                                commanderName = root.TryGetProperty("Name", out var n) ? n.GetString() : commanderName;
                                fid = root.TryGetProperty("FID", out var f) ? f.GetString() : fid;
                                break;

                            case "Loadout":
                                if (root.TryGetProperty("CargoCapacity", out var cap) && cap.TryGetInt32(out var capValue))
                                {
                                    cargoCapacity = capValue;
                                }
                                if (root.TryGetProperty("MaxJumpRange", out var range) && range.TryGetDouble(out var rangeValue))
                                {
                                    maxJumpRange = rangeValue;
                                }
                                hasOverchargedFsd = HasOverchargedFrameShiftDrive(root);
                                break;

                            case "Cargo":
                                var vessel = root.TryGetProperty("Vessel", out var v) ? v.GetString() : null;
                                if (vessel == "Ship" && root.TryGetProperty("Count", out var cargoCount) && cargoCount.TryGetInt32(out var cargoCountValue))
                                {
                                    latestRawShipCargo = cargoCountValue;

                                    // Only resync from Inventory when it's actually present - see
                                    // the comment above on why it can't be relied on for every
                                    // Cargo event. When absent, trackedTritium is left as-is,
                                    // carried over from whatever CargoTransfer/CarrierDepositFuel/
                                    // MarketBuy/MarketSell events have adjusted it via since the
                                    // last resync.
                                    if (root.TryGetProperty("Inventory", out var inventory) && inventory.ValueKind == JsonValueKind.Array)
                                    {
                                        trackedTritium = GetTritiumCountFromInventory(inventory);
                                    }

                                    // Tracked tritium is a subset of the ship's own reported
                                    // total and can never legitimately exceed it - clamping here
                                    // self-heals any drift the incremental CargoTransfer/
                                    // CarrierDepositFuel/MarketBuy/MarketSell tracking above
                                    // picks up from a stray/contradictory journal entry (observed
                                    // in practice: a CargoTransfer bundling a "toship" tritium
                                    // line into what was otherwise a same-instant, fully-emptied
                                    // transfer, with the immediately following Cargo event
                                    // reporting Count 0 - i.e. the ground truth already available
                                    // here, even without an Inventory breakdown to resync from).
                                    trackedTritium = Math.Min(trackedTritium, latestRawShipCargo);
                                }
                                break;

                            case "CargoTransfer":
                                // Ship <-> carrier (or SRV) cargo-hold transfer. "toship" is the
                                // only direction that adds to what the ship is carrying - every
                                // other direction value ("tocarrier", "tosrv", ...) means tritium
                                // leaving the ship's hold.
                                if (root.TryGetProperty("Transfers", out var transfers) && transfers.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var transfer in transfers.EnumerateArray())
                                    {
                                        var type = GetString(transfer, "Type");
                                        if (!string.Equals(type, "tritium", StringComparison.OrdinalIgnoreCase))
                                        {
                                            continue;
                                        }

                                        if (transfer.TryGetProperty("Count", out var transferCount) && transferCount.TryGetInt32(out var transferCountValue))
                                        {
                                            var direction = GetString(transfer, "Direction");
                                            trackedTritium = string.Equals(direction, "toship", StringComparison.OrdinalIgnoreCase)
                                                ? trackedTritium + transferCountValue
                                                : Math.Max(0, trackedTritium - transferCountValue);
                                        }
                                    }
                                }
                                break;

                            case "CarrierDepositFuel":
                                // Fueling the carrier directly from the ship's cargo hold - always
                                // tritium, always ship -> carrier, so always a straight reduction.
                                // Ship-side tracking applies regardless of which carrier received
                                // it, but Total (the carrier's own new fuel level after this
                                // deposit) is only meaningful for whichever carrier CarrierID
                                // names here - recorded into carrierFuelById the same
                                // ownership-agnostic way as carrierLocationsById below, resolved
                                // against the confirmed-owned carrier after the full pass.
                                if (root.TryGetProperty("Amount", out var depositAmount) && depositAmount.TryGetInt32(out var depositAmountValue))
                                {
                                    trackedTritium = Math.Max(0, trackedTritium - depositAmountValue);
                                }
                                if (root.TryGetProperty("CarrierID", out var depositCarrierId) && depositCarrierId.TryGetInt64(out var depositCarrierIdValue))
                                {
                                    if (root.TryGetProperty("Total", out var depositTotal) && depositTotal.TryGetInt32(out var depositTotalValue))
                                    {
                                        carrierFuelById[depositCarrierIdValue] = depositTotalValue;
                                    }

                                    if (TryReadEventTimestampUtc(root, out var depositTimestampUtc))
                                    {
                                        carrierLastDepositUtcById[depositCarrierIdValue] = depositTimestampUtc;
                                    }
                                }
                                break;

                            case "MarketBuy":
                                if (string.Equals(GetString(root, "Type"), "tritium", StringComparison.OrdinalIgnoreCase) &&
                                    root.TryGetProperty("Count", out var buyCount) && buyCount.TryGetInt32(out var buyCountValue))
                                {
                                    trackedTritium += buyCountValue;
                                }
                                break;

                            case "MarketSell":
                                if (string.Equals(GetString(root, "Type"), "tritium", StringComparison.OrdinalIgnoreCase) &&
                                    root.TryGetProperty("Count", out var sellCount) && sellCount.TryGetInt32(out var sellCountValue))
                                {
                                    trackedTritium = Math.Max(0, trackedTritium - sellCountValue);
                                }
                                break;

                            case "Location":
                                currentSystem = GetString(root, "StarSystem") ?? currentSystem;
                                var locationDocked = root.TryGetProperty("Docked", out var ld) && ld.GetBoolean();
                                currentStation = locationDocked ? GetString(root, "StationName") : null;
                                break;

                            case "Docked":
                                currentSystem = GetString(root, "StarSystem") ?? currentSystem;
                                currentStation = GetString(root, "StationName") ?? currentStation;
                                break;

                            case "Undocked":
                                currentStation = null;
                                break;

                            case "FSDJump":
                                currentSystem = GetString(root, "StarSystem") ?? currentSystem;
                                currentStation = null;
                                break;

                            case "CarrierStats":
                                carrierName = GetString(root, "Name") ?? carrierName;
                                if (root.TryGetProperty("CarrierID", out var statsId) && statsId.TryGetInt64(out var statsIdValue))
                                {
                                    ownedCarrierId = statsIdValue;

                                    // FuelLevel is the carrier's own tritium fuel tank level
                                    // (0-1000t) - only reported when CarrierStats fires (i.e. the
                                    // commander has opened Carrier Management this session).
                                    if (root.TryGetProperty("FuelLevel", out var fuelLevel) && fuelLevel.TryGetInt32(out var fuelLevelValue))
                                    {
                                        carrierFuelById[statsIdValue] = fuelLevelValue;
                                    }
                                }
                                break;

                            case "CarrierJump":
                                // No CarrierType field here (unlike CarrierLocation), but MarketID
                                // is the jumped-to carrier's CarrierID - being docked there doesn't
                                // necessarily mean it's owned by this commander (could be a guest
                                // aboard someone else's carrier during its jump).
                                if (root.TryGetProperty("MarketID", out var jumpId) && jumpId.TryGetInt64(out var jumpIdValue))
                                {
                                    var jumpSystem = GetString(root, "StarSystem");
                                    var jumpBody = GetString(root, "Body");
                                    if (jumpSystem != null)
                                    {
                                        carrierLocationsById[jumpIdValue] = (jumpSystem, jumpBody);
                                    }

                                    var carrierJumpDocked = root.TryGetProperty("Docked", out var cjd) && cjd.GetBoolean();
                                    if (carrierJumpDocked)
                                    {
                                        currentSystem = jumpSystem ?? currentSystem;
                                        currentStation = GetString(root, "StationName") ?? currentStation;
                                    }
                                }
                                break;

                            case "CarrierLocation":
                                // Only "FleetCarrier" is ever a candidate for "this commander's own
                                // carrier" - "SquadronCarrier" (a shared squadron carrier the
                                // commander doesn't own) also appears in the journal and must be
                                // ignored here, not treated as if it were theirs.
                                var carrierType = GetString(root, "CarrierType");
                                if (carrierType == "FleetCarrier" &&
                                    root.TryGetProperty("CarrierID", out var locId) && locId.TryGetInt64(out var locIdValue))
                                {
                                    var trackedSystem = GetString(root, "StarSystem");
                                    if (trackedSystem != null)
                                    {
                                        // CarrierLocation never carries a body name, so clear any
                                        // previously-recorded one for this carrier - it may now be
                                        // stale even if the system happens to be unchanged.
                                        carrierLocationsById[locIdValue] = (trackedSystem, null);
                                    }
                                }
                                break;
                        }
                    }
                }
            }
            catch (IOException)
            {
            }

            string? carrierSystem = null;
            string? carrierBody = null;
            long? resolvedCarrierId = null;
            if (ownedCarrierId.HasValue && carrierLocationsById.TryGetValue(ownedCarrierId.Value, out var owned))
            {
                (carrierSystem, carrierBody) = owned;
                resolvedCarrierId = ownedCarrierId;
            }
            else if (ownedCarrierId is null && carrierLocationsById.Count == 1)
            {
                // CarrierStats never fired this session, so ownership can't be confirmed by ID -
                // but if only one distinct carrier was ever referenced, there's no ambiguity to
                // resolve, so it's safe to assume it's the commander's own.
                var only = carrierLocationsById.Single();
                resolvedCarrierId = only.Key;
                (carrierSystem, carrierBody) = only.Value;
            }

            // Same resolved-owned-ID lookup as carrierSystem/carrierBody above - a fuel figure
            // recorded against a carrier this commander doesn't own (e.g. a squadron carrier's
            // CarrierDepositFuel from a guest deposit) must never be shown as if it were theirs.
            var carrierFuelLevel = resolvedCarrierId.HasValue && carrierFuelById.TryGetValue(resolvedCarrierId.Value, out var fuel)
                ? fuel
                : (int?)null;
            var carrierLastDepositUtc = resolvedCarrierId.HasValue && carrierLastDepositUtcById.TryGetValue(resolvedCarrierId.Value, out var depositUtc)
                ? depositUtc
                : (DateTime?)null;

            return new JournalSummary
            {
                CommanderName = commanderName,
                Fid = fid,
                CargoCapacity = cargoCapacity,
                MaxJumpRange = maxJumpRange,
                HasOverchargedFsd = hasOverchargedFsd,
                CurrentCargo = Math.Max(0, latestRawShipCargo - trackedTritium),
                CurrentTritium = trackedTritium,
                CurrentSystem = currentSystem,
                CurrentStation = currentStation,
                CarrierName = carrierName,
                CarrierId = resolvedCarrierId,
                CarrierSystem = carrierSystem,
                CarrierBody = carrierBody,
                CarrierFuelLevel = carrierFuelLevel,
                CarrierLastDepositUtc = carrierLastDepositUtc
            };
        }

        /// <summary>Parses an event's own "timestamp" field the same way TryReadFileheaderTimestampUtc does for the journal's first line.</summary>
        private static bool TryReadEventTimestampUtc(JsonElement root, out DateTime timestampUtc)
        {
            timestampUtc = default;
            if (root.TryGetProperty("timestamp", out var ts) && ts.GetString() is { } text &&
                DateTime.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out var parsed))
            {
                timestampUtc = parsed;
                return true;
            }

            return false;
        }

        private static string? GetString(JsonElement root, string propertyName) =>
            root.TryGetProperty(propertyName, out var value) ? value.GetString() : null;

        /// <summary>
        /// True if a Loadout event's own Modules array has the FrameShiftDrive slot filled with
        /// an overcharged FSD booster - matched by its Item id ending in
        /// "_overchargebooster_mkii" (case-insensitive) rather than the full id, so a future
        /// size/class variant of the same booster is still recognised without a code change.
        /// </summary>
        private static bool HasOverchargedFrameShiftDrive(JsonElement loadoutRoot)
        {
            if (!loadoutRoot.TryGetProperty("Modules", out var modules) || modules.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var module in modules.EnumerateArray())
            {
                if (string.Equals(GetString(module, "Slot"), "FrameShiftDrive", StringComparison.OrdinalIgnoreCase))
                {
                    var item = GetString(module, "Item");
                    return item != null && item.EndsWith("_overchargebooster_mkii", StringComparison.OrdinalIgnoreCase);
                }
            }

            return false;
        }

        /// <summary>
        /// Pulls just the tritium entry's Count out of a Cargo event's per-commodity Inventory
        /// array (0 if tritium isn't present in it) - used to resync the running tracked-tritium
        /// total (see ReadJournalSummary) whenever a Cargo event happens to carry the full
        /// breakdown, since that's the one authoritative ground-truth source available.
        /// </summary>
        private static int GetTritiumCountFromInventory(JsonElement inventory)
        {
            foreach (var item in inventory.EnumerateArray())
            {
                if (string.Equals(GetString(item, "Name"), "tritium", StringComparison.OrdinalIgnoreCase) &&
                    item.TryGetProperty("Count", out var itemCount) && itemCount.TryGetInt32(out var itemCountValue))
                {
                    return itemCountValue;
                }
            }

            return 0;
        }

        private static (IntPtr Handle, string WindowPosition, string MonitorInfo) TryReadWindowAndMonitor(Process process)
        {
            try
            {
                var hwnd = process.MainWindowHandle;
                var rect = hwnd == IntPtr.Zero ? null : Win32Monitors.GetWindowRect(hwnd);
                if (rect is null)
                {
                    return (IntPtr.Zero, "Unknown", "Unknown");
                }

                var windowPosition = $"({rect.Value.Left}, {rect.Value.Top}) — {rect.Value.Width}×{rect.Value.Height}px";

                var monitor = Win32Monitors.GetMonitorForWindow(hwnd);
                var monitorInfo = monitor is null
                    ? "Unknown"
                    : $"{monitor.Value.DeviceName}{(monitor.Value.IsPrimary ? " (Primary)" : string.Empty)} — " +
                      $"{monitor.Value.MonitorRect.Width}×{monitor.Value.MonitorRect.Height}px at " +
                      $"({monitor.Value.MonitorRect.Left}, {monitor.Value.MonitorRect.Top})";

                return (hwnd, windowPosition, monitorInfo);
            }
            catch (InvalidOperationException)
            {
                // Process exited between enumeration and inspection.
                return (IntPtr.Zero, "Unknown", "Unknown");
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // No main window yet (e.g. still on the launcher/splash screen).
                return (IntPtr.Zero, "Unknown", "Unknown");
            }
        }

        /// <summary>Disposes the Process objects returned by GetProcessesByName once scanning is done.</summary>
        private sealed class ProcessList : IDisposable
        {
            public ProcessList(Process[] items) => Items = items;

            public IReadOnlyList<Process> Items { get; }

            public void Dispose()
            {
                foreach (var process in Items)
                {
                    process.Dispose();
                }
            }
        }
    }
}
