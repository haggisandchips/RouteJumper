using System.Globalization;
using System.IO;

namespace RouteJumper.Tests.TestSupport
{
    /// <summary>Builds a fake Elite Dangerous journal file (one JSON object per line) for CarrierRouteJournalWatcher/EliteInstanceScanner tests.</summary>
    internal sealed class JournalFile
    {
        private readonly List<string> _lines = new();

        public static string TimestampOf(DateTime utc) => utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        public JournalFile Fileheader(DateTime timestampUtc)
        {
            _lines.Add($"{{\"timestamp\":\"{TimestampOf(timestampUtc)}\",\"event\":\"Fileheader\"}}");
            return this;
        }

        public JournalFile Commander(string name, string fid)
        {
            _lines.Add($"{{\"timestamp\":\"{TimestampOf(DateTime.UtcNow)}\",\"event\":\"Commander\",\"FID\":\"{fid}\",\"Name\":\"{name}\"}}");
            return this;
        }

        public JournalFile Line(string json)
        {
            _lines.Add(json);
            return this;
        }

        public JournalFile CarrierJumpRequest(long carrierId, string systemName, DateTime departureTimeUtc, string carrierType = "FleetCarrier")
        {
            _lines.Add(
                "{\"timestamp\":\"" + TimestampOf(DateTime.UtcNow) + "\",\"event\":\"CarrierJumpRequest\"," +
                "\"CarrierID\":" + carrierId + ",\"CarrierType\":\"" + carrierType + "\"," +
                "\"SystemName\":\"" + systemName + "\",\"DepartureTime\":\"" + TimestampOf(departureTimeUtc) + "\"}");
            return this;
        }

        public JournalFile CarrierJumpCancelled(long carrierId)
        {
            _lines.Add("{\"timestamp\":\"" + TimestampOf(DateTime.UtcNow) + "\",\"event\":\"CarrierJumpCancelled\",\"CarrierID\":" + carrierId + "}");
            return this;
        }

        public JournalFile CarrierLocation(long carrierId, string systemName, DateTime? timestampUtc = null, string carrierType = "FleetCarrier")
        {
            _lines.Add(
                "{\"timestamp\":\"" + TimestampOf(timestampUtc ?? DateTime.UtcNow) + "\",\"event\":\"CarrierLocation\"," +
                "\"CarrierID\":" + carrierId + ",\"CarrierType\":\"" + carrierType + "\",\"StarSystem\":\"" + systemName + "\"}");
            return this;
        }

        public JournalFile CarrierStats(long carrierId, string name, int fuelLevel)
        {
            _lines.Add(
                "{\"timestamp\":\"" + TimestampOf(DateTime.UtcNow) + "\",\"event\":\"CarrierStats\"," +
                "\"CarrierID\":" + carrierId + ",\"Name\":\"" + name + "\",\"FuelLevel\":" + fuelLevel + "}");
            return this;
        }

        public JournalFile StartJump(string jumpType, string? starSystem, DateTime? timestampUtc = null)
        {
            var systemPart = starSystem is null ? string.Empty : ",\"StarSystem\":\"" + starSystem + "\"";
            _lines.Add(
                "{\"timestamp\":\"" + TimestampOf(timestampUtc ?? DateTime.UtcNow) + "\",\"event\":\"StartJump\"," +
                "\"JumpType\":\"" + jumpType + "\"" + systemPart + "}");
            return this;
        }

        public JournalFile FSDJump(string starSystem, DateTime? timestampUtc = null)
        {
            _lines.Add(
                "{\"timestamp\":\"" + TimestampOf(timestampUtc ?? DateTime.UtcNow) + "\",\"event\":\"FSDJump\"," +
                "\"StarSystem\":\"" + starSystem + "\"}");
            return this;
        }

        /// <summary>Note the field is "Name", not "StarSystem" - confirmed against a real journal.</summary>
        public JournalFile FSDTarget(string name, DateTime? timestampUtc = null)
        {
            _lines.Add(
                "{\"timestamp\":\"" + TimestampOf(timestampUtc ?? DateTime.UtcNow) + "\",\"event\":\"FSDTarget\"," +
                "\"Name\":\"" + name + "\"}");
            return this;
        }

        public JournalFile Music(string musicTrack, DateTime? timestampUtc = null)
        {
            _lines.Add(
                "{\"timestamp\":\"" + TimestampOf(timestampUtc ?? DateTime.UtcNow) + "\",\"event\":\"Music\"," +
                "\"MusicTrack\":\"" + musicTrack + "\"}");
            return this;
        }

        public string WriteTo(string path)
        {
            File.WriteAllLines(path, _lines);
            return path;
        }

        public string ToText() => string.Join("\n", _lines) + "\n";
    }
}
