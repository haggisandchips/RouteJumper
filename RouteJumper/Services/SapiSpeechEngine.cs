using System.Speech.Synthesis;

namespace RouteJumper.Services
{
    /// <summary>
    /// Thin wrapper over Windows' built-in SAPI speech synthesizer (System.Speech) - the actual
    /// OS integration point for SpeechAnnouncer's announcements. Kept separate from
    /// SpeechAnnouncer's own voice/volume/mute state and persistence (see ISpeechEngine) so that
    /// logic can be unit tested without a real speech engine.
    /// </summary>
    public sealed class SapiSpeechEngine : ISpeechEngine
    {
        private readonly SpeechSynthesizer _synthesizer = new();
        private readonly string? _defaultVoiceName;

        /// <summary>
        /// Friendly display name -> the real SAPI voice name SelectVoice actually needs. Windows'
        /// stock voices are installed in pairs like "Microsoft David" and "Microsoft David
        /// Desktop" - the same voice twice under a near-identical name - so the "Desktop" one is
        /// dropped entirely rather than offered as a confusing duplicate choice, and the
        /// "Microsoft " prefix (present on every one of them) is stripped for display, since it
        /// adds nothing a user needs to choose between voices by.
        /// </summary>
        private readonly Dictionary<string, string> _friendlyToRealVoiceName;

        public SapiSpeechEngine()
        {
            _synthesizer.SetOutputToDefaultAudioDevice();
            _friendlyToRealVoiceName = BuildFriendlyVoiceNameMap(
                _synthesizer.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo.Name));

            try
            {
                _defaultVoiceName = _synthesizer.Voice.Name;
            }
            catch (InvalidOperationException)
            {
                // No voice installed at all - GetInstalledVoiceNames() will report an empty list
                // and every SetVoice/SpeakAsync call below degrades to a silent no-op via its own
                // try/catch rather than throwing.
                _defaultVoiceName = null;
            }
        }

        /// <summary>
        /// Pure string transform, pulled out of the constructor above so it's testable without a
        /// real SpeechSynthesizer (VoiceInfo has no public constructor to fake one with).
        /// </summary>
        internal static Dictionary<string, string> BuildFriendlyVoiceNameMap(IEnumerable<string> realVoiceNames)
        {
            var map = new Dictionary<string, string>();
            foreach (var realName in realVoiceNames)
            {
                if (realName.EndsWith(" Desktop", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var friendlyName = realName.StartsWith("Microsoft ", StringComparison.OrdinalIgnoreCase)
                    ? realName["Microsoft ".Length..]
                    : realName;

                map[friendlyName] = realName;
            }

            return map;
        }

        public IReadOnlyList<string> GetInstalledVoiceNames() => _friendlyToRealVoiceName.Keys.ToList();

        public void SetVoice(string? voiceName)
        {
            var target = voiceName is { Length: > 0 } && _friendlyToRealVoiceName.TryGetValue(voiceName, out var realName)
                ? realName
                : _defaultVoiceName;
            if (target is null)
            {
                return;
            }

            try
            {
                _synthesizer.SelectVoice(target);
            }
            catch (ArgumentException)
            {
                // The requested (or previously-persisted) voice doesn't exist on this machine -
                // e.g. settings carried over from another PC, or the voice was uninstalled since.
                // Leave whichever voice is already selected rather than throwing.
            }
        }

        public void SetVolume(int volume) => _synthesizer.Volume = Math.Clamp(volume, 0, 100);

        public void SpeakAsync(string text)
        {
            try
            {
                _synthesizer.SpeakAsyncCancelAll();
                _synthesizer.SpeakAsync(text);
            }
            catch (InvalidOperationException)
            {
                // No usable voice/output device - nothing meaningful to do.
            }
        }

        public void Dispose() => _synthesizer.Dispose();
    }
}
