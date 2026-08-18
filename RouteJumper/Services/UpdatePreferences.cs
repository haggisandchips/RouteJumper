using RouteJumper.Common;

namespace RouteJumper.Services
{
    /// <summary>
    /// Owns whether ED:FC Auto Pilot silently checks the project's GitHub Releases for a newer
    /// version on every launch (§3.7) - bound to directly from the Preferences dialog's own
    /// "Updates" section, the same "doubles as its own ViewModel" pattern SpeechAnnouncer already
    /// uses. Never consulted by File/Help &gt; Check for Updates itself, which always runs
    /// regardless of this setting - that's an explicit, on-demand request, not the background
    /// check this toggle controls.
    /// </summary>
    public class UpdatePreferences : ObservableObject
    {
        private const string AutoCheckSettingKey = "AutoCheckForUpdates";

        private readonly AppSettingsStore _settings;
        private bool _autoCheckEnabled;

        public UpdatePreferences(AppSettingsStore settings)
        {
            _settings = settings;
            _autoCheckEnabled = settings.GetBool(AutoCheckSettingKey, defaultValue: true);
        }

        /// <summary>Whether App.xaml.cs's silent startup check runs at all - on by default until explicitly turned off.</summary>
        public bool AutoCheckEnabled
        {
            get => _autoCheckEnabled;
            set
            {
                if (SetProperty(ref _autoCheckEnabled, value))
                {
                    _settings.SetBool(AutoCheckSettingKey, value);
                }
            }
        }
    }
}
