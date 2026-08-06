namespace RouteJumper.ViewModels
{
    /// <summary>
    /// Read-only snapshot of one running EliteDangerous64.exe instance, as of the last Refresh.
    /// Rebuilt from scratch on every refresh (not mutated in place), so it doesn't need
    /// INotifyPropertyChanged like RouteRowViewModel does.
    /// </summary>
    public class EliteInstanceViewModel
    {
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
            string? carrierBody)
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
        }

        public int ProcessId { get; }

        public string CommanderName { get; }

        public string Fid { get; }

        public string JournalFileName { get; }

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

        /// <summary>Current cargo held, from the most recent Ship-vessel Cargo event's Count field.</summary>
        public int? CurrentCargo { get; }

        public string CargoDisplay => (CurrentCargo, CargoCapacity) switch
        {
            (int current, int capacity) => $"{current} / {capacity}t",
            (int current, null) => $"{current}t / Unknown",
            (null, int capacity) => $"Unknown / {capacity}t",
            _ => "Unknown"
        };

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
    }
}
