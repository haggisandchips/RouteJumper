namespace RouteJumper.Services
{
    /// <summary>
    /// Abstraction over the real OS speech engine (see <see cref="SapiSpeechEngine"/>) so
    /// SpeechAnnouncer's own voice/volume/mute selection and persistence logic can be unit
    /// tested against a fake, rather than a real speech engine that needs an installed voice and
    /// produces real audio.
    /// </summary>
    public interface ISpeechEngine : IDisposable
    {
        /// <summary>Every voice installed on this machine, by name - for the Preferences dialog's voice picker.</summary>
        IReadOnlyList<string> GetInstalledVoiceNames();

        /// <summary>Selects a voice by name; null/empty (or an unrecognized name) falls back to whatever this engine considers its default.</summary>
        void SetVoice(string? voiceName);

        /// <summary>0-100.</summary>
        void SetVolume(int volume);

        /// <summary>Speaks text asynchronously, cancelling whatever this engine was already speaking first - an announcement should never queue up behind a stale one.</summary>
        void SpeakAsync(string text);
    }
}
