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
        [InlineData("N", "Neutron")]
        [InlineData("H", "Black Hole")]
        [InlineData("SupermassiveBlackHole", "Supermassive Black Hole")]
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
            Assert.Equal("WC", StarClassNames.ToDisplayName("WC"));
        }

        [Fact]
        public void ToDisplayName_IsCaseInsensitive()
        {
            Assert.Equal("K (Yellow-Orange)", StarClassNames.ToDisplayName("k"));
        }
    }
}
