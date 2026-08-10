using System.Runtime.InteropServices;

namespace RouteJumper.Services
{
    /// <summary>
    /// Shared by MacroPlayer and InputRecorder to bring their target Elite Dangerous window to
    /// the foreground before sending/capturing input.
    ///
    /// A bare SetForegroundWindow call is subject to Windows' focus-stealing prevention: the
    /// system only lets a process set the foreground window unconditionally if that process is
    /// already the foreground process, was itself launched by the current foreground process, or
    /// (among a couple of other narrow cases) is the process that most recently received an
    /// input event. RouteJumper is normally none of those relative to whatever the user was just
    /// doing (e.g. clicking Play while some other window has focus), so a direct call frequently
    /// fails silently - the target's taskbar button just flashes instead of the window actually
    /// coming to front - and the caller has no reliable way to tell the difference from here.
    ///
    /// Sending one harmless, zero-motion synthetic mouse move via SendInput immediately before
    /// SetForegroundWindow satisfies that last condition ("received the last input event")
    /// without moving the cursor or clicking anything, letting the follow-up call succeed even
    /// when RouteJumper itself isn't currently focused.
    /// </summary>
    public static class Win32Foreground
    {
        private const int SW_RESTORE = 9;
        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const int INPUT_MOUSE = 0;

        public static void ForceForegroundWindow(IntPtr hwnd)
        {
            NativeMethods.ShowWindow(hwnd, SW_RESTORE);
            SendZeroMotionMouseMove();
            NativeMethods.SetForegroundWindow(hwnd);
        }

        private static void SendZeroMotionMouseMove()
        {
            var input = new NativeMethods.INPUT
            {
                type = INPUT_MOUSE,
                mi = new NativeMethods.MOUSEINPUT { dwFlags = MOUSEEVENTF_MOVE }
            };

            NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>());
        }

        private static class NativeMethods
        {
            [DllImport("user32.dll")]
            public static extern bool SetForegroundWindow(IntPtr hWnd);

            [DllImport("user32.dll")]
            public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

            [StructLayout(LayoutKind.Sequential)]
            public struct INPUT
            {
                public int type;
                public MOUSEINPUT mi;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct MOUSEINPUT
            {
                public int dx;
                public int dy;
                public uint mouseData;
                public uint dwFlags;
                public uint time;
                public IntPtr dwExtraInfo;
            }
        }
    }
}
