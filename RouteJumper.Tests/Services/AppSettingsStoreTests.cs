using System.IO;
using RouteJumper.Services;
using RouteJumper.Tests.TestSupport;
using Xunit;

namespace RouteJumper.Tests.Services
{
    public class AppSettingsStoreTests
    {
        [Fact]
        public void GetString_UnknownKey_ReturnsNull()
        {
            using var dir = new TempDirectory();
            var store = new AppSettingsStore(dir.Path);

            Assert.Null(store.GetString("missing"));
        }

        [Fact]
        public void SetString_ThenGetString_RoundTrips()
        {
            using var dir = new TempDirectory();
            var store = new AppSettingsStore(dir.Path);

            store.SetString("RouteText", "Sol\nAlpha Centauri");

            Assert.Equal("Sol\nAlpha Centauri", store.GetString("RouteText"));
        }

        [Fact]
        public void SetString_ExistingKey_Overwrites()
        {
            using var dir = new TempDirectory();
            var store = new AppSettingsStore(dir.Path);

            store.SetString("Key", "first");
            store.SetString("Key", "second");

            Assert.Equal("second", store.GetString("Key"));
        }

        [Fact]
        public void GetDouble_UnknownKey_ReturnsNull()
        {
            using var dir = new TempDirectory();
            var store = new AppSettingsStore(dir.Path);

            Assert.Null(store.GetDouble("missing"));
        }

        [Fact]
        public void SetDouble_ThenGetDouble_RoundTrips()
        {
            using var dir = new TempDirectory();
            var store = new AppSettingsStore(dir.Path);

            store.SetDouble("AutoWaitMs", 300.0);

            Assert.Equal(300.0, store.GetDouble("AutoWaitMs"));
        }

        [Fact]
        public void GetBool_UnknownKey_ReturnsSuppliedDefault()
        {
            using var dir = new TempDirectory();
            var store = new AppSettingsStore(dir.Path);

            Assert.False(store.GetBool("missing"));
            Assert.True(store.GetBool("missing", true));
        }

        [Fact]
        public void SetBool_ThenGetBool_RoundTrips()
        {
            using var dir = new TempDirectory();
            var store = new AppSettingsStore(dir.Path);

            store.SetBool("Flag", true);
            Assert.True(store.GetBool("Flag"));

            store.SetBool("Flag", false);
            Assert.False(store.GetBool("Flag"));
        }

        [Fact]
        public void NewStore_PointingAtSameDirectory_SeesPreviouslyPersistedValues()
        {
            using var dir = new TempDirectory();
            new AppSettingsStore(dir.Path).SetString("Key", "value");

            var reopened = new AppSettingsStore(dir.Path);

            Assert.Equal("value", reopened.GetString("Key"));
        }

        [Fact]
        public void Constructor_CreatesDatabaseFile()
        {
            using var dir = new TempDirectory();
            _ = new AppSettingsStore(dir.Path);

            Assert.True(File.Exists(Path.Combine(dir.Path, "routejumper.db")));
        }
    }

    public class AppConfigStoreTests
    {
        [Fact]
        public void JournalDirectory_FirstRead_CreatesConfigFileWithDefault()
        {
            using var dir = new TempDirectory();
            var store = new AppConfigStore(dir.Path);

            var journalDirectory = store.JournalDirectory;

            Assert.True(File.Exists(Path.Combine(dir.Path, "routejumper.conf")));
            Assert.Contains("Saved Games", journalDirectory);
            Assert.Contains("Frontier Developments", journalDirectory);
            Assert.Contains("Elite Dangerous", journalDirectory);
        }

        [Fact]
        public void JournalDirectory_HandEditedConfig_TakesEffectOnNextRead()
        {
            using var dir = new TempDirectory();
            var store = new AppConfigStore(dir.Path);
            _ = store.JournalDirectory; // creates the file

            File.WriteAllLines(Path.Combine(dir.Path, "routejumper.conf"), new[] { "JournalDirectory=C:\\CustomJournals" });

            Assert.Equal("C:\\CustomJournals", store.JournalDirectory);
        }

        [Fact]
        public void JournalDirectory_BlankValueInConfig_FallsBackToDefault()
        {
            using var dir = new TempDirectory();
            var store = new AppConfigStore(dir.Path);

            File.WriteAllLines(Path.Combine(dir.Path, "routejumper.conf"), new[] { "JournalDirectory=" });

            Assert.Contains("Saved Games", store.JournalDirectory);
        }

        [Fact]
        public void JournalDirectory_IgnoresCommentsAndBlankLines()
        {
            using var dir = new TempDirectory();
            File.WriteAllLines(Path.Combine(dir.Path, "routejumper.conf"), new[]
            {
                "# a comment",
                "",
                "JournalDirectory=D:\\Journals"
            });

            var store = new AppConfigStore(dir.Path);

            Assert.Equal("D:\\Journals", store.JournalDirectory);
        }

        [Fact]
        public void JournalDirectory_MissingKeyEntirely_FallsBackToDefault()
        {
            using var dir = new TempDirectory();
            File.WriteAllLines(Path.Combine(dir.Path, "routejumper.conf"), new[] { "SomeOtherKey=value" });

            var store = new AppConfigStore(dir.Path);

            Assert.Contains("Saved Games", store.JournalDirectory);
        }

        [Fact]
        public void SpanshAutocompleteDebounceMs_NoConfigFileYet_DefaultsTo250AndIsWrittenToNewFile()
        {
            using var dir = new TempDirectory();
            var store = new AppConfigStore(dir.Path);

            Assert.Equal(250, store.SpanshAutocompleteDebounceMs);

            var written = File.ReadAllText(Path.Combine(dir.Path, "routejumper.conf"));
            Assert.Contains("SpanshAutocompleteDebounceMs=250", written);
        }

        [Fact]
        public void SpanshAutocompleteDebounceMs_HandEditedConfigFile_ReadsTheNewValue()
        {
            using var dir = new TempDirectory();
            File.WriteAllLines(Path.Combine(dir.Path, "routejumper.conf"), new[] { "SpanshAutocompleteDebounceMs=500" });
            var store = new AppConfigStore(dir.Path);

            Assert.Equal(500, store.SpanshAutocompleteDebounceMs);
        }

        [Fact]
        public void SpanshAutocompleteDebounceMs_InvalidValue_FallsBackToDefault()
        {
            using var dir = new TempDirectory();
            File.WriteAllLines(Path.Combine(dir.Path, "routejumper.conf"), new[] { "SpanshAutocompleteDebounceMs=not-a-number" });
            var store = new AppConfigStore(dir.Path);

            Assert.Equal(250, store.SpanshAutocompleteDebounceMs);
        }

        // ===================== Backfilling keys missing from an existing config file (e.g.
        // written by an older version of the app, before some setting existed) =====================

        [Fact]
        public void ExistingFileMissingASetting_AppendsItWithDefaultOnNextRead()
        {
            using var dir = new TempDirectory();
            var configPath = Path.Combine(dir.Path, "routejumper.conf");
            File.WriteAllLines(configPath, new[] { "JournalDirectory=D:\\Journals" });
            var store = new AppConfigStore(dir.Path);

            var debounce = store.SpanshAutocompleteDebounceMs;

            Assert.Equal(250, debounce);
            var written = File.ReadAllText(configPath);
            Assert.Contains("SpanshAutocompleteDebounceMs=250", written);
        }

        [Fact]
        public void ExistingFileMissingASetting_LeavesExistingValuesUntouched()
        {
            using var dir = new TempDirectory();
            var configPath = Path.Combine(dir.Path, "routejumper.conf");
            File.WriteAllLines(configPath, new[] { "JournalDirectory=D:\\Journals", "LogRetentionDays=30" });
            var store = new AppConfigStore(dir.Path);

            _ = store.SpanshAutocompleteDebounceMs; // triggers the backfill

            Assert.Equal("D:\\Journals", store.JournalDirectory);
            Assert.Equal(30, store.LogRetentionDays);
        }

        [Fact]
        public void ExistingFileMissingMultipleSettings_BackfillsAllOfThemInOnePass()
        {
            using var dir = new TempDirectory();
            var configPath = Path.Combine(dir.Path, "routejumper.conf");
            File.WriteAllLines(configPath, new[] { "JournalDirectory=D:\\Journals" });
            var store = new AppConfigStore(dir.Path);

            _ = store.LogRetentionDays;

            var written = File.ReadAllText(configPath);
            Assert.Contains("LogRetentionDays=7", written);
            Assert.Contains("LogMaxFileSizeMB=10", written);
            Assert.Contains("LogMaxTotalSizeMB=100", written);
            Assert.Contains("EdsmUnresolvedRetryHours=12", written);
            Assert.Contains("SpanshAutocompleteDebounceMs=250", written);
        }

        [Fact]
        public void ExistingFileWithEveryKeyAlready_NoWriteOnNextRead()
        {
            using var dir = new TempDirectory();
            var configPath = Path.Combine(dir.Path, "routejumper.conf");
            var store = new AppConfigStore(dir.Path);
            _ = store.SpanshAutocompleteDebounceMs; // creates the file with every key already present
            var writtenAfterCreate = File.ReadAllText(configPath);

            _ = store.SpanshAutocompleteDebounceMs; // a second read of an already-complete file

            Assert.Equal(writtenAfterCreate, File.ReadAllText(configPath));
        }
    }
}
