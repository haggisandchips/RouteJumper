using RouteJumper.Services;
using RouteJumper.Tests.TestSupport;
using Xunit;

namespace RouteJumper.Tests.Services
{
    public class UpdatePreferencesTests
    {
        [Fact]
        public void Constructor_DefaultsToAutoCheckEnabled()
        {
            using var dir = new TempDirectory();
            var preferences = new UpdatePreferences(new AppSettingsStore(dir.Path));

            Assert.True(preferences.AutoCheckEnabled);
        }

        [Fact]
        public void AutoCheckEnabled_SetFalse_PersistsAcrossInstances()
        {
            using var dir = new TempDirectory();
            var settings = new AppSettingsStore(dir.Path);
            var preferences = new UpdatePreferences(settings);

            preferences.AutoCheckEnabled = false;

            var restored = new UpdatePreferences(new AppSettingsStore(dir.Path));
            Assert.False(restored.AutoCheckEnabled);
        }

        [Fact]
        public void AutoCheckEnabled_SetTrueAfterFalse_PersistsAcrossInstances()
        {
            using var dir = new TempDirectory();
            var settings = new AppSettingsStore(dir.Path);
            new UpdatePreferences(settings) { AutoCheckEnabled = false };

            new UpdatePreferences(new AppSettingsStore(dir.Path)) { AutoCheckEnabled = true };

            var restored = new UpdatePreferences(new AppSettingsStore(dir.Path));
            Assert.True(restored.AutoCheckEnabled);
        }
    }
}
