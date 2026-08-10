using System.IO;
using RouteJumper.Services;
using RouteJumper.Tests.TestSupport;
using Xunit;

namespace RouteJumper.Tests.Services
{
    public class EliteInstanceScannerReadJournalSummaryTests
    {
        private static string WriteJournal(TempDirectory dir, params string[] lines)
        {
            var path = dir.CombinePath($"Journal.{Guid.NewGuid():N}.log");
            File.WriteAllLines(path, lines);
            return path;
        }

        [Fact]
        public async Task ScanAsync_ReturnsOneCardPerRunningEliteDangerousProcess()
        {
            // The dev/CI machine running this suite may or may not have EliteDangerous64.exe
            // running - rather than assuming either way, compare against the real process count
            // directly, the same way EliteInstanceScanner itself discovers instances.
            var expectedCount = System.Diagnostics.Process.GetProcessesByName("EliteDangerous64").Length;

            var scanner = new EliteInstanceScanner(new AppConfigStore(Path.GetTempPath()));
            var result = await scanner.ScanAsync();

            Assert.Equal(expectedCount, result.Count);
        }

        [Fact]
        public void ReadJournalSummary_Commander_ReadsNameAndFid()
        {
            using var dir = new TempDirectory();
            var path = WriteJournal(dir, "{\"event\":\"Commander\",\"FID\":\"F123\",\"Name\":\"Jameson\"}");

            var summary = EliteInstanceScanner.ReadJournalSummary(path);

            Assert.Equal("Jameson", summary.CommanderName);
            Assert.Equal("F123", summary.Fid);
        }

        [Fact]
        public void ReadJournalSummary_Loadout_ReadsCargoCapacity()
        {
            using var dir = new TempDirectory();
            var path = WriteJournal(dir, "{\"event\":\"Loadout\",\"CargoCapacity\":256}");

            var summary = EliteInstanceScanner.ReadJournalSummary(path);

            Assert.Equal(256, summary.CargoCapacity);
        }

        [Fact]
        public void ReadJournalSummary_NoCargoEventAtAll_DefaultsToZeroNotUnknown()
        {
            using var dir = new TempDirectory();
            var path = WriteJournal(dir, "{\"event\":\"Loadout\",\"CargoCapacity\":256}");

            var summary = EliteInstanceScanner.ReadJournalSummary(path);

            Assert.Equal(0, summary.CurrentCargo);
            Assert.Equal(0, summary.CurrentTritium);
        }

        [Fact]
        public void ReadJournalSummary_CargoEventWithInventory_ResyncsTritiumAndExcludesFromCurrentCargo()
        {
            using var dir = new TempDirectory();
            var path = WriteJournal(dir,
                "{\"event\":\"Cargo\",\"Vessel\":\"Ship\",\"Count\":50,\"Inventory\":[{\"Name\":\"tritium\",\"Count\":20},{\"Name\":\"gold\",\"Count\":30}]}");

            var summary = EliteInstanceScanner.ReadJournalSummary(path);

            Assert.Equal(20, summary.CurrentTritium);
            Assert.Equal(30, summary.CurrentCargo); // 50 total - 20 tritium
        }

        [Fact]
        public void ReadJournalSummary_CarrierCargoEvent_IsIgnored()
        {
            using var dir = new TempDirectory();
            var path = WriteJournal(dir, "{\"event\":\"Cargo\",\"Vessel\":\"Carrier\",\"Count\":900}");

            var summary = EliteInstanceScanner.ReadJournalSummary(path);

            Assert.Equal(0, summary.CurrentCargo);
        }

        [Fact]
        public void ReadJournalSummary_CargoTransferToShip_IncreasesTrackedTritium()
        {
            using var dir = new TempDirectory();
            var path = WriteJournal(dir,
                "{\"event\":\"Cargo\",\"Vessel\":\"Ship\",\"Count\":0}",
                "{\"event\":\"CargoTransfer\",\"Transfers\":[{\"Type\":\"tritium\",\"Count\":15,\"Direction\":\"toship\"}]}");

            var summary = EliteInstanceScanner.ReadJournalSummary(path);

            Assert.Equal(15, summary.CurrentTritium);
        }

        [Fact]
        public void ReadJournalSummary_CargoTransferToCarrier_DecreasesTrackedTritium()
        {
            using var dir = new TempDirectory();
            var path = WriteJournal(dir,
                "{\"event\":\"Cargo\",\"Vessel\":\"Ship\",\"Count\":20,\"Inventory\":[{\"Name\":\"tritium\",\"Count\":20}]}",
                "{\"event\":\"CargoTransfer\",\"Transfers\":[{\"Type\":\"tritium\",\"Count\":15,\"Direction\":\"tocarrier\"}]}");

            var summary = EliteInstanceScanner.ReadJournalSummary(path);

            Assert.Equal(5, summary.CurrentTritium);
        }

        [Fact]
        public void ReadJournalSummary_CargoTransfer_NeverGoesNegative()
        {
            using var dir = new TempDirectory();
            var path = WriteJournal(dir,
                "{\"event\":\"CargoTransfer\",\"Transfers\":[{\"Type\":\"tritium\",\"Count\":999,\"Direction\":\"tocarrier\"}]}");

            var summary = EliteInstanceScanner.ReadJournalSummary(path);

            Assert.Equal(0, summary.CurrentTritium);
        }

        [Fact]
        public void ReadJournalSummary_CarrierDepositFuel_DecreasesShipTritiumAndSetsCarrierFuel()
        {
            // CarrierFuelLevel is only ever resolved against a CarrierID that also has a
            // carrierLocationsById entry (from CarrierJump/CarrierLocation) - CarrierStats alone
            // establishes ownership (CarrierId) but not a resolved location/fuel reading, so a
            // CarrierLocation line is required here too, the same as a real session would have
            // logged before ever depositing fuel.
            using var dir = new TempDirectory();
            var path = WriteJournal(dir,
                "{\"event\":\"Cargo\",\"Vessel\":\"Ship\",\"Count\":30,\"Inventory\":[{\"Name\":\"tritium\",\"Count\":30}]}",
                "{\"event\":\"CarrierStats\",\"CarrierID\":555,\"Name\":\"Serenity\"}",
                "{\"event\":\"CarrierLocation\",\"CarrierID\":555,\"CarrierType\":\"FleetCarrier\",\"StarSystem\":\"Deciat\"}",
                "{\"event\":\"CarrierDepositFuel\",\"CarrierID\":555,\"Amount\":10,\"Total\":600}");

            var summary = EliteInstanceScanner.ReadJournalSummary(path);

            Assert.Equal(20, summary.CurrentTritium);
            Assert.Equal(600, summary.CarrierFuelLevel);
        }

        [Fact]
        public void ReadJournalSummary_MarketBuyTritium_IncreasesTracked()
        {
            using var dir = new TempDirectory();
            var path = WriteJournal(dir, "{\"event\":\"MarketBuy\",\"Type\":\"tritium\",\"Count\":25}");

            var summary = EliteInstanceScanner.ReadJournalSummary(path);

            Assert.Equal(25, summary.CurrentTritium);
        }

        [Fact]
        public void ReadJournalSummary_MarketSellTritium_DecreasesTracked()
        {
            using var dir = new TempDirectory();
            var path = WriteJournal(dir,
                "{\"event\":\"MarketBuy\",\"Type\":\"tritium\",\"Count\":25}",
                "{\"event\":\"MarketSell\",\"Type\":\"tritium\",\"Count\":10}");

            var summary = EliteInstanceScanner.ReadJournalSummary(path);

            Assert.Equal(15, summary.CurrentTritium);
        }

        [Fact]
        public void ReadJournalSummary_MarketBuyOfNonTritium_IsIgnored()
        {
            using var dir = new TempDirectory();
            var path = WriteJournal(dir, "{\"event\":\"MarketBuy\",\"Type\":\"gold\",\"Count\":25}");

            var summary = EliteInstanceScanner.ReadJournalSummary(path);

            Assert.Equal(0, summary.CurrentTritium);
        }

        [Fact]
        public void ReadJournalSummary_Docked_SetsSystemAndStation()
        {
            using var dir = new TempDirectory();
            var path = WriteJournal(dir, "{\"event\":\"Docked\",\"StarSystem\":\"Sol\",\"StationName\":\"Abraham Lincoln\"}");

            var summary = EliteInstanceScanner.ReadJournalSummary(path);

            Assert.Equal("Sol", summary.CurrentSystem);
            Assert.Equal("Abraham Lincoln", summary.CurrentStation);
        }

        [Fact]
        public void ReadJournalSummary_Undocked_ClearsStationButKeepsSystem()
        {
            using var dir = new TempDirectory();
            var path = WriteJournal(dir,
                "{\"event\":\"Docked\",\"StarSystem\":\"Sol\",\"StationName\":\"Abraham Lincoln\"}",
                "{\"event\":\"Undocked\"}");

            var summary = EliteInstanceScanner.ReadJournalSummary(path);

            Assert.Equal("Sol", summary.CurrentSystem);
            Assert.Null(summary.CurrentStation);
        }

        [Fact]
        public void ReadJournalSummary_FSDJump_UpdatesSystemAndClearsStation()
        {
            using var dir = new TempDirectory();
            var path = WriteJournal(dir,
                "{\"event\":\"Docked\",\"StarSystem\":\"Sol\",\"StationName\":\"Abraham Lincoln\"}",
                "{\"event\":\"FSDJump\",\"StarSystem\":\"Alpha Centauri\"}");

            var summary = EliteInstanceScanner.ReadJournalSummary(path);

            Assert.Equal("Alpha Centauri", summary.CurrentSystem);
            Assert.Null(summary.CurrentStation);
        }

        [Fact]
        public void ReadJournalSummary_Location_NotDocked_ClearsStation()
        {
            using var dir = new TempDirectory();
            var path = WriteJournal(dir, "{\"event\":\"Location\",\"StarSystem\":\"Sol\",\"Docked\":false}");

            var summary = EliteInstanceScanner.ReadJournalSummary(path);

            Assert.Equal("Sol", summary.CurrentSystem);
            Assert.Null(summary.CurrentStation);
        }

        [Fact]
        public void ReadJournalSummary_CarrierStatsThenMatchingLocation_ResolvesOwnedCarrier()
        {
            using var dir = new TempDirectory();
            var path = WriteJournal(dir,
                "{\"event\":\"CarrierStats\",\"CarrierID\":777,\"Name\":\"Serenity\",\"FuelLevel\":500}",
                "{\"event\":\"CarrierLocation\",\"CarrierID\":777,\"CarrierType\":\"FleetCarrier\",\"StarSystem\":\"Deciat\"}");

            var summary = EliteInstanceScanner.ReadJournalSummary(path);

            Assert.Equal("Serenity", summary.CarrierName);
            Assert.Equal(777, summary.CarrierId);
            Assert.Equal("Deciat", summary.CarrierSystem);
            Assert.Equal(500, summary.CarrierFuelLevel);
        }

        [Fact]
        public void ReadJournalSummary_SquadronCarrierLocation_IsIgnored()
        {
            using var dir = new TempDirectory();
            var path = WriteJournal(dir,
                "{\"event\":\"CarrierStats\",\"CarrierID\":777,\"Name\":\"Serenity\"}",
                "{\"event\":\"CarrierLocation\",\"CarrierID\":888,\"CarrierType\":\"SquadronCarrier\",\"StarSystem\":\"Deciat\"}");

            var summary = EliteInstanceScanner.ReadJournalSummary(path);

            Assert.Null(summary.CarrierSystem);
        }

        [Fact]
        public void ReadJournalSummary_NoCarrierStats_ButSingleCarrierReferenced_IsAssumedOwned()
        {
            using var dir = new TempDirectory();
            var path = WriteJournal(dir,
                "{\"event\":\"CarrierLocation\",\"CarrierID\":42,\"CarrierType\":\"FleetCarrier\",\"StarSystem\":\"Deciat\"}");

            var summary = EliteInstanceScanner.ReadJournalSummary(path);

            Assert.Equal(42, summary.CarrierId);
            Assert.Equal("Deciat", summary.CarrierSystem);
        }

        [Fact]
        public void ReadJournalSummary_NoCarrierStats_MultipleCarriersReferenced_OwnershipUnresolved()
        {
            using var dir = new TempDirectory();
            var path = WriteJournal(dir,
                "{\"event\":\"CarrierLocation\",\"CarrierID\":1,\"CarrierType\":\"FleetCarrier\",\"StarSystem\":\"Deciat\"}",
                "{\"event\":\"CarrierLocation\",\"CarrierID\":2,\"CarrierType\":\"FleetCarrier\",\"StarSystem\":\"Sol\"}");

            var summary = EliteInstanceScanner.ReadJournalSummary(path);

            Assert.Null(summary.CarrierId);
            Assert.Null(summary.CarrierSystem);
        }

        [Fact]
        public void ReadJournalSummary_CarrierJumpDocked_UpdatesCarrierLocationAndCurrentStation()
        {
            using var dir = new TempDirectory();
            var path = WriteJournal(dir,
                "{\"event\":\"CarrierStats\",\"CarrierID\":9,\"Name\":\"Serenity\"}",
                "{\"event\":\"CarrierJump\",\"MarketID\":9,\"StarSystem\":\"Deciat\",\"Body\":\"Deciat A\",\"Docked\":true,\"StationName\":\"Serenity\"}");

            var summary = EliteInstanceScanner.ReadJournalSummary(path);

            Assert.Equal("Deciat", summary.CarrierSystem);
            Assert.Equal("Deciat A", summary.CarrierBody);
            Assert.Equal("Deciat", summary.CurrentSystem);
            Assert.Equal("Serenity", summary.CurrentStation);
        }

        [Fact]
        public void ReadJournalSummary_UnparsableJsonLine_IsSkippedNotThrown()
        {
            using var dir = new TempDirectory();
            var path = WriteJournal(dir, "not valid json", "{\"event\":\"Commander\",\"FID\":\"F1\",\"Name\":\"X\"}");

            var summary = EliteInstanceScanner.ReadJournalSummary(path);

            Assert.Equal("X", summary.CommanderName);
        }

        [Fact]
        public void ReadJournalSummary_MissingFile_ReturnsEmptySummary()
        {
            var summary = EliteInstanceScanner.ReadJournalSummary(@"C:\does\not\exist.log");

            Assert.Null(summary.CommanderName);
            Assert.Equal(0, summary.CurrentCargo);
        }

        [Fact]
        public void ReadJournalSummary_LatestOccurrenceWinsForRepeatedFields()
        {
            using var dir = new TempDirectory();
            var path = WriteJournal(dir,
                "{\"event\":\"Commander\",\"FID\":\"F1\",\"Name\":\"First\"}",
                "{\"event\":\"Commander\",\"FID\":\"F2\",\"Name\":\"Second\"}");

            var summary = EliteInstanceScanner.ReadJournalSummary(path);

            Assert.Equal("Second", summary.CommanderName);
            Assert.Equal("F2", summary.Fid);
        }
    }

    public class EliteInstanceScannerMatchJournalTests
    {
        // MatchJournal reads the real process's own StartTime (not injectable), so these tests
        // anchor candidate timestamps to the current test process's actual start time instead of
        // an arbitrary fixed instant.
        private static System.Diagnostics.Process CurrentProcess => System.Diagnostics.Process.GetCurrentProcess();

        [Fact]
        public void MatchJournal_ClosestCandidateWithinTolerance_IsChosen()
        {
            var process = CurrentProcess;
            var startUtc = process.StartTime.ToUniversalTime();
            var candidates = new List<(string Path, DateTime TimestampUtc)>
            {
                ("far.log", startUtc.AddMinutes(-4)),
                ("close.log", startUtc.AddSeconds(-30)),
            };

            var match = EliteInstanceScanner.MatchJournal(process, candidates, new HashSet<string>());

            Assert.Equal("close.log", match);
        }

        [Fact]
        public void MatchJournal_NoCandidateWithinTolerance_ReturnsNull()
        {
            var process = CurrentProcess;
            var startUtc = process.StartTime.ToUniversalTime();
            var candidates = new List<(string Path, DateTime TimestampUtc)>
            {
                ("too-far.log", startUtc.AddMinutes(-10)),
            };

            var match = EliteInstanceScanner.MatchJournal(process, candidates, new HashSet<string>());

            Assert.Null(match);
        }

        [Fact]
        public void MatchJournal_AlreadyAssignedCandidate_IsSkipped()
        {
            var process = CurrentProcess;
            var startUtc = process.StartTime.ToUniversalTime();
            var candidates = new List<(string Path, DateTime TimestampUtc)>
            {
                ("best.log", startUtc),
                ("second-best.log", startUtc.AddSeconds(10)),
            };

            var match = EliteInstanceScanner.MatchJournal(process, candidates, new HashSet<string> { "best.log" });

            Assert.Equal("second-best.log", match);
        }

        [Fact]
        public void MatchJournal_NoCandidates_ReturnsNull()
        {
            var match = EliteInstanceScanner.MatchJournal(CurrentProcess, new List<(string, DateTime)>(), new HashSet<string>());
            Assert.Null(match);
        }
    }
}
