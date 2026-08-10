using RouteJumper.Services;
using Xunit;

namespace RouteJumper.Tests.Services
{
    /// <summary>
    /// Win32Monitors/ClipboardMonitor are thin P/Invoke wrappers with no branching logic of
    /// their own to unit-test - these are smoke tests confirming they call through to the real
    /// OS without throwing, on whatever machine the suite runs on.
    /// </summary>
    public class Win32MonitorsTests
    {
        [Fact]
        public void GetAllMonitors_ReturnsAtLeastOneMonitor()
        {
            var monitors = Win32Monitors.GetAllMonitors();
            Assert.NotEmpty(monitors);
        }

        [Fact]
        public void GetRightmostMonitor_ReturnsAMonitorFromGetAllMonitors()
        {
            var all = Win32Monitors.GetAllMonitors();
            var rightmost = Win32Monitors.GetRightmostMonitor();

            Assert.NotNull(rightmost);
            Assert.Equal(all.Max(m => m.MonitorRect.Left), rightmost!.Value.MonitorRect.Left);
        }

        [Fact]
        public void GetWindowRect_InvalidHandle_ReturnsNull()
        {
            Assert.Null(Win32Monitors.GetWindowRect(IntPtr.Zero));
        }

        [Fact]
        public void GetMonitorForWindow_InvalidHandle_FallsBackToNearestMonitorRatherThanNull()
        {
            // MONITOR_DEFAULTTONEAREST guarantees a non-null HMONITOR even for an invalid/null
            // window handle - it falls back to the nearest (typically primary) monitor instead
            // of failing, unlike GetWindowRect above.
            Assert.NotNull(Win32Monitors.GetMonitorForWindow(IntPtr.Zero));
        }

        [Fact]
        public void Rect_WidthAndHeight_AreComputedFromEdges()
        {
            var rect = new Win32Monitors.Rect { Left = 10, Top = 20, Right = 110, Bottom = 220 };
            Assert.Equal(100, rect.Width);
            Assert.Equal(200, rect.Height);
        }
    }

    public class ClipboardMonitorTests
    {
        [Fact]
        public void GetSequenceNumber_DoesNotThrow()
        {
            ClipboardMonitor.GetSequenceNumber();
        }

        [Fact]
        public void AddListener_InvalidHandle_ReturnsFalseRatherThanThrowing()
        {
            Assert.False(ClipboardMonitor.AddListener(IntPtr.Zero));
        }
    }
}
