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
            string? currentSystem,
            string? currentStation,
            string? carrierName,
            string? carrierSystem,
            string? carrierBody,
            string? journalFilePath,
            long? carrierId)
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
            CurrentSystem = currentSystem;
            CurrentStation = currentStation;
            CarrierName = carrierName;
            CarrierSystem = carrierSystem;
            CarrierBody = carrierBody;
            JournalFilePath = journalFilePath;
            CarrierId = carrierId;
        }

        public int ProcessId { get; }

        public string CommanderName { get; }

        public string Fid { get; }

        public string JournalFileName { get; }

        /// <summary>
        /// Full path to the matched journal file, or null if none was matched. Kept alongside
        /// the display-only JournalFileName so the Captain role's journal watcher (§11.5) can
        /// open the file without re-deriving it.
        /// </summary>
        public string? JournalFilePath { get; }

        /// <summary>
        /// The commander's own fleet carrier's CarrierID, resolved the same way as
        /// CarrierSystem/CarrierBody (see EliteInstanceScanner) - null if never established
        /// this session. Used to filter CarrierJumpRequest/CarrierLocation events to "this
        /// commander's own carrier" when the Captain role is assigned (§11.5).
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
        /// Current cargo held, from the most recent Ship-vessel Cargo event's Inventory total,
        /// excluding tritium (see EliteInstanceScanner.ReadCargoCountExcludingTritium).
        /// </summary>
        public int? CurrentCargo { get; }

        public string CargoDisplay => (CurrentCargo, CargoCapacity) switch
        {
            (int current, int capacity) => $"{current} / {capacity}t",
            (int current, null) => $"{current}t / Unknown",
            (null, int capacity) => $"Unknown / {capacity}t",
            _ => "Unknown"
        };

        /// <summary>Free cargo space (tritium excluded), or null if capacity/current is unknown.</summary>
        public int? AvailableCargoCapacity => (CargoCapacity, CurrentCargo) switch
        {
            (int capacity, int current) => capacity - current,
            _ => null
        };

        /// <summary>
        /// False when available capacity is positively known to be zero or less (no cargo
        /// racks, or full), or when it isn't known at all (Loadout/Cargo haven't been read
        /// yet this session) - per SPEC §11.5.
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

        /// <summary>True while this instance holds the Captain role (see SPEC §11.5).</summary>
        public bool IsCaptain
        {
            get => _isCaptain;
            set => SetProperty(ref _isCaptain, value);
        }

        /// <summary>True while this instance holds the Engineer role (see SPEC §11.5).</summary>
        public bool IsEngineer
        {
            get => _isEngineer;
            set => SetProperty(ref _isEngineer, value);
        }
    }
}
