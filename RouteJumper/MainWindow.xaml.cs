using System.Windows;
using RouteJumper.Services;

namespace RouteJumper
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            SourceInitialized += OnSourceInitialized;
        }

        /// <summary>
        /// Places the window against the right edge of the physically rightmost monitor, so it
        /// stays out of the way of other apps (e.g. Visual Studio) on the left/middle monitors.
        /// Runs at SourceInitialized (HWND exists, window not yet shown) so there's no visible jump.
        /// </summary>
        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            var monitor = Win32Monitors.GetRightmostMonitor();
            if (monitor is null)
            {
                return;
            }

            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget is null)
            {
                return;
            }

            var transform = source.CompositionTarget.TransformFromDevice;
            var topRightDip = transform.Transform(new Point(monitor.Value.MonitorRect.Right, monitor.Value.MonitorRect.Top));
            var bottomDip = transform.Transform(new Point(monitor.Value.MonitorRect.Right, monitor.Value.MonitorRect.Bottom));
            var monitorHeightDip = bottomDip.Y - topRightDip.Y;

            const double edgeMargin = 10;
            Left = topRightDip.X - Width - edgeMargin;
            Top = topRightDip.Y + (monitorHeightDip - Height) / 2;
        }
    }
}
