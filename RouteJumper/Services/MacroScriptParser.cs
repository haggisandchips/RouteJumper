namespace RouteJumper.Services
{
    /// <summary>Result of parsing a macro script (SPEC §6.3) - the top-level steps, plus any named macros it defines.</summary>
    public sealed class ParsedMacroScript
    {
        public IReadOnlyList<MacroInstruction> MainSteps { get; init; } = Array.Empty<MacroInstruction>();

        public IReadOnlyDictionary<string, IReadOnlyList<MacroInstruction>> Macros { get; init; } =
            new Dictionary<string, IReadOnlyList<MacroInstruction>>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Parses a macro script's text into instructions (SPEC §6.3). One instruction per line, or
    /// several instructions on one line separated by ';' (surrounding whitespace ignored) for
    /// grouping related steps together, e.g. "UP; WAIT 200; DOWN"; blank lines, blank segments,
    /// and lines/segments starting with '#' are comments. Grammar:
    ///
    ///   ACTION_NAME              - tap (e.g. UP - see ControlActionExtensions.ToActionName)
    ///   KEY &lt;storage&gt;      - tap a key with no bound action (e.g. KEY Control+A)
    ///   HOLD &lt;token&gt; &lt;ms&gt;   - press-and-hold ACTION_NAME or "KEY ..." for ms milliseconds
    ///   CLICK &lt;x&gt;,&lt;y&gt;       - mouse click, relative to the target window's client area;
    ///                             either coordinate may be the literal placeholder "{CENTRE}",
    ///                             resolved at play time to that axis' current midpoint
    ///   HOLD CLICK &lt;x&gt;,&lt;y&gt; &lt;ms&gt; - press-and-hold a click at x,y for ms milliseconds
    ///   WAIT &lt;ms&gt;             - pause before the next step
    ///   PASTE &lt;text&gt;         - sets the clipboard to text and sends Ctrl+V; text may contain
    ///                             the "{NEXT_SYSTEM}" placeholder, resolved at play time to the
    ///                             Route tab's current next system name
    ///   REPEAT &lt;n&gt; ... END   - repeats its body n times (nestable)
    ///   MACRO &lt;name&gt; ... END - defines a named, reusable macro (top-level only, not nested)
    ///   CALL &lt;name&gt;          - invokes a previously (or later) defined macro inline
    ///
    /// Deliberately permissive rather than strict - a malformed line is skipped rather than
    /// raising an error, since this is meant to be hand-edited free text (SPEC §6.3: "suitable
    /// for human editing"), not a language a typo should be able to break outright. Resolving
    /// what a token actually means (an action's *current* key binding, or an unresolvable
    /// action/macro name) is deferred to play time - see MacroPlayer - so a script stays valid
    /// even if key bindings change after it was recorded.
    /// </summary>
    public static class MacroScriptParser
    {
        public static ParsedMacroScript Parse(string scriptText)
        {
            var mainSteps = new List<MacroInstruction>();
            var macros = new Dictionary<string, List<MacroInstruction>>(StringComparer.OrdinalIgnoreCase);

            var frames = new List<List<MacroInstruction>> { mainSteps };
            var frameIsMacro = new List<bool> { false };
            var inMacro = false;

            foreach (var rawLine in scriptText.Replace("\r\n", "\n").Split('\n'))
            {
                foreach (var rawSegment in rawLine.Split(';'))
                {
                    ProcessSegment(rawSegment);
                }
            }

            return new ParsedMacroScript
            {
                MainSteps = mainSteps,
                Macros = macros.ToDictionary(
                    kv => kv.Key,
                    kv => (IReadOnlyList<MacroInstruction>)kv.Value,
                    StringComparer.OrdinalIgnoreCase)
            };

            void ProcessSegment(string rawSegment)
            {
                var line = rawSegment.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    return;
                }

                var upper = line.ToUpperInvariant();
                var current = frames[^1];

                if (upper.StartsWith("REPEAT "))
                {
                    if (int.TryParse(line[7..].Trim(), out var count) && count > 0)
                    {
                        var body = new List<MacroInstruction>();
                        current.Add(new MacroInstruction.Repeat(count, body));
                        frames.Add(body);
                        frameIsMacro.Add(false);
                    }
                    return;
                }

                if (upper.StartsWith("MACRO "))
                {
                    var name = line[6..].Trim();
                    // Only allowed at the top level, and macros can't nest inside each other -
                    // kept simple, since a macro calling into another named macro (CALL) already
                    // covers composition without needing physical nesting too.
                    if (name.Length > 0 && frames.Count == 1 && !inMacro)
                    {
                        var body = new List<MacroInstruction>();
                        macros[name] = body;
                        frames.Add(body);
                        frameIsMacro.Add(true);
                        inMacro = true;
                    }
                    return;
                }

                if (upper == "END")
                {
                    if (frames.Count > 1)
                    {
                        frames.RemoveAt(frames.Count - 1);
                        if (frameIsMacro[^1])
                        {
                            inMacro = false;
                        }
                        frameIsMacro.RemoveAt(frameIsMacro.Count - 1);
                    }
                    return;
                }

                if (upper.StartsWith("CALL "))
                {
                    var name = line[5..].Trim();
                    if (name.Length > 0)
                    {
                        current.Add(new MacroInstruction.Call(name));
                    }
                    return;
                }

                if (upper.StartsWith("HOLD CLICK "))
                {
                    var rest = line[11..].Trim();
                    var lastSpace = rest.LastIndexOf(' ');
                    if (lastSpace > 0 &&
                        int.TryParse(rest[(lastSpace + 1)..].Trim(), out var holdMs) && holdMs > 0 &&
                        TryParseCoordinates(rest[..lastSpace].Trim(), out var holdX, out var holdY))
                    {
                        current.Add(new MacroInstruction.HoldClick(holdX, holdY, holdMs));
                    }
                    return;
                }

                if (upper.StartsWith("HOLD "))
                {
                    var rest = line[5..].Trim();
                    var lastSpace = rest.LastIndexOf(' ');
                    if (lastSpace > 0 && int.TryParse(rest[(lastSpace + 1)..].Trim(), out var ms) && ms > 0)
                    {
                        current.Add(new MacroInstruction.Hold(rest[..lastSpace].Trim(), ms));
                    }
                    return;
                }

                if (upper.StartsWith("CLICK "))
                {
                    if (TryParseCoordinates(line[6..].Trim(), out var x, out var y))
                    {
                        current.Add(new MacroInstruction.Click(x, y));
                    }
                    return;
                }

                if (upper.StartsWith("WAIT "))
                {
                    if (int.TryParse(line[5..].Trim(), out var ms) && ms > 0)
                    {
                        current.Add(new MacroInstruction.Wait(ms));
                    }
                    return;
                }

                if (upper.StartsWith("PASTE "))
                {
                    current.Add(new MacroInstruction.Paste(line[6..].Trim()));
                    return;
                }

                // A bare token - either an ACTION_NAME or "KEY <storage>" - is a plain tap.
                current.Add(new MacroInstruction.Tap(line));
            }
        }

        /// <summary>
        /// A coordinate token is valid if it's an integer or the literal placeholder "{CENTRE}"
        /// (case-insensitive) - actually resolving "{CENTRE}" happens at play time (see
        /// MacroPlayer), since it depends on the target window's current size.
        /// </summary>
        private static bool TryParseCoordinates(string coordinatePair, out string x, out string y)
        {
            x = string.Empty;
            y = string.Empty;

            var coords = coordinatePair.Split(',');
            if (coords.Length != 2)
            {
                return false;
            }

            var xToken = coords[0].Trim();
            var yToken = coords[1].Trim();
            if (!IsValidCoordinateToken(xToken) || !IsValidCoordinateToken(yToken))
            {
                return false;
            }

            x = xToken;
            y = yToken;
            return true;
        }

        private static bool IsValidCoordinateToken(string token) =>
            token.Equals("{CENTRE}", StringComparison.OrdinalIgnoreCase) || int.TryParse(token, out _);
    }
}
