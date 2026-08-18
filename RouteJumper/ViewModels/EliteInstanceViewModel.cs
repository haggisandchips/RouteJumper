using RouteJumper.Common;

namespace RouteJumper.ViewModels
{
    /// <summary>
    /// Snapshot of one running EliteDangerous64.exe instance, as of the last Refresh. Rebuilt
    /// from scratch on every refresh (not mutated in place) - except for IsCaptain/IsEngineer,
    /// which RolesViewModel restores onto the new instance for the same ProcessId after each
    /// refresh, and which need INotifyPropertyChanged so a role toggle updates the card
    /// immediately without waiting for the next refresh.
    /// </summary>
    public class EliteInstanceViewModel : ObservableObject
    {
        private bool _isCaptain;
        private bool _isEngineer;
        private bool _isTracked;

        public EliteInstanceViewModel(
            int processId,
            string commanderName,
            string fid,
            string journalFileName,
            IntPtr windowHandle,
            string windowPosition,
            string monitorInfo,
            int? cargoCapacity,
            int? currentCargo,
            int? currentTritium,
            string? currentSystem,
            string? currentStation,
            string? carrierName,
            string? carrierSystem,
            string? carrierBody,
            string? journalFilePath,
            long? carrierId,
            int? carrierFuelLevel,
            DateTime? carrierLastDepositUtc = null,
            double? maxJumpRange = null,
            bool hasOverchargedFsd = false)
        {
            ProcessId = processId;
            CommanderName = commanderName;
            Fid = fid;
            JournalFileName = journalFileName;
            WindowHandle = windowHandle;
            WindowPosition = windowPosition;
            MonitorInfo = monitorInfo;
            CargoCapacity = cargoCapacity;
            CurrentCargo = currentCargo;
            CurrentTritium = currentTritium;
            CurrentSystem = currentSystem;
            CurrentStation = currentStation;
            CarrierName = carrierName;
            CarrierSystem = carrierSystem;
            CarrierBody = carrierBody;
            JournalFilePath = journalFilePath;
            CarrierId = carrierId;
            CarrierFuelLevel = carrierFuelLevel;
            CarrierLastDepositUtc = carrierLastDepositUtc;
            MaxJumpRange = maxJumpRange;
            HasOverchargedFsd = hasOverchargedFsd;
        }

        public int ProcessId { get; }

        public string CommanderName { get; }

        public string Fid { get; }

        public string JournalFileName { get; }

        /// <summary>
        /// Full path to the matched journal file, or null if none was matched. Kept alongside
        /// the display-only JournalFileName so the Captain role's journal watcher can
        /// open the file without re-deriving it.
        /// </summary>
        public string? JournalFilePath { get; }

        /// <summary>
        /// The commander's own fleet carrier's CarrierID, resolved the same way as
        /// CarrierSystem/CarrierBody (see EliteInstanceScanner) - null if never established
        /// this session. Used to filter CarrierJumpRequest/CarrierLocation events to "this
        /// commander's own carrier" when the Captain role is assigned.
        /// </summary>
        public long? CarrierId { get; }

        /// <summary>
        /// The game window's HWND, kept as a raw handle (not just the display string) so it can
        /// later be passed straight to Win32 calls (e.g. SendInput/PostMessage) without re-parsing.
        /// IntPtr.Zero if the window couldn't be found.
        /// </summary>
        public IntPtr WindowHandle { get; }

        public string WindowHandleDisplay => WindowHandle == IntPtr.Zero
            ? "Unknown"
            : $"0x{WindowHandle.ToInt64():X}";

        public string WindowPosition { get; }

        public string MonitorInfo { get; }

        /// <summary>Max cargo tonnage, from the most recent Loadout event's CargoCapacity field.</summary>
        public int? CargoCapacity { get; }

        /// <summary>
        /// This ship's own maximum jump range (ly), from the most recent Loadout event's
        /// MaxJumpRange field - computed by Frontier for a full fuel tank with whatever cargo is
        /// currently loaded (there's no separate "unladen range" field in the journal). Null
        /// until a Loadout event has been seen this session. Confirmed in practice to read higher
        /// than the ship's real fully-fuelled, zero-cargo range, so it does *not* pre-fill the
        /// Spansh dialog's Neutron Plotter Range field (SPEC §4.12) - kept here regardless, ready
        /// for a future proper unladen-range calculation to build on.
        /// </summary>
        public double? MaxJumpRange { get; }

        /// <summary>
        /// True if the most recent Loadout event's FrameShiftDrive slot is filled with an
        /// overcharged FSD booster (an Item id ending in "_overchargebooster_mkii") - see
        /// EliteInstanceScanner.HasOverchargedFrameShiftDrive. Used to default the Spansh
        /// dialog's Neutron Plotter tab (SPEC §4.12) to the overcharge supercharge multiplier.
        /// </summary>
        public bool HasOverchargedFsd { get; }

        /// <summary>
        /// Current non-tritium cargo held - used for AvailableCargoCapacity/CanBeEngineer, which
        /// must keep excluding tritium (what matters there is free space *for* tritium). Not used
        /// directly for CargoDisplay any more - see TotalCargoDisplayed. Derived in
        /// EliteInstanceScanner from the ship's latest Cargo event total, minus tritium tracked
        /// across every event that can move it in/out of the ship's hold (CargoTransfer,
        /// CarrierDepositFuel, MarketBuy/MarketSell). Defaults to 0, not unknown, if no Cargo
        /// event has been seen for this ship at all this session - Frontier only logs one when
        /// the hold's contents change, so no event is itself evidence of an empty hold.
        /// </summary>
        public int? CurrentCargo { get; }

        /// <summary>
        /// Tritium currently tracked aboard the ship's cargo hold - see EliteInstanceScanner.
        /// Defaults to 0 under the same condition as CurrentCargo.
        /// </summary>
        public int? CurrentTritium { get; }

        /// <summary>
        /// The "x" in CargoDisplay's "x / y" - unlike CurrentCargo, this includes tritium, since
        /// the cargo total shown to the user should account for everything aboard; tritium is
        /// then broken out as its own line underneath (TritiumDisplay) rather than hidden.
        /// </summary>
        private int? TotalCargoDisplayed => (CurrentCargo, CurrentTritium) switch
        {
            (int cargo, int tritium) => cargo + tritium,
            (int cargo, null) => cargo,
            _ => null
        };

        public string CargoDisplay => (TotalCargoDisplayed, CargoCapacity) switch
        {
            (int current, int capacity) => $"{current} / {capacity}t",
            (int current, null) => $"{current}t / Unknown",
            (null, int capacity) => $"Unknown / {capacity}t",
            _ => "Unknown"
        };

        /// <summary>
        /// Empty (not "Unknown") when there's no tritium to show - unlike every other Unknown-
        /// capable display string on this class, this one drives Visibility via
        /// StringToVisibilityConverter (see RolesView.xaml), so the line is omitted entirely
        /// rather than shown as "0t tritium"/"Unknown tritium" when there's nothing to report.
        /// </summary>
        public string TritiumDisplay => CurrentTritium is > 0 ? $"{CurrentTritium}t tritium" : string.Empty;

        /// <summary>Free cargo space (tritium excluded), or null if capacity/current is unknown.</summary>
        public int? AvailableCargoCapacity => (CargoCapacity, CurrentCargo) switch
        {
            (int capacity, int current) => capacity - current,
            _ => null
        };

        /// <summary>
        /// False when available capacity is positively known to be zero or less (no cargo
        /// racks, or full), or when it isn't known at all (Loadout hasn't been read yet this
        /// session - CurrentCargo itself defaults to 0, not unknown, so a genuinely empty hold
        /// no longer blocks this once capacity is known).
        /// </summary>
        public bool CanBeEngineer => AvailableCargoCapacity.HasValue && AvailableCargoCapacity.Value > 0;

        /// <summary>Current system; null means never established this session.</summary>
        public string? CurrentSystem { get; }

        /// <summary>Current station; null means "not docked" (or unknown).</summary>
        public string? CurrentStation { get; }

        public string LocationDisplay => (CurrentSystem, CurrentStation) switch
        {
            (string system, string station) => $"{system} — {station}",
            (string system, null) => system,
            _ => "Unknown"
        };

        /// <summary>
        /// The commander's own fleet carrier name. Only known if the Carrier Management panel
        /// was opened this session - the journal doesn't log it automatically - so null here
        /// does not necessarily mean the commander has no carrier.
        /// </summary>
        public string? CarrierName { get; }

        public string? CarrierSystem { get; }

        public string? CarrierBody { get; }

        /// <summary>
        /// The carrier's own tritium fuel tank level (0-1000t) - see EliteInstanceScanner. Null
        /// if neither CarrierStats nor a CarrierDepositFuel deposit into this carrier has been
        /// seen yet this session (most commonly: Carrier Management hasn't been opened yet).
        /// </summary>
        public int? CarrierFuelLevel { get; }

        /// <summary>
        /// UTC timestamp of the most recent CarrierDepositFuel event resolved against this
        /// carrier's own confirmed CarrierID - see EliteInstanceScanner. Null if no deposit into
        /// this carrier has been seen yet this session. Elite's journal never logs the fuel a
        /// jump itself consumes (only CarrierStats and a fresh deposit report an absolute level),
        /// so CarrierFuelLevel alone can go stale across a jump - AutoPilotController's own
        /// Engineer-refuel panic check uses this timestamp, not a before/after CarrierFuelLevel
        /// comparison, to confirm a real deposit actually happened during a given playback
        /// window, since a depot refilled back to a previously-known ceiling (or already at the
        /// last known ceiling because no CarrierStats/deposit fired since before the jump that
        /// dropped it) would otherwise show no numeric increase despite a genuine deposit.
        /// </summary>
        public DateTime? CarrierLastDepositUtc { get; }

        /// <summary>
        /// Empty (not "Unknown") when the fuel level isn't known - same
        /// StringToVisibilityConverter-driven omit-the-line pattern as TritiumDisplay, since
        /// there's nothing useful to show until Carrier Management has been opened at least once
        /// this session.
        /// </summary>
        public string CarrierFuelDisplay => CarrierFuelLevel.HasValue ? $"{CarrierFuelLevel}/1000t fuel" : string.Empty;

        public string CarrierDisplay
        {
            get
            {
                if (CarrierName is null && CarrierSystem is null)
                {
                    return "None detected this session";
                }

                var name = CarrierName ?? "Unnamed carrier";
                if (CarrierSystem is null)
                {
                    return $"{name} (location unknown)";
                }

                var location = CarrierBody is null ? CarrierSystem : $"{CarrierSystem}, {CarrierBody}";
                return $"{name} — {location}";
            }
        }

        /// <summary>True while this instance holds the Captain role.</summary>
        public bool IsCaptain
        {
            get => _isCaptain;
            set => SetProperty(ref _isCaptain, value);
        }

        /// <summary>True while this instance holds the Engineer role.</summary>
        public bool IsEngineer
        {
            get => _isEngineer;
            set => SetProperty(ref _isEngineer, value);
        }

        /// <summary>True while this instance is the Track tab's tracked (Ship mode) instance.</summary>
        public bool IsTracked
        {
            get => _isTracked;
            set => SetProperty(ref _isTracked, value);
        }
    }
}
