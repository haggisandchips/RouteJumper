using RouteJumper.Models;
using Xunit;

namespace RouteJumper.Tests.Models
{
    public class ControlActionExtensionsTests
    {
        [Theory]
        [InlineData(ControlAction.Up, "UP")]
        [InlineData(ControlAction.Down, "DOWN")]
        [InlineData(ControlAction.Left, "LEFT")]
        [InlineData(ControlAction.Right, "RIGHT")]
        [InlineData(ControlAction.Select, "SELECT")]
        [InlineData(ControlAction.PrevPanel, "PREV_PANEL")]
        [InlineData(ControlAction.NextPanel, "NEXT_PANEL")]
        [InlineData(ControlAction.Exit, "EXIT")]
        [InlineData(ControlAction.RightPanel, "RIGHT_PANEL")]
        public void ToActionName_MatchesSpecVocabulary(ControlAction action, string expected)
        {
            Assert.Equal(expected, action.ToActionName());
        }

        [Theory]
        [InlineData(ControlAction.Up)]
        [InlineData(ControlAction.Down)]
        [InlineData(ControlAction.Left)]
        [InlineData(ControlAction.Right)]
        [InlineData(ControlAction.Select)]
        [InlineData(ControlAction.PrevPanel)]
        [InlineData(ControlAction.NextPanel)]
        [InlineData(ControlAction.Exit)]
        [InlineData(ControlAction.RightPanel)]
        public void ToActionName_And_TryParseActionName_RoundTrip(ControlAction action)
        {
            var name = action.ToActionName();

            var parsed = ControlActionExtensions.TryParseActionName(name, out var result);

            Assert.True(parsed);
            Assert.Equal(action, result);
        }

        [Fact]
        public void TryParseActionName_UnknownName_ReturnsFalse()
        {
            var parsed = ControlActionExtensions.TryParseActionName("NOT_A_REAL_ACTION", out var result);

            Assert.False(parsed);
            Assert.Equal(default, result);
        }

        [Fact]
        public void TryParseActionName_IsCaseSensitive()
        {
            var parsed = ControlActionExtensions.TryParseActionName("up", out _);
            Assert.False(parsed);
        }
    }
}
