namespace RouteJumper.Services
{
    /// <summary>
    /// One parsed line (or block) of a recorded macro script (SPEC §6.3). See
    /// <see cref="MacroScriptParser"/> for the text grammar these are parsed from, and
    /// <see cref="MacroScriptBuilder"/> for how a recording is turned back into that text -
    /// deliberately only ever emitting Tap/Hold/Click/Wait, never Repeat/Call, since loops and
    /// macro calls are something a user adds by hand-editing afterward, not something recording
    /// itself infers.
    ///
    /// Token (on Tap/Hold) is either a SPEC §6.1 action name (e.g. "UP") or, for a key that
    /// isn't bound to any action, "KEY &lt;storage string&gt;" (e.g. "KEY Control+A") - see
    /// KeyBindingFormatter for the storage string format.
    /// </summary>
    public abstract record MacroInstruction
    {
        public sealed record Tap(string Token) : MacroInstruction;

        public sealed record Hold(string Token, int DurationMs) : MacroInstruction;

        /// <summary>
        /// X/Y are relative to the target window's client area, and kept as raw tokens (not
        /// parsed ints) rather than resolved at parse time, since either may be the literal
        /// placeholder "{CENTRE}" - resolved at play time (see MacroPlayer) to that axis' actual
        /// current midpoint, the same "resolved late" principle PASTE's "{NEXT_SYSTEM}" and
        /// Tap/Hold's ACTION_NAME tokens already follow, since the window may have been resized
        /// since the macro was recorded.
        /// </summary>
        public sealed record Click(string X, string Y) : MacroInstruction;

        /// <summary>A click held down for DurationMs before release, at the same X/Y semantics as Click.</summary>
        public sealed record HoldClick(string X, string Y, int DurationMs) : MacroInstruction;

        public sealed record Wait(int DurationMs) : MacroInstruction;

        /// <summary>
        /// Pastes text into the target window (sets the clipboard, then sends Ctrl+V) - Text may
        /// be literal, or contain the "{NEXT_SYSTEM}" placeholder, resolved at play time to the
        /// Route tab's current next (in-progress) system name - see MacroPlayer.
        /// </summary>
        public sealed record Paste(string Text) : MacroInstruction;

        public sealed record Repeat(int Count, IReadOnlyList<MacroInstruction> Body) : MacroInstruction;

        public sealed record Call(string MacroName) : MacroInstruction;
    }
}
