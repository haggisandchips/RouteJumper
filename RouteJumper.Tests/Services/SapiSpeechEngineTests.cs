using RouteJumper.Services;
using Xunit;

namespace RouteJumper.Tests.Services
{
    public class SapiSpeechEngineTests
    {
        [Fact]
        public void BuildFriendlyVoiceNameMap_StripsMicrosoftPrefix()
        {
            var map = SapiSpeechEngine.BuildFriendlyVoiceNameMap(new[] { "Microsoft David" });

            Assert.Equal("Microsoft David", map["David"]);
        }

        [Fact]
        public void BuildFriendlyVoiceNameMap_ExcludesDesktopDuplicates()
        {
            var map = SapiSpeechEngine.BuildFriendlyVoiceNameMap(new[] { "Microsoft David", "Microsoft David Desktop" });

            Assert.Single(map);
            Assert.Equal("Microsoft David", map["David"]);
        }

        [Fact]
        public void BuildFriendlyVoiceNameMap_LeavesNonMicrosoftNamesUnchanged()
        {
            var map = SapiSpeechEngine.BuildFriendlyVoiceNameMap(new[] { "Some Other Voice" });

            Assert.Equal("Some Other Voice", map["Some Other Voice"]);
        }

        [Fact]
        public void BuildFriendlyVoiceNameMap_KeepsMultipleDistinctVoices()
        {
            var map = SapiSpeechEngine.BuildFriendlyVoiceNameMap(new[]
            {
                "Microsoft David Desktop",
                "Microsoft Hazel Desktop",
                "Microsoft Zira Desktop",
                "Microsoft David",
                "Microsoft Hazel",
                "Microsoft Susan",
                "Microsoft George",
                "Microsoft Mark",
                "Microsoft Zira"
            });

            Assert.Equal(
                new Dictionary<string, string>
                {
                    ["David"] = "Microsoft David",
                    ["Hazel"] = "Microsoft Hazel",
                    ["Susan"] = "Microsoft Susan",
                    ["George"] = "Microsoft George",
                    ["Mark"] = "Microsoft Mark",
                    ["Zira"] = "Microsoft Zira"
                },
                map);
        }
    }
}
