using RouteJumper.Services;
using RouteJumper.Tests.TestSupport;
using Xunit;

namespace RouteJumper.Tests.Services
{
    public class SpeechAnnouncerTests
    {
        private static (SpeechAnnouncer Announcer, FakeSpeechEngine Engine) Create(TempDirectory dir)
        {
            var engine = new FakeSpeechEngine();
            var announcer = new SpeechAnnouncer(new AppSettingsStore(dir.Path), engine);
            return (announcer, engine);
        }

        [Fact]
        public void Constructor_DefaultsToFullVolumeAndUnmuted()
        {
            using var dir = new TempDirectory();
            var (announcer, _) = Create(dir);

            Assert.Equal(100, announcer.Volume);
            Assert.False(announcer.Muted);
            Assert.Null(announcer.VoiceName);
        }

        [Fact]
        public void Constructor_AppliesDefaultsToEngine()
        {
            using var dir = new TempDirectory();
            var (_, engine) = Create(dir);

            Assert.Equal(100, Assert.Single(engine.VolumeSelections));
            Assert.Null(Assert.Single(engine.VoiceSelections));
        }

        [Fact]
        public void AvailableVoices_ReflectsTheEnginesInstalledVoices()
        {
            using var dir = new TempDirectory();
            var engine = new FakeSpeechEngine { InstalledVoiceNames = new[] { "Voice A", "Voice B", "Voice C" } };
            var announcer = new SpeechAnnouncer(new AppSettingsStore(dir.Path), engine);

            Assert.Equal(new[] { "Voice A", "Voice B", "Voice C" }, announcer.AvailableVoices);
        }

        [Fact]
        public void VoiceName_Setter_PersistsAndAppliesToEngine()
        {
            using var dir = new TempDirectory();
            var (announcer, engine) = Create(dir);

            announcer.VoiceName = "Voice B";

            Assert.Equal("Voice B", engine.VoiceSelections[^1]);
            Assert.Equal("Voice B", new AppSettingsStore(dir.Path).GetString("SpeechVoiceName"));
        }

        [Fact]
        public void VoiceName_PersistsAndIsRestoredByAFreshInstance()
        {
            using var dir = new TempDirectory();
            Create(dir).Announcer.VoiceName = "Voice B";

            var (restored, _) = Create(dir);

            Assert.Equal("Voice B", restored.VoiceName);
        }

        [Fact]
        public void VoiceName_RestoreFallsBackToDefault_WhenPersistedVoiceNoLongerInstalled()
        {
            using var dir = new TempDirectory();
            Create(dir).Announcer.VoiceName = "Voice B";

            var engine = new FakeSpeechEngine { InstalledVoiceNames = new[] { "Voice A" } };
            var restored = new SpeechAnnouncer(new AppSettingsStore(dir.Path), engine);

            Assert.Null(restored.VoiceName);
        }

        [Fact]
        public void VoiceName_RestoreFallsBackToDefault_ReNormalizesThePersistedValueToBlank()
        {
            using var dir = new TempDirectory();
            Create(dir).Announcer.VoiceName = "Voice B";
            var engine = new FakeSpeechEngine { InstalledVoiceNames = new[] { "Voice A" } };
            _ = new SpeechAnnouncer(new AppSettingsStore(dir.Path), engine);

            Assert.Equal(string.Empty, new AppSettingsStore(dir.Path).GetString("SpeechVoiceName"));
        }

        [Fact]
        public void VoiceName_Restore_MigratesALegacyRawSapiNameToItsCurrentFriendlyName()
        {
            using var dir = new TempDirectory();
            new AppSettingsStore(dir.Path).SetString("SpeechVoiceName", "Microsoft David");
            var engine = new FakeSpeechEngine { InstalledVoiceNames = new[] { "David" } };

            var announcer = new SpeechAnnouncer(new AppSettingsStore(dir.Path), engine);

            Assert.Equal("David", announcer.VoiceName);
            Assert.Equal("David", new AppSettingsStore(dir.Path).GetString("SpeechVoiceName"));
        }

        [Fact]
        public void VoiceName_Restore_MigratesALegacyDesktopSuffixedNameToItsCurrentFriendlyName()
        {
            using var dir = new TempDirectory();
            new AppSettingsStore(dir.Path).SetString("SpeechVoiceName", "Microsoft David Desktop");
            var engine = new FakeSpeechEngine { InstalledVoiceNames = new[] { "David" } };

            var announcer = new SpeechAnnouncer(new AppSettingsStore(dir.Path), engine);

            Assert.Equal("David", announcer.VoiceName);
        }

        [Fact]
        public void ClearVoiceCommand_ResetsVoiceNameToNull()
        {
            using var dir = new TempDirectory();
            var (announcer, _) = Create(dir);
            announcer.VoiceName = "Voice B";

            announcer.ClearVoiceCommand.Execute(null);

            Assert.Null(announcer.VoiceName);
        }

        [Fact]
        public void Volume_Setter_ClampsToZeroToOneHundred()
        {
            using var dir = new TempDirectory();
            var (announcer, engine) = Create(dir);

            announcer.Volume = 250;
            Assert.Equal(100, announcer.Volume);

            announcer.Volume = -10;
            Assert.Equal(0, announcer.Volume);

            Assert.Equal(0, engine.VolumeSelections[^1]);
        }

        [Fact]
        public void Volume_PersistsAndIsRestoredByAFreshInstance()
        {
            using var dir = new TempDirectory();
            Create(dir).Announcer.Volume = 42;

            var (restored, _) = Create(dir);

            Assert.Equal(42, restored.Volume);
        }

        [Fact]
        public void Muted_PersistsAndIsRestoredByAFreshInstance()
        {
            using var dir = new TempDirectory();
            Create(dir).Announcer.Muted = true;

            var (restored, _) = Create(dir);

            Assert.True(restored.Muted);
        }

        [Fact]
        public void ToggleMuteCommand_FlipsMuted()
        {
            using var dir = new TempDirectory();
            var (announcer, _) = Create(dir);

            announcer.ToggleMuteCommand.Execute(null);
            Assert.True(announcer.Muted);

            announcer.ToggleMuteCommand.Execute(null);
            Assert.False(announcer.Muted);
        }

        [Fact]
        public void Speak_WhileUnmuted_CallsEngine()
        {
            using var dir = new TempDirectory();
            var (announcer, engine) = Create(dir);

            announcer.Speak("Plotting in 30 seconds");

            Assert.Equal("Plotting in 30 seconds", Assert.Single(engine.SpokenTexts));
        }

        [Fact]
        public void Speak_WhileMuted_DoesNotCallEngine()
        {
            using var dir = new TempDirectory();
            var (announcer, engine) = Create(dir);
            announcer.Muted = true;

            announcer.Speak("Refueling in 30 seconds");

            Assert.Empty(engine.SpokenTexts);
        }

        [Fact]
        public void Speak_BlankText_DoesNotCallEngine()
        {
            using var dir = new TempDirectory();
            var (announcer, engine) = Create(dir);

            announcer.Speak("   ");

            Assert.Empty(engine.SpokenTexts);
        }

        [Fact]
        public void TestCommand_SpeaksEvenWhileMuted()
        {
            using var dir = new TempDirectory();
            var (announcer, engine) = Create(dir);
            announcer.Muted = true;

            announcer.TestCommand.Execute(null);

            Assert.Single(engine.SpokenTexts);
        }

        [Fact]
        public void Dispose_DisposesTheEngine()
        {
            using var dir = new TempDirectory();
            var (announcer, engine) = Create(dir);

            announcer.Dispose();

            Assert.True(engine.Disposed);
        }
    }
}
