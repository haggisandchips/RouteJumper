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
    /// Parses a macro script's text into instructions (SPEC §6.3). One instruction per line;
    /// blank lines and lines starting with '#' are comments. Grammar:
    ///
    ///   ACTION_NAME              - tap (e.g. UP - see ControlActionExtensions.ToActionName)
    ///   KEY &lt;storage&gt;      - tap a key with no bound action (e.g. KEY Control+A)
    ///   HOLD &lt;token&gt; &lt;ms&gt;   - press-and-hold ACTION_NAME or "KEY ..." for ms milliseconds
    ///   CLICK &lt;x&gt;,&lt;y&gt;       - mouse click, relative to the target window's client area
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
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
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
                    continue;
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
                    continue;
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
                    continue;
                }

                if (upper.StartsWith("CALL "))
                {
                    var name = line[5..].Trim();
                    if (name.Length > 0)
                    {
                        current.Add(new MacroInstruction.Call(name));
                    }
                    continue;
                }

                if (upper.StartsWith("HOLD "))
                {
                    var rest = line[5..].Trim();
                    var lastSpace = rest.LastIndexOf(' ');
                    if (lastSpace > 0 && int.TryParse(rest[(lastSpace + 1)..].Trim(), out var ms) && ms > 0)
                    {
                        current.Add(new MacroInstruction.Hold(rest[..lastSpace].Trim(), ms));
                    }
                    continue;
                }

                if (upper.StartsWith("CLICK "))
                {
                    var coords = line[6..].Trim().Split(',');
                    if (coords.Length == 2 &&
                        int.TryParse(coords[0].Trim(), out var x) &&
                        int.TryParse(coords[1].Trim(), out var y))
                    {
                        current.Add(new MacroInstruction.Click(x, y));
                    }
                    continue;
                }

                if (upper.StartsWith("WAIT "))
                {
                    if (int.TryParse(line[5..].Trim(), out var ms) && ms > 0)
                    {
                        current.Add(new MacroInstruction.Wait(ms));
                    }
                    continue;
                }

                if (upper.StartsWith("PASTE "))
                {
                    current.Add(new MacroInstruction.Paste(line[6..].Trim()));
                    continue;
                }

                // A bare token - either an ACTION_NAME or "KEY <storage>" - is a plain tap.
                current.Add(new MacroInstruction.Tap(line));
            }

            return new ParsedMacroScript
            {
                MainSteps = mainSteps,
                Macros = macros.ToDictionary(
                    kv => kv.Key,
                    kv => (IReadOnlyList<MacroInstruction>)kv.Value,
                    StringComparer.OrdinalIgnoreCase)
            };
        }
    }
}
