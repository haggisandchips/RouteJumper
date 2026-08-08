using System.Windows.Input;

namespace RouteJumper.Services
{
    /// <summary>
    /// Converts between a captured (Key, ModifierKeys) pair and two string forms:
    /// a canonical, parseable "storage" form (WPF's own Key names, e.g. "Control+Shift+J") used
    /// for persistence and for exact-match comparison while recording (see MacroScriptBuilder),
    /// and a friendlier "display" form (e.g. "Ctrl+Shift+Del") used in the UI and in recorded
    /// scripts. Kept as two separate forms rather than one, since the friendly names
    /// (Key.Back -> "Backspace", Key.D4 -> "4", ...) aren't guaranteed distinct enough to safely
    /// round-trip through <see cref="TryParse"/> the way WPF's own enum names are.
    /// </summary>
    public static class KeyBindingFormatter
    {
        public static string ToStorageString(Key key, ModifierKeys modifiers) =>
            string.Join('+', ModifierParts(modifiers, "Control", "Shift", "Alt", "Windows").Append(key.ToString()));

        public static string ToDisplayString(Key key, ModifierKeys modifiers) =>
            string.Join('+', ModifierParts(modifiers, "Ctrl", "Shift", "Alt", "Win").Append(FriendlyKeyName(key)));

        /// <summary>Display form directly from a storage string - for showing a persisted/parsed binding.</summary>
        public static string ToDisplayString(string storageString) =>
            TryParse(storageString, out var key, out var modifiers) ? ToDisplayString(key, modifiers) : storageString;

        public static bool TryParse(string storageString, out Key key, out ModifierKeys modifiers)
        {
            key = Key.None;
            modifiers = ModifierKeys.None;

            var parts = storageString.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                return false;
            }

            for (var i = 0; i < parts.Length - 1; i++)
            {
                modifiers |= parts[i].ToUpperInvariant() switch
                {
                    "CONTROL" or "CTRL" => ModifierKeys.Control,
                    "SHIFT" => ModifierKeys.Shift,
                    "ALT" => ModifierKeys.Alt,
                    "WINDOWS" or "WIN" => ModifierKeys.Windows,
                    _ => ModifierKeys.None
                };
            }

            return TryParseKey(parts[^1], out key);
        }

        /// <summary>
        /// Accepts either WPF's own Key enum name (the canonical storage form) or one of
        /// FriendlyKeyName's display aliases (e.g. "Enter", "Del", "Up Arrow") - a hand-edited
        /// macro script (SPEC §6.3) is just as likely to use the friendly name shown elsewhere in
        /// the UI as the raw enum name, and silently skipping a step over that mismatch (as
        /// MacroPlayer does for any token it can't resolve) is a surprising way for a script to
        /// fail. Case-insensitive for the same hand-editing-forgiveness reason.
        /// </summary>
        private static bool TryParseKey(string token, out Key key)
        {
            key = FriendlyAliasToKey(token);
            if (key != Key.None)
            {
                return true;
            }

            return Enum.TryParse(token, ignoreCase: true, out key);
        }

        private static Key FriendlyAliasToKey(string token) => token.ToUpperInvariant() switch
        {
            "UP ARROW" => Key.Up,
            "DOWN ARROW" => Key.Down,
            "LEFT ARROW" => Key.Left,
            "RIGHT ARROW" => Key.Right,
            "BACKSPACE" => Key.Back,
            "DEL" => Key.Delete,
            "ENTER" or "RETURN" => Key.Return,
            "ESC" => Key.Escape,
            "0" => Key.D0,
            "1" => Key.D1,
            "2" => Key.D2,
            "3" => Key.D3,
            "4" => Key.D4,
            "5" => Key.D5,
            "6" => Key.D6,
            "7" => Key.D7,
            "8" => Key.D8,
            "9" => Key.D9,
            _ => Key.None
        };

        private static IEnumerable<string> ModifierParts(ModifierKeys modifiers, string control, string shift, string alt, string windows)
        {
            if (modifiers.HasFlag(ModifierKeys.Control))
            {
                yield return control;
            }

            if (modifiers.HasFlag(ModifierKeys.Shift))
            {
                yield return shift;
            }

            if (modifiers.HasFlag(ModifierKeys.Alt))
            {
                yield return alt;
            }

            if (modifiers.HasFlag(ModifierKeys.Windows))
            {
                yield return windows;
            }
        }

        /// <summary>
        /// Friendlier names for keys whose raw WPF enum name reads awkwardly - matches the exact
        /// wording SPEC §6.1's default binding table uses (e.g. "Up Arrow", "Del") wherever one
        /// of those keys is involved.
        /// </summary>
        private static string FriendlyKeyName(Key key) => key switch
        {
            Key.Up => "Up Arrow",
            Key.Down => "Down Arrow",
            Key.Left => "Left Arrow",
            Key.Right => "Right Arrow",
            Key.Back => "Backspace",
            Key.Delete => "Del",
            Key.Return => "Enter",
            Key.D0 => "0",
            Key.D1 => "1",
            Key.D2 => "2",
            Key.D3 => "3",
            Key.D4 => "4",
            Key.D5 => "5",
            Key.D6 => "6",
            Key.D7 => "7",
            Key.D8 => "8",
            Key.D9 => "9",
            _ => key.ToString()
        };
    }
}
