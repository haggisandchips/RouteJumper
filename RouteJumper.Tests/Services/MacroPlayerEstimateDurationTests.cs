using RouteJumper.Services;
using Xunit;

namespace RouteJumper.Tests.Services
{
    /// <summary>
    /// MacroPlayer.EstimateDurationMs is a pure mirror of RunAsync/ExecuteLeafAsync's own timing
    /// (SPEC §6.4's TRITIUM_LOOPS capping depends on it) - no Win32/SendInput involved, so these
    /// assert on relative deltas (e.g. "adding AutoWaitMs adds exactly AutoWaitMs once per leaf")
    /// rather than hardcoding MacroPlayer's own private timing constants, which stays correct even
    /// if those constants are ever tuned.
    /// </summary>
    public class MacroPlayerEstimateDurationTests
    {
        [Fact]
        public void EstimateDurationMs_EmptyScript_IsZero()
        {
            Assert.Equal(0, MacroPlayer.EstimateDurationMs(string.Empty, autoWaitMs: 0));
        }

        [Fact]
        public void EstimateDurationMs_SingleTap_IsPositiveEvenWithNoAutoWait()
        {
            Assert.True(MacroPlayer.EstimateDurationMs("UP", autoWaitMs: 0) > 0);
        }

        [Fact]
        public void EstimateDurationMs_SingleLeaf_AutoWaitAddsExactlyOnce()
        {
            var withoutAutoWait = MacroPlayer.EstimateDurationMs("UP", autoWaitMs: 0);
            var withAutoWait = MacroPlayer.EstimateDurationMs("UP", autoWaitMs: 100);

            // A trailing AutoWait still applies even as the very last (only) instruction - there's
            // no "next" instruction to check, so the "unless next is WAIT" exception can't apply.
            Assert.Equal(withoutAutoWait + 100, withAutoWait);
        }

        [Fact]
        public void EstimateDurationMs_TwoLeaves_AutoWaitAddsOncePerLeaf()
        {
            var withoutAutoWait = MacroPlayer.EstimateDurationMs("UP\nDOWN", autoWaitMs: 0);
            var withAutoWait = MacroPlayer.EstimateDurationMs("UP\nDOWN", autoWaitMs: 100);

            Assert.Equal(withoutAutoWait + 200, withAutoWait);
        }

        [Fact]
        public void EstimateDurationMs_LeafBeforeWait_SkipsItsOwnTrailingAutoWait()
        {
            // Same rule RunAsync itself applies: the WAIT's own duration already provides the
            // pause, so the preceding leaf's trailing AutoWait would just be redundant.
            var withoutAutoWait = MacroPlayer.EstimateDurationMs("UP\nWAIT 500", autoWaitMs: 0);
            var withAutoWait = MacroPlayer.EstimateDurationMs("UP\nWAIT 500", autoWaitMs: 100);

            // Only the WAIT instruction itself gets a trailing AutoWait (it's the last step) -
            // the UP before it does not, since its "next" is a WAIT.
            Assert.Equal(withoutAutoWait + 100, withAutoWait);
        }

        [Fact]
        public void EstimateDurationMs_WaitContributesItsOwnFullDuration()
        {
            var without = MacroPlayer.EstimateDurationMs("UP", autoWaitMs: 0);
            var withWait = MacroPlayer.EstimateDurationMs("UP\nWAIT 1234", autoWaitMs: 0);

            Assert.Equal(without + 1234, withWait);
        }

        [Fact]
        public void EstimateDurationMs_Repeat_MultipliesBodyDurationByCount()
        {
            var oneIteration = MacroPlayer.EstimateDurationMs("UP", autoWaitMs: 50);
            var threeIterations = MacroPlayer.EstimateDurationMs("REPEAT 3\n    UP\nEND", autoWaitMs: 50);

            // Each iteration is its own self-contained steps list (no "next" reaching across
            // iteration boundaries), so a single-step body gets its own trailing AutoWait every
            // time round, same as playing it stand-alone that many times.
            Assert.Equal(oneIteration * 3, threeIterations);
        }

        [Fact]
        public void EstimateDurationMs_Call_InlinesTheReferencedMacrosDuration()
        {
            var direct = MacroPlayer.EstimateDurationMs("RIGHT_PANEL\nSELECT", autoWaitMs: 20);
            var viaCall = MacroPlayer.EstimateDurationMs(
                "MACRO refuel\n    RIGHT_PANEL\n    SELECT\nEND\nCALL refuel", autoWaitMs: 20);

            Assert.Equal(direct, viaCall);
        }

        [Fact]
        public void EstimateDurationMs_CallToUnknownMacro_ContributesNothing()
        {
            var withoutCall = MacroPlayer.EstimateDurationMs("UP", autoWaitMs: 0);
            var withUnresolvedCall = MacroPlayer.EstimateDurationMs("UP\nCALL doesNotExist", autoWaitMs: 0);

            Assert.Equal(withoutCall, withUnresolvedCall);
        }

        [Fact]
        public void EstimateDurationMs_HoldAndHoldClick_UseTheirOwnExplicitDuration()
        {
            var hold = MacroPlayer.EstimateDurationMs("HOLD RIGHT 800", autoWaitMs: 0);
            var holdClick = MacroPlayer.EstimateDurationMs("HOLD CLICK 10,20 600", autoWaitMs: 0);

            Assert.Equal(800, hold);
            Assert.Equal(600, holdClick);
        }

        [Fact]
        public void EstimateDurationMs_Click_IsInstantaneousOnItsOwn()
        {
            Assert.Equal(0, MacroPlayer.EstimateDurationMs("CLICK 10,20", autoWaitMs: 0));
        }
    }
}
