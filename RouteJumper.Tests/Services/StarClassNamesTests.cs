using RouteJumper.Services;
using Xunit;

namespace RouteJumper.Tests.Services
{
    public class StarClassNamesTests
    {
        [Theory]
        [InlineData("O", "O (Blue)")]
        [InlineData("B", "B (Blue-White)")]
        [InlineData("A", "A (Blue-White)")]
        [InlineData("F", "F (White)")]
        [InlineData("G", "G (White-Yellow)")]
        [InlineData("K", "K (Yellow-Orange)")]
        [InlineData("M", "M (Red dwarf)")]
        [InlineData("L", "L (Brown dwarf)")]
        [InlineData("T", "T (Brown dwarf)")]
        [InlineData("Y", "Y (Brown dwarf)")]
        [InlineData("N", "Neutron")]
        [InlineData("H", "Black Hole")]
        [InlineData("SupermassiveBlackHole", "Supermassive Black Hole")]
        [InlineData("TTS", "T Tauri")]
        [InlineData("AeBe", "Herbig Ae/Be")]
        [InlineData("W", "Wolf-Rayet")]
        [InlineData("WN", "Wolf-Rayet N")]
        [InlineData("WNC", "Wolf-Rayet NC")]
        [InlineData("WC", "Wolf-Rayet C")]
        [InlineData("WO", "Wolf-Rayet O")]
        [InlineData("C", "C")]
        [InlineData("CS", "CS")]
        [InlineData("CN", "CN")]
        [InlineData("CJ", "CJ")]
        [InlineData("CH", "CH")]
        [InlineData("CHd", "CHd")]
        [InlineData("MS", "MS")]
        [InlineData("S", "S")]
        public void ToDisplayName_KnownClass_ReturnsEdsmStyleNameWithoutTheRedundantStarWord(string starClass, string expected)
        {
            Assert.Equal(expected, StarClassNames.ToDisplayName(starClass));
        }

        [Theory]
        [InlineData("D", "White Dwarf (D)")]
        [InlineData("DA", "White Dwarf (DA)")]
        [InlineData("DQ", "White Dwarf (DQ)")]
        public void ToDisplayName_WhiteDwarfSubclass_FollowsGenericPattern(string starClass, string expected)
        {
            Assert.Equal(expected, StarClassNames.ToDisplayName(starClass));
        }

        [Fact]
        public void ToDisplayName_UnknownExoticClass_FallsBackToRawCode()
        {
            // K_OrangeGiant is one of the underscore-suffixed giant/supergiant codes some journal
            // documentation lists but this app doesn't (yet) have confirmed EDSM-format evidence
            // for - deliberately left unmapped rather than guessing.
            Assert.Equal("K_OrangeGiant", StarClassNames.ToDisplayName("K_OrangeGiant"));
        }

        [Fact]
        public void ToDisplayName_IsCaseInsensitive()
        {
            Assert.Equal("K (Yellow-Orange)", StarClassNames.ToDisplayName("k"));
        }

        [Theory]
        [InlineData("K (Yellow-Orange)", "K")]
        [InlineData("L (Brown dwarf)", "L")]
        [InlineData("Y (Brown dwarf)", "Y")]
        [InlineData("Neutron", "N")]
        [InlineData("Black Hole", "H")]
        [InlineData("T Tauri", "TTS")]
        [InlineData("Herbig Ae/Be", "AeBe")]
        [InlineData("Wolf-Rayet C", "WC")]
        [InlineData("CN", "CN")]
        public void TryGetCode_KnownDisplayName_ReturnsTheCanonicalCode(string displayName, string expectedCode)
        {
            Assert.True(StarClassNames.TryGetCode(displayName, out var code));
            Assert.Equal(expectedCode, code);
        }

        [Theory]
        [InlineData("White Dwarf (DA)", "DA")]
        [InlineData("White Dwarf (DAV)", "DAV")]
        [InlineData("White Dwarf (D)", "D")]
        public void TryGetCode_WhiteDwarfPattern_ExtractsTheInnerCode(string displayName, string expectedCode)
        {
            Assert.True(StarClassNames.TryGetCode(displayName, out var code));
            Assert.Equal(expectedCode, code);
        }

        [Fact]
        public void TryGetCode_UnrecognizedText_ReturnsFalse()
        {
            Assert.False(StarClassNames.TryGetCode("Some Never-Before-Seen Description", out _));
        }

        [Fact]
        public void ToDisplayName_ThenTryGetCode_RoundTripsForEveryKnownCode()
        {
            // Every code this app formats must be reversible from its own formatted text -
            // otherwise an EDSM-resolved result for that same star could never converge with a
            // journal-seeded one.
            string[] codes = { "O", "B", "A", "F", "G", "K", "M", "L", "T", "Y", "N", "H", "SupermassiveBlackHole",
                "TTS", "AeBe", "W", "WN", "WNC", "WC", "WO", "C", "CS", "CN", "CJ", "CH", "CHd", "MS", "S" };

            foreach (var code in codes)
            {
                var display = StarClassNames.ToDisplayName(code);
                Assert.True(StarClassNames.TryGetCode(display, out var roundTripped), $"TryGetCode couldn't recover a code from \"{display}\" (originally \"{code}\").");
                Assert.Equal(code, roundTripped, ignoreCase: true);
            }
        }
    }
}
