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

        public string WriteTo(string path)
        {
            File.WriteAllLines(path, _lines);
            return path;
        }

        public string ToText() => string.Join("\n", _lines) + "\n";
    }
}
