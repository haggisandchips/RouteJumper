using RouteJumper.Services;
using Xunit;

namespace RouteJumper.Tests.Services
{
    public class StarClassNamesTests
    {
        [Theory]
        [InlineData("O", "O (Blue) Star")]
        [InlineData("B", "B (Blue-White) Star")]
        [InlineData("A", "A (Blue-White) Star")]
        [InlineData("F", "F (White) Star")]
        [InlineData("G", "G (White-Yellow) Star")]
        [InlineData("K", "K (Yellow-Orange) Star")]
        [InlineData("M", "M (Red dwarf) Star")]
        [InlineData("N", "Neutron Star")]
        [InlineData("H", "Black Hole")]
        [InlineData("SupermassiveBlackHole", "Supermassive Black Hole")]
        public void ToDisplayName_KnownClass_ReturnsEdsmStyleName(string starClass, string expected)
        {
            Assert.Equal(expected, StarClassNames.ToDisplayName(starClass));
        }

        [Theory]
        [InlineData("D", "White Dwarf (D) Star")]
        [InlineData("DA", "White Dwarf (DA) Star")]
        [InlineData("DQ", "White Dwarf (DQ) Star")]
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
            Assert.Equal("K (Yellow-Orange) Star", StarClassNames.ToDisplayName("k"));
        }
    }
}
