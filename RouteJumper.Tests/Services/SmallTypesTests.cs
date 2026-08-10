using RouteJumper.Services;
using Xunit;

namespace RouteJumper.Tests.Services
{
    public class PlaybackAbortedExceptionTests
    {
        [Fact]
        public void Constructor_SetsMessage()
        {
            var ex = new PlaybackAbortedException("lost focus");
            Assert.Equal("lost focus", ex.Message);
        }

        [Fact]
        public void IsAnException()
        {
            Assert.IsAssignableFrom<Exception>(new PlaybackAbortedException("x"));
        }
    }

    public class MacroInstructionEqualityTests
    {
        [Fact]
        public void Tap_ValueEquality_ComparesToken()
        {
            Assert.Equal(new MacroInstruction.Tap("UP"), new MacroInstruction.Tap("UP"));
            Assert.NotEqual(new MacroInstruction.Tap("UP"), new MacroInstruction.Tap("DOWN"));
        }

        [Fact]
        public void Hold_ValueEquality_ComparesTokenAndDuration()
        {
            Assert.Equal(new MacroInstruction.Hold("UP", 100), new MacroInstruction.Hold("UP", 100));
            Assert.NotEqual(new MacroInstruction.Hold("UP", 100), new MacroInstruction.Hold("UP", 200));
        }

        [Fact]
        public void DifferentInstructionKinds_AreNeverEqual()
        {
            MacroInstruction tap = new MacroInstruction.Tap("UP");
            MacroInstruction call = new MacroInstruction.Call("UP");

            Assert.NotEqual(tap, call);
        }
    }
}
