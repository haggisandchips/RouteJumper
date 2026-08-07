using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using RouteJumper.ViewModels;

namespace RouteJumper.Services
{
    /// <summary>
    /// Finds running EliteDangerous64.exe instances and, for each one, works out which journal
    /// file it owns (there's no direct OS-level link between a process and a journal file, so
    /// this matches each process to the journal whose Fileheader timestamp is closest to the
    /// process's start time), then reads that journal's Commander event for FID/name and reads
    /// the process's window position/monitor via Win32.
    /// </summary>
    public class EliteInstanceScanner
    {
        private const string ProcessName = "EliteDangerous64";
        private static readonly TimeSpan JournalMatchTolerance = TimeSpan.FromMinutes(5);

        public Task<IReadOnlyList<EliteInstanceViewModel>> ScanAsync() => Task.Run(Scan);

        private static IReadOnlyList<EliteInstanceViewModel> Scan()
        {
            using var processes = new ProcessList(Process.GetProcessesByName(ProcessName));
            if (processes.Items.Count == 0)
            {
                return Array.Empty<EliteInstanceViewModel>();
            }

            var journalCandidates = GetJournalFilesWithTimestamps();
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

        private static List<(string Path, DateTime TimestampUtc)> GetJournalFilesWithTimestamps()
        {
            var journalDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Saved Games", "Frontier Developments", "Elite Dangerous");

            if (!Directory.Exists(journalDir))
            {
                return new List<(string, DateTime)>();
            }

            var result = new List<(string, DateTime)>();
            foreach (var path in Directory.GetFiles(journalDir, "Journal.*.log"))
            {
                var timestamp = TryReadFileheaderTimestampUtc(path);
                if (timestamp.HasValue)
                {
                    result.Add((path, timestamp.Value));
                }
            }

            return result;
        }

        private static string? MatchJournal(
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
                summary.CarrierId);
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

        /// <summary>Result of a single full pass over a journal file.</summary>
        private readonly struct JournalSummary
        {
            public string? CommanderName { get; init; }
            public string? Fid { get; init; }
            public int? CargoCapacity { get; init; }
            public int? CurrentCargo { get; init; }

            /// <summary>
            /// Tritium currently tracked aboard the ship's cargo hold, shown transparently on the
            /// card rather than silently subtracted - see ReadJournalSummary. Null under the same
            /// condition as CurrentCargo (no Cargo event for this ship seen at all yet).
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
            /// CarrierLocation events to "this commander's own carrier" (see SPEC §11.5).
            /// </summary>
            public long? CarrierId { get; init; }
        }

        private static JournalSummary ReadJournalSummary(string path)
        {
            string? commanderName = null;
            string? fid = null;
            int? cargoCapacity = null;

            // The ship's Cargo event's own Count includes tritium, so it can't be used directly -
            // what matters for fleet carrier jump planning is free space *for* tritium (see
            // SPEC §11.3), and tritium already aboard isn't relevant tonnage. latestRawShipCargo
            // is that raw total (latest "Cargo"/Ship event wins, as with every other field here);
            // trackedTritium is the best-known tritium quantity currently aboard the ship's cargo
            // hold, kept in sync incrementally by every event that can move tritium in or out of
            // it (see the switch below) - not just the Cargo event's own Inventory breakdown,
            // which turns out to not always be present (confirmed live: a Cargo event immediately
            // following a CargoTransfer/MarketBuy carried only a bare Count, no Inventory at all).
            // The final CurrentCargo is computed once, after the full pass, as
            // latestRawShipCargo - trackedTritium.
            int? latestRawShipCargo = null;
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
                                if (root.TryGetProperty("Amount", out var depositAmount) && depositAmount.TryGetInt32(out var depositAmountValue))
                                {
                                    trackedTritium = Math.Max(0, trackedTritium - depositAmountValue);
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

            return new JournalSummary
            {
                CommanderName = commanderName,
                Fid = fid,
                CargoCapacity = cargoCapacity,
                CurrentCargo = latestRawShipCargo.HasValue ? Math.Max(0, latestRawShipCargo.Value - trackedTritium) : null,
                CurrentTritium = latestRawShipCargo.HasValue ? trackedTritium : null,
                CurrentSystem = currentSystem,
                CurrentStation = currentStation,
                CarrierName = carrierName,
                CarrierId = resolvedCarrierId,
                CarrierSystem = carrierSystem,
                CarrierBody = carrierBody
            };
        }

        private static string? GetString(JsonElement root, string propertyName) =>
            root.TryGetProperty(propertyName, out var value) ? value.GetString() : null;

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
