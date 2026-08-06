using System.Runtime.InteropServices;

namespace RouteJumper.Services
{
    /// <summary>
    /// Thin Win32 wrapper for window bounds and monitor enumeration/lookup. Shared by
    /// EliteInstanceScanner (which monitor is a given ED window on) and MainWindow's
    /// startup placement (which monitor is "rightmost").
    /// </summary>
    public static class Win32Monitors
    {
        private const uint MonitorDefaultToNearest = 2;
        private const uint MonitorInfoFPrimary = 1;

        public readonly struct Rect
        {
            public int Left { get; init; }
            public int Top { get; init; }
            public int Right { get; init; }
            public int Bottom { get; init; }

            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        public readonly struct MonitorInfo
        {
            public required Rect MonitorRect { get; init; }
            public required Rect WorkAreaRect { get; init; }
            public required bool IsPrimary { get; init; }
            public required string DeviceName { get; init; }
        }

        /// <summary>Screen-coordinate bounds of the given window, or null if it can't be read.</summary>
        public static Rect? GetWindowRect(IntPtr hwnd)
        {
            return NativeMethods.GetWindowRect(hwnd, out var rect) ? ToRect(rect) : null;
        }

        /// <summary>The monitor a given window is (mostly) on, or null if it can't be determined.</summary>
        public static MonitorInfo? GetMonitorForWindow(IntPtr hwnd)
        {
            var hMonitor = NativeMethods.MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            return hMonitor == IntPtr.Zero ? null : GetMonitorInfo(hMonitor);
        }

        /// <summary>All monitors currently attached, in OS enumeration order.</summary>
        public static IReadOnlyList<MonitorInfo> GetAllMonitors()
        {
            var monitors = new List<MonitorInfo>();

            bool Callback(IntPtr hMonitor, IntPtr hdc, ref NativeMethods.RectStruct rect, IntPtr lParam)
            {
                var info = GetMonitorInfo(hMonitor);
                if (info.HasValue)
                {
                    monitors.Add(info.Value);
                }

                return true;
            }

            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero);
            return monitors;
        }

        /// <summary>The monitor whose left edge is furthest right - i.e. physically rightmost.</summary>
        public static MonitorInfo? GetRightmostMonitor()
        {
            var monitors = GetAllMonitors();
            return monitors.Count == 0
                ? null
                : monitors.OrderByDescending(m => m.MonitorRect.Left).First();
        }

        private static MonitorInfo? GetMonitorInfo(IntPtr hMonitor)
        {
            var mi = NativeMethods.MonitorInfoEx.Create();
            if (!NativeMethods.GetMonitorInfo(hMonitor, ref mi))
            {
                return null;
            }

            return new MonitorInfo
            {
                MonitorRect = ToRect(mi.MonitorRect),
                WorkAreaRect = ToRect(mi.WorkAreaRect),
                IsPrimary = (mi.Flags & MonitorInfoFPrimary) != 0,
                DeviceName = mi.DeviceName
            };
        }

        private static Rect ToRect(NativeMethods.RectStruct r) => new()
        {
            Left = r.Left,
            Top = r.Top,
            Right = r.Right,
            Bottom = r.Bottom
        };

        private static class NativeMethods
        {
            public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RectStruct lprcMonitor, IntPtr dwData);

            [DllImport("user32.dll")]
            public static extern bool GetWindowRect(IntPtr hWnd, out RectStruct lpRect);

            [DllImport("user32.dll")]
            public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoEx lpmi);

            [DllImport("user32.dll")]
            public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

            [StructLayout(LayoutKind.Sequential)]
            public struct RectStruct
            {
                public int Left;
                public int Top;
                public int Right;
                public int Bottom;
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            public struct MonitorInfoEx
            {
                public int Size;
                public RectStruct MonitorRect;
                public RectStruct WorkAreaRect;
                public uint Flags;

                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
                public string DeviceName;

                public static MonitorInfoEx Create() => new()
                {
                    Size = Marshal.SizeOf<MonitorInfoEx>(),
                    DeviceName = string.Empty
                };
            }
        }
    }
}
