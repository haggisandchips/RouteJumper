using System.Runtime.InteropServices;

namespace RouteJumper.Services
{
    /// <summary>
    /// Thin Win32 wrapper for detecting system clipboard changes (SPEC §5.6's Update).
    /// AddListener/RemoveListener register/unregister a window to receive WM_CLIPBOARDUPDATE;
    /// GetSequenceNumber reads a counter Windows bumps on every clipboard content change, from
    /// any source (this app or another) - used to tell "this WM_CLIPBOARDUPDATE is just
    /// confirming a write this app itself just made" apart from "something else changed the
    /// clipboard". The actual message-loop hook lives in MainWindow.xaml.cs (needs a real
    /// HWND/HwndSource - the same view-layer carve-out its startup placement and window-bounds
    /// persistence already use), not here; this class is a pure P/Invoke wrapper, same pattern
    /// as Win32Monitors.
    /// </summary>
    public static class ClipboardMonitor
    {
        public const int WM_CLIPBOARDUPDATE = 0x031D;

        public static bool AddListener(IntPtr hwnd) => NativeMethods.AddClipboardFormatListener(hwnd);

        public static bool RemoveListener(IntPtr hwnd) => NativeMethods.RemoveClipboardFormatListener(hwnd);

        public static uint GetSequenceNumber() => NativeMethods.GetClipboardSequenceNumber();

        private static class NativeMethods
        {
            [DllImport("user32.dll")]
            public static extern bool AddClipboardFormatListener(IntPtr hwnd);

            [DllImport("user32.dll")]
            public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

            [DllImport("user32.dll")]
            public static extern uint GetClipboardSequenceNumber();
        }
    }
}
