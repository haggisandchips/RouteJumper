using RouteJumper.Services;
using Xunit;

namespace RouteJumper.Tests.Services
{
    public class MacroScriptParserTests
    {
        [Fact]
        public void Parse_ActionName_ProducesTap()
        {
            var parsed = MacroScriptParser.Parse("UP");

            var tap = Assert.IsType<MacroInstruction.Tap>(Assert.Single(parsed.MainSteps));
            Assert.Equal("UP", tap.Token);
        }

        [Fact]
        public void Parse_KeyToken_ProducesTapWithFullToken()
        {
            var parsed = MacroScriptParser.Parse("KEY Control+A");

            var tap = Assert.IsType<MacroInstruction.Tap>(Assert.Single(parsed.MainSteps));
            Assert.Equal("KEY Control+A", tap.Token);
        }

        [Fact]
        public void Parse_Hold_ProducesHoldWithTokenAndDuration()
        {
            var parsed = MacroScriptParser.Parse("HOLD RIGHT 800");

            var hold = Assert.IsType<MacroInstruction.Hold>(Assert.Single(parsed.MainSteps));
            Assert.Equal("RIGHT", hold.Token);
            Assert.Equal(800, hold.DurationMs);
        }

        [Fact]
        public void Parse_HoldWithKeyToken_KeepsFullTokenTogether()
        {
            var parsed = MacroScriptParser.Parse("HOLD KEY Control+A 500");

            var hold = Assert.IsType<MacroInstruction.Hold>(Assert.Single(parsed.MainSteps));
            Assert.Equal("KEY Control+A", hold.Token);
            Assert.Equal(500, hold.DurationMs);
        }

        [Fact]
        public void Parse_Hold_ZeroOrNegativeDuration_IsSkipped()
        {
            var parsed = MacroScriptParser.Parse("HOLD RIGHT 0\nHOLD RIGHT -5");

            Assert.Empty(parsed.MainSteps);
        }

        [Fact]
        public void Parse_Click_ProducesClickWithCoordinates()
        {
            var parsed = MacroScriptParser.Parse("CLICK 240,160");

            var click = Assert.IsType<MacroInstruction.Click>(Assert.Single(parsed.MainSteps));
            Assert.Equal("240", click.X);
            Assert.Equal("160", click.Y);
        }

        [Fact]
        public void Parse_ClickWithCentrePlaceholder_KeepsLiteralPlaceholder()
        {
            var parsed = MacroScriptParser.Parse("CLICK {CENTRE},{CENTRE}");

            var click = Assert.IsType<MacroInstruction.Click>(Assert.Single(parsed.MainSteps));
            Assert.Equal("{CENTRE}", click.X);
            Assert.Equal("{CENTRE}", click.Y);
        }

        [Fact]
        public void Parse_ClickPlaceholderIsCaseInsensitive()
        {
            var parsed = MacroScriptParser.Parse("CLICK {centre},100");

            var click = Assert.IsType<MacroInstruction.Click>(Assert.Single(parsed.MainSteps));
            Assert.Equal("{centre}", click.X);
        }

        [Theory]
        [InlineData("CLICK 100")]
        [InlineData("CLICK 100,200,300")]
        [InlineData("CLICK abc,100")]
        [InlineData("CLICK {NOTCENTRE},100")]
        public void Parse_MalformedClick_IsSkipped(string line)
        {
            var parsed = MacroScriptParser.Parse(line);
            Assert.Empty(parsed.MainSteps);
        }

        [Fact]
        public void Parse_HoldClick_ProducesHoldClickWithCoordinatesAndDuration()
        {
            var parsed = MacroScriptParser.Parse("HOLD CLICK 240,160 600");

            var holdClick = Assert.IsType<MacroInstruction.HoldClick>(Assert.Single(parsed.MainSteps));
            Assert.Equal("240", holdClick.X);
            Assert.Equal("160", holdClick.Y);
            Assert.Equal(600, holdClick.DurationMs);
        }

        [Fact]
        public void Parse_HoldClick_ZeroDuration_IsSkipped()
        {
            var parsed = MacroScriptParser.Parse("HOLD CLICK 240,160 0");
            Assert.Empty(parsed.MainSteps);
        }

        [Fact]
        public void Parse_Wait_ProducesWaitWithDuration()
        {
            var parsed = MacroScriptParser.Parse("WAIT 500");

            var wait = Assert.IsType<MacroInstruction.Wait>(Assert.Single(parsed.MainSteps));
            Assert.Equal(500, wait.DurationMs);
        }

        [Fact]
        public void Parse_Paste_ProducesPasteWithLiteralText()
        {
            var parsed = MacroScriptParser.Parse("PASTE Hello world");

            var paste = Assert.IsType<MacroInstruction.Paste>(Assert.Single(parsed.MainSteps));
            Assert.Equal("Hello world", paste.Text);
        }

        [Fact]
        public void Parse_PasteWithNextSystemPlaceholder_KeepsLiteralPlaceholder()
        {
            var parsed = MacroScriptParser.Parse("PASTE {NEXT_SYSTEM}");

            var paste = Assert.IsType<MacroInstruction.Paste>(Assert.Single(parsed.MainSteps));
            Assert.Equal("{NEXT_SYSTEM}", paste.Text);
        }

        [Fact]
        public void Parse_Repeat_ProducesRepeatWithNestedBody()
        {
            var parsed = MacroScriptParser.Parse("REPEAT 3\n    UP\n    WAIT 200\nEND");

            var repeat = Assert.IsType<MacroInstruction.Repeat>(Assert.Single(parsed.MainSteps));
            Assert.Equal(3, repeat.Count);
            Assert.Equal(2, repeat.Body.Count);
            Assert.IsType<MacroInstruction.Tap>(repeat.Body[0]);
            Assert.IsType<MacroInstruction.Wait>(repeat.Body[1]);
        }

        [Fact]
        public void Parse_NestedRepeat_Works()
        {
            var script = "REPEAT 2\n  REPEAT 3\n    UP\n  END\nEND";
            var parsed = MacroScriptParser.Parse(script);

            var outer = Assert.IsType<MacroInstruction.Repeat>(Assert.Single(parsed.MainSteps));
            Assert.Equal(2, outer.Count);
            var inner = Assert.IsType<MacroInstruction.Repeat>(Assert.Single(outer.Body));
            Assert.Equal(3, inner.Count);
            Assert.IsType<MacroInstruction.Tap>(Assert.Single(inner.Body));
        }

        [Theory]
        [InlineData("REPEAT 0")]
        [InlineData("REPEAT -1")]
        [InlineData("REPEAT abc")]
        public void Parse_RepeatWithInvalidCount_ProducesNoStep(string line)
        {
            var parsed = MacroScriptParser.Parse(line + "\nUP\nEND");

            // The invalid REPEAT itself contributes nothing, but its body isn't opened as a
            // frame either, so the subsequent UP is a top-level step, and the stray END is
            // ignored (frames.Count == 1 already).
            var tap = Assert.IsType<MacroInstruction.Tap>(Assert.Single(parsed.MainSteps));
            Assert.Equal("UP", tap.Token);
        }

        [Fact]
        public void Parse_Macro_IsNotAddedToMainSteps()
        {
            var parsed = MacroScriptParser.Parse("MACRO refuel\n    RIGHT_PANEL\n    SELECT\nEND");

            Assert.Empty(parsed.MainSteps);
            Assert.True(parsed.Macros.ContainsKey("refuel"));
            Assert.Equal(2, parsed.Macros["refuel"].Count);
        }

        [Fact]
        public void Parse_MacroLookupIsCaseInsensitive()
        {
            var parsed = MacroScriptParser.Parse("MACRO Refuel\n    UP\nEND");

            Assert.True(parsed.Macros.ContainsKey("refuel"));
            Assert.True(parsed.Macros.ContainsKey("REFUEL"));
        }

        [Fact]
        public void Parse_MacroNestedInsideRepeat_IsNotAllowed()
        {
            var parsed = MacroScriptParser.Parse("REPEAT 2\n    MACRO nested\n        UP\n    END\nEND");

            // MACRO is only valid at the top level - inside a REPEAT it's simply ignored.
            Assert.Empty(parsed.Macros);
        }

        [Fact]
        public void Parse_Call_ProducesCallWithMacroName()
        {
            var parsed = MacroScriptParser.Parse("CALL refuel");

            var call = Assert.IsType<MacroInstruction.Call>(Assert.Single(parsed.MainSteps));
            Assert.Equal("refuel", call.MacroName);
        }

        [Fact]
        public void Parse_CallInsideRepeat_IsNested()
        {
            var parsed = MacroScriptParser.Parse("REPEAT 2\n    CALL refuel\nEND");

            var repeat = Assert.IsType<MacroInstruction.Repeat>(Assert.Single(parsed.MainSteps));
            var call = Assert.IsType<MacroInstruction.Call>(Assert.Single(repeat.Body));
            Assert.Equal("refuel", call.MacroName);
        }

        [Fact]
        public void Parse_BlankLinesAndComments_AreIgnored()
        {
            var parsed = MacroScriptParser.Parse("# a comment\n\nUP\n   # indented comment\n");

            var tap = Assert.IsType<MacroInstruction.Tap>(Assert.Single(parsed.MainSteps));
            Assert.Equal("UP", tap.Token);
        }

        [Fact]
        public void Parse_SemicolonSeparatedSegmentsOnOneLine_ProduceMultipleSteps()
        {
            var parsed = MacroScriptParser.Parse("UP; WAIT 200; DOWN");

            Assert.Equal(3, parsed.MainSteps.Count);
            Assert.IsType<MacroInstruction.Tap>(parsed.MainSteps[0]);
            Assert.IsType<MacroInstruction.Wait>(parsed.MainSteps[1]);
            Assert.IsType<MacroInstruction.Tap>(parsed.MainSteps[2]);
        }

        [Fact]
        public void Parse_CrLfLineEndings_AreNormalized()
        {
            var parsed = MacroScriptParser.Parse("UP\r\nDOWN\r\n");
            Assert.Equal(2, parsed.MainSteps.Count);
        }

        [Fact]
        public void Parse_UnmatchedEnd_IsIgnoredAtTopLevel()
        {
            var parsed = MacroScriptParser.Parse("END\nUP");

            var tap = Assert.IsType<MacroInstruction.Tap>(Assert.Single(parsed.MainSteps));
            Assert.Equal("UP", tap.Token);
        }

        [Fact]
        public void Parse_EmptyScript_ProducesNoSteps()
        {
            var parsed = MacroScriptParser.Parse(string.Empty);
            Assert.Empty(parsed.MainSteps);
            Assert.Empty(parsed.Macros);
        }

        [Fact]
        public void Parse_UnclosedMacro_LeavesSubsequentLinesInsideIt()
        {
            var parsed = MacroScriptParser.Parse("MACRO refuel\n    UP\n    DOWN");

            Assert.Empty(parsed.MainSteps);
            Assert.Equal(2, parsed.Macros["refuel"].Count);
        }
    }

    public class MacroScriptSerializerTests
    {
        [Fact]
        public void ToScriptText_Tap_WritesRawToken()
        {
            var text = MacroScriptSerializer.ToScriptText(new MacroInstruction[] { new MacroInstruction.Tap("UP") });
            Assert.Equal("UP" + Environment.NewLine, text);
        }

        [Fact]
        public void ToScriptText_Hold_WritesHoldLine()
        {
            var text = MacroScriptSerializer.ToScriptText(new MacroInstruction[] { new MacroInstruction.Hold("RIGHT", 800) });
            Assert.Equal("HOLD RIGHT 800" + Environment.NewLine, text);
        }

        [Fact]
        public void ToScriptText_Click_WritesClickLine()
        {
            var text = MacroScriptSerializer.ToScriptText(new MacroInstruction[] { new MacroInstruction.Click("240", "160") });
            Assert.Equal("CLICK 240,160" + Environment.NewLine, text);
        }

        [Fact]
        public void ToScriptText_HoldClick_WritesHoldClickLine()
        {
            var text = MacroScriptSerializer.ToScriptText(new MacroInstruction[] { new MacroInstruction.HoldClick("240", "160", 600) });
            Assert.Equal("HOLD CLICK 240,160 600" + Environment.NewLine, text);
        }

        [Fact]
        public void ToScriptText_Wait_WritesWaitLine()
        {
            var text = MacroScriptSerializer.ToScriptText(new MacroInstruction[] { new MacroInstruction.Wait(500) });
            Assert.Equal("WAIT 500" + Environment.NewLine, text);
        }

        [Fact]
        public void ToScriptText_Paste_WritesPasteLine()
        {
            var text = MacroScriptSerializer.ToScriptText(new MacroInstruction[] { new MacroInstruction.Paste("{NEXT_SYSTEM}") });
            Assert.Equal("PASTE {NEXT_SYSTEM}" + Environment.NewLine, text);
        }

        [Fact]
        public void ToScriptText_Call_WritesCallLine()
        {
            var text = MacroScriptSerializer.ToScriptText(new MacroInstruction[] { new MacroInstruction.Call("refuel") });
            Assert.Equal("CALL refuel" + Environment.NewLine, text);
        }

        [Fact]
        public void ToScriptText_MultipleSteps_OnePerLine()
        {
            var steps = new MacroInstruction[]
            {
                new MacroInstruction.Tap("UP"),
                new MacroInstruction.Wait(150),
                new MacroInstruction.Click("10", "20")
            };

            var text = MacroScriptSerializer.ToScriptText(steps);

            Assert.Equal("UP" + Environment.NewLine + "WAIT 150" + Environment.NewLine + "CLICK 10,20" + Environment.NewLine, text);
        }

        [Fact]
        public void RoundTrip_SerializeThenParse_ProducesEquivalentFlatSteps()
        {
            var steps = new MacroInstruction[]
            {
                new MacroInstruction.Tap("UP"),
                new MacroInstruction.Hold("KEY Control+A", 400),
                new MacroInstruction.Click("100", "200"),
                new MacroInstruction.HoldClick("{CENTRE}", "50", 300),
                new MacroInstruction.Wait(250),
                new MacroInstruction.Paste("{NEXT_SYSTEM}")
            };

            var text = MacroScriptSerializer.ToScriptText(steps);
            var parsed = MacroScriptParser.Parse(text);

            Assert.Equal(steps.Length, parsed.MainSteps.Count);
            for (var i = 0; i < steps.Length; i++)
            {
                Assert.Equal(steps[i], parsed.MainSteps[i]);
            }
        }
    }
}
