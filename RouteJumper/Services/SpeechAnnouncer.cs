using RouteJumper.Common;

namespace RouteJumper.Services
{
    /// <summary>
    /// Owns spoken-announcement state (selected voice, volume, muted) and its persistence, and is
    /// the single place anything in the app goes through to actually speak - Auto Pilot's
    /// plot/refuel countdowns (see AutoPilotController) via <see cref="Speak"/>, and the
    /// Preferences dialog's Test button via <see cref="TestCommand"/>. Bound to directly from
    /// both MainWindow (the always-visible mute button) and the Preferences dialog, so it doubles
    /// as its own ViewModel rather than needing a separate wrapper.
    /// </summary>
    public class SpeechAnnouncer : ObservableObject, IDisposable
    {
        private const string VoiceNameSettingKey = "SpeechVoiceName";
        private const string VolumeSettingKey = "SpeechVolume";
        private const string MutedSettingKey = "SpeechMuted";
        private const int DefaultVolume = 100;
        private const string TestPhrase = "This is a test of the announcement voice.";

        private readonly AppSettingsStore _settings;
        private readonly ISpeechEngine _engine;

        private string? _voiceName;
        private int _volume;
        private bool _muted;

        public SpeechAnnouncer(AppSettingsStore settings, ISpeechEngine engine)
        {
            _settings = settings;
            _engine = engine;

            AvailableVoices = engine.GetInstalledVoiceNames();

            _volume = _settings.GetDouble(VolumeSettingKey) is { } storedVolume
                ? Math.Clamp((int)storedVolume, 0, 100)
                : DefaultVolume;
            _muted = _settings.GetBool(MutedSettingKey);

            var storedVoice = _settings.GetString(VoiceNameSettingKey);
            _voiceName = ResolvePersistedVoiceName(storedVoice);
            if (_voiceName != storedVoice)
            {
                // Either a legacy raw engine name (persisted before display names were cleaned
                // up - see ResolvePersistedVoiceName) that's now been normalized to its current
                // friendly name, or a voice that's genuinely gone (uninstalled, or settings
                // carried over from another PC) falling back to blank - either way, save the
                // resolved value so future restores match directly without this fallback.
                _settings.SetString(VoiceNameSettingKey, _voiceName ?? string.Empty);
            }

            _engine.SetVolume(_volume);
            _engine.SetVoice(_voiceName);

            TestCommand = new RelayCommand(() => _engine.SpeakAsync(TestPhrase));
            ToggleMuteCommand = new RelayCommand(() => Muted = !Muted);
            ClearVoiceCommand = new RelayCommand(() => VoiceName = null);
        }

        /// <summary>Every voice installed on this machine - for the Preferences dialog's voice picker.</summary>
        public IReadOnlyList<string> AvailableVoices { get; }

        /// <summary>
        /// Matches a persisted voice name against AvailableVoices, falling back to null (the
        /// engine's own default) rather than a stale name if it no longer exists on this machine
        /// - e.g. settings carried over from another PC, or the voice was uninstalled since it
        /// was last selected. Also recognizes a name persisted before display names were cleaned
        /// up - SAPI's raw "Microsoft X"/"X Desktop" naming - by re-applying that same cleanup to
        /// the stored value, so a voice already chosen isn't silently dropped back to "system
        /// default" the first time this runs after that change.
        /// </summary>
        private string? ResolvePersistedVoiceName(string? stored)
        {
            if (string.IsNullOrEmpty(stored))
            {
                return null;
            }

            if (AvailableVoices.Contains(stored))
            {
                return stored;
            }

            var legacyFriendlyName = stored.StartsWith("Microsoft ", StringComparison.OrdinalIgnoreCase)
                ? stored["Microsoft ".Length..]
                : stored;
            legacyFriendlyName = legacyFriendlyName.EndsWith(" Desktop", StringComparison.OrdinalIgnoreCase)
                ? legacyFriendlyName[..^" Desktop".Length]
                : legacyFriendlyName;

            return AvailableVoices.Contains(legacyFriendlyName) ? legacyFriendlyName : null;
        }

        /// <summary>Speaks a fixed sample phrase to preview the current voice/volume - always audible, even while Muted, since pressing it is itself an explicit request to hear it.</summary>
        public RelayCommand TestCommand { get; }

        /// <summary>Flips Muted - bound to the main window's always-visible mute button.</summary>
        public RelayCommand ToggleMuteCommand { get; }

        /// <summary>Resets VoiceName back to the engine's own default - the Voice picker's ComboBox has no blank entry of its own to click back to once a specific voice has been chosen.</summary>
        public RelayCommand ClearVoiceCommand { get; }

        /// <summary>The selected voice's name, or null for the engine's own default.</summary>
        public string? VoiceName
        {
            get => _voiceName;
            set
            {
                if (SetProperty(ref _voiceName, value))
                {
                    _settings.SetString(VoiceNameSettingKey, value ?? string.Empty);
                    _engine.SetVoice(value);
                }
            }
        }

        /// <summary>0-100.</summary>
        public int Volume
        {
            get => _volume;
            set
            {
                var clamped = Math.Clamp(value, 0, 100);
                if (SetProperty(ref _volume, clamped))
                {
                    _settings.SetDouble(VolumeSettingKey, clamped);
                    _engine.SetVolume(clamped);
                }
            }
        }

        /// <summary>While true, Speak() is silently skipped - the Test button ignores this entirely (see TestCommand).</summary>
        public bool Muted
        {
            get => _muted;
            set
            {
                if (SetProperty(ref _muted, value))
                {
                    _settings.SetBool(MutedSettingKey, value);
                }
            }
        }

        /// <summary>Speaks an automated announcement (Auto Pilot's plot/refuel countdowns) - a no-op while Muted, or for blank text.</summary>
        public void Speak(string text)
        {
            if (_muted || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            _engine.SpeakAsync(text);
        }

        public void Dispose() => _engine.Dispose();
    }
}
