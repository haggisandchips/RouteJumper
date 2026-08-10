using RouteJumper.Services;
using Xunit;

namespace RouteJumper.Tests.Services
{
    /// <summary>
    /// MacroPlayer.Flatten is the one piece of MacroPlayer that's pure logic (parse + unroll,
    /// no Win32/SendInput involved) - the rest of the class (PlayAsync, RunSingleStepAsync, and
    /// everything downstream of ExecuteLeafAsync) drives real keyboard/mouse input against a real
    /// HWND and isn't exercised here.
    /// </summary>
    public class MacroPlayerFlattenTests
    {
        [Fact]
        public void Flatten_SimpleScript_ReturnsStepsInOrder()
        {
            var flattened = MacroPlayer.Flatten("UP\nDOWN");

            Assert.Equal(2, flattened.Count);
            Assert.Equal("UP", Assert.IsType<MacroInstruction.Tap>(flattened[0]).Token);
            Assert.Equal("DOWN", Assert.IsType<MacroInstruction.Tap>(flattened[1]).Token);
        }

        [Fact]
        public void Flatten_Repeat_UnrollsBodyCountTimes()
        {
            var flattened = MacroPlayer.Flatten("REPEAT 3\n    UP\nEND");

            Assert.Equal(3, flattened.Count);
            Assert.All(flattened, step => Assert.Equal("UP", Assert.IsType<MacroInstruction.Tap>(step).Token));
        }

        [Fact]
        public void Flatten_NestedRepeat_UnrollsBothLevels()
        {
            var flattened = MacroPlayer.Flatten("REPEAT 2\n  REPEAT 3\n    UP\n  END\nEND");

            Assert.Equal(6, flattened.Count);
        }

        [Fact]
        public void Flatten_Call_InlinesNamedMacro()
        {
            var script = "MACRO refuel\n    RIGHT_PANEL\n    SELECT\nEND\nCALL refuel";
            var flattened = MacroPlayer.Flatten(script);

            Assert.Equal(2, flattened.Count);
            Assert.Equal("RIGHT_PANEL", Assert.IsType<MacroInstruction.Tap>(flattened[0]).Token);
            Assert.Equal("SELECT", Assert.IsType<MacroInstruction.Tap>(flattened[1]).Token);
        }

        [Fact]
        public void Flatten_CallToUnknownMacro_ContributesNothing()
        {
            var flattened = MacroPlayer.Flatten("UP\nCALL doesNotExist\nDOWN");

            Assert.Equal(2, flattened.Count);
        }

        [Fact]
        public void Flatten_CallInsideRepeat_InlinesOncePerIteration()
        {
            var script = "MACRO tap\n    UP\nEND\nREPEAT 2\n    CALL tap\nEND";
            var flattened = MacroPlayer.Flatten(script);

            Assert.Equal(2, flattened.Count);
        }

        [Fact]
        public void Flatten_WaitSteps_AreDroppedEntirely()
        {
            var flattened = MacroPlayer.Flatten("UP\nWAIT 500\nDOWN");

            Assert.Equal(2, flattened.Count);
            Assert.DoesNotContain(flattened, s => s is MacroInstruction.Wait);
        }

        [Fact]
        public void Flatten_WaitInsideRepeat_IsAlsoDropped()
        {
            var flattened = MacroPlayer.Flatten("REPEAT 2\n    UP\n    WAIT 100\nEND");

            Assert.Equal(2, flattened.Count);
        }

        [Fact]
        public void Flatten_EmptyScript_ReturnsEmpty()
        {
            Assert.Empty(MacroPlayer.Flatten(string.Empty));
        }

        [Fact]
        public void Flatten_ClickAndPaste_ArePreservedAsLeaves()
        {
            var flattened = MacroPlayer.Flatten("CLICK 10,20\nPASTE {NEXT_SYSTEM}");

            Assert.IsType<MacroInstruction.Click>(flattened[0]);
            Assert.IsType<MacroInstruction.Paste>(flattened[1]);
        }
    }
}
