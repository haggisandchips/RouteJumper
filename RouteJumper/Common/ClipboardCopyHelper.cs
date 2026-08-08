using System.Media;
using System.Windows;

namespace RouteJumper.Common
{
    /// <summary>
    /// Shared "copy text to the clipboard and play a confirmation sound" logic - used wherever a
    /// single click should copy something with audible feedback (the Route tab's row
    /// click-to-copy, the Roles tab's journal filename click-to-copy). Deliberately just the
    /// clipboard write plus the ping - the visual clipboard-source icon on the Route tab is a
    /// separate, additional concern layered on top by RouteViewModel itself, not something every
    /// consumer of this helper needs.
    /// </summary>
    public static class ClipboardCopyHelper
    {
        public static void CopyWithPing(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            Clipboard.SetText(text);
            SystemSounds.Asterisk.Play();
        }
    }
}
