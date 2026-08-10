using RouteJumper.Services;

namespace RouteJumper.Tests.TestSupport
{
    /// <summary>
    /// Test double for ISpeechEngine: records every call instead of driving a real SAPI voice,
    /// so SpeechAnnouncer's own voice/volume/mute selection and persistence logic can be tested
    /// without an installed voice or real audio output.
    /// </summary>
    internal sealed class FakeSpeechEngine : ISpeechEngine
    {
        public List<string> SpokenTexts { get; } = new();

        public List<string?> VoiceSelections { get; } = new();

        public List<int> VolumeSelections { get; } = new();

        public IReadOnlyList<string> InstalledVoiceNames { get; set; } = new[] { "Voice A", "Voice B" };

        public bool Disposed { get; private set; }

        public IReadOnlyList<string> GetInstalledVoiceNames() => InstalledVoiceNames;

        public void SetVoice(string? voiceName) => VoiceSelections.Add(voiceName);

        public void SetVolume(int volume) => VolumeSelections.Add(volume);

        public void SpeakAsync(string text) => SpokenTexts.Add(text);

        public void Dispose() => Disposed = true;
    }
}
