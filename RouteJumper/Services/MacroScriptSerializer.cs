using System.Text;

namespace RouteJumper.Services
{
    /// <summary>
    /// Converts recorded steps (SPEC §6.3) back into script text - the inverse of
    /// <see cref="MacroScriptParser"/>, but only ever needs to handle the flat instruction kinds
    /// <see cref="InputRecorder"/> actually produces (Tap/Hold/Click/HoldClick/Wait/Call) - a
    /// recording never generates Repeat blocks on its own; those are something a user adds
    /// afterward by hand-editing the resulting text.
    /// </summary>
    public static class MacroScriptSerializer
    {
        public static string ToScriptText(IEnumerable<MacroInstruction> steps)
        {
            var builder = new StringBuilder();
            foreach (var step in steps)
            {
                builder.AppendLine(ToLine(step));
            }

            return builder.ToString();
        }

        private static string ToLine(MacroInstruction step) => step switch
        {
            MacroInstruction.Tap tap => tap.Token,
            MacroInstruction.Hold hold => $"HOLD {hold.Token} {hold.DurationMs}",
            MacroInstruction.Click click => $"CLICK {click.X},{click.Y}",
            MacroInstruction.HoldClick holdClick => $"HOLD CLICK {holdClick.X},{holdClick.Y} {holdClick.DurationMs}",
            MacroInstruction.Wait wait => $"WAIT {wait.DurationMs}",
            MacroInstruction.Paste paste => $"PASTE {paste.Text}",
            MacroInstruction.Call call => $"CALL {call.MacroName}",
            MacroInstruction.Repeat repeat => $"REPEAT {repeat.Count}",
            _ => string.Empty
        };
    }
}
