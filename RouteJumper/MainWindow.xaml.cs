using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using RouteJumper.Services;
using RouteJumper.ViewModels;

namespace RouteJumper
{
    public partial class MainWindow : Window
    {
        private const string LeftKey = "WindowLeft";
        private const string TopKey = "WindowTop";
        private const string WidthKey = "WindowWidth";
        private const string HeightKey = "WindowHeight";
        private const string MaximizedKey = "WindowMaximized";

        private readonly AppSettingsStore _settings = new();

        private HwndSource? _hwndSource;

        public MainWindow()
        {
            InitializeComponent();
            SourceInitialized += OnSourceInitialized;
            Closing += OnClosing;
        }

        /// <summary>
        /// Restores the window's last position/size/maximized state if one was persisted and it
        /// would still land somewhere reachable (see IsPositionVisibleOnAnyMonitor - monitor
        /// setups change between sessions); otherwise falls back to the original default: placed
        /// against the right edge of the physically rightmost monitor, so it stays out of the
        /// way of other apps (e.g. Visual Studio) on the left/middle monitors. Runs at
        /// SourceInitialized (HWND exists, window not yet shown) so there's no visible jump.
        /// </summary>
        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            SetUpClipboardMonitoring();

            if (TryRestoreBounds())
            {
                return;
            }

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

        /// <summary>
        /// Registers this window to receive WM_CLIPBOARDUPDATE - view-layer
        /// glue, same carve-out as the window-placement/bounds-persistence logic already in this
        /// file, since it needs a real HWND/message pump that RouteViewModel has no access to.
        /// The actual reaction (clearing whichever row's clipboard icon is stale) is business
        /// logic and lives in RouteViewModel.OnSystemClipboardChanged - this just forwards the
        /// notification to it.
        /// </summary>
        private void SetUpClipboardMonitoring()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            _hwndSource = HwndSource.FromHwnd(hwnd);
            if (_hwndSource is null)
            {
                return;
            }

            ClipboardMonitor.AddListener(hwnd);
            _hwndSource.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == ClipboardMonitor.WM_CLIPBOARDUPDATE)
            {
                (DataContext as MainViewModel)?.RouteViewModel.OnSystemClipboardChanged();
            }

            return IntPtr.Zero;
        }

        private bool TryRestoreBounds()
        {
            var left = _settings.GetDouble(LeftKey);
            var top = _settings.GetDouble(TopKey);
            var width = _settings.GetDouble(WidthKey);
            var height = _settings.GetDouble(HeightKey);

            if (left is null || top is null || width is null || height is null)
            {
                return false;
            }

            if (!IsPositionVisibleOnAnyMonitor(left.Value, top.Value))
            {
                return false;
            }

            Left = left.Value;
            Top = top.Value;
            Width = width.Value;
            Height = height.Value;

            if (_settings.GetBool(MaximizedKey))
            {
                WindowState = WindowState.Maximized;
            }

            return true;
        }

        /// <summary>
        /// Best-effort sanity check, not a precise one: converts every currently-connected
        /// monitor's work area into DIPs using this window's own composition-target transform
        /// (the same simplification the default placement above already relies on - accurate for
        /// monitors sharing that transform's DPI scale, an acceptable approximation for a
        /// fallback check whose only job is to catch the egregious case of a fully disconnected
        /// monitor) and checks whether the persisted top-left corner would land inside any of
        /// them. If not - a monitor was unplugged/replugged differently, or resolution changed -
        /// restoring the persisted position would leave the window unreachable, so the caller
        /// falls back to the default placement instead.
        /// </summary>
        private bool IsPositionVisibleOnAnyMonitor(double left, double top)
        {
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget is null)
            {
                return false;
            }

            var transform = source.CompositionTarget.TransformFromDevice;
            foreach (var monitor in Win32Monitors.GetAllMonitors())
            {
                var topLeftDip = transform.Transform(new Point(monitor.WorkAreaRect.Left, monitor.WorkAreaRect.Top));
                var bottomRightDip = transform.Transform(new Point(monitor.WorkAreaRect.Right, monitor.WorkAreaRect.Bottom));

                if (left >= topLeftDip.X && left <= bottomRightDip.X && top >= topLeftDip.Y && top <= bottomRightDip.Y)
                {
                    return true;
                }
            }

            return false;
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            var isMaximized = WindowState == WindowState.Maximized;

            // RestoreBounds, not Left/Top/Width/Height, while maximized - those reflect the
            // full-screen size at this point, which is not what should be restored as the
            // "normal" size next launch.
            var bounds = isMaximized ? RestoreBounds : new Rect(Left, Top, Width, Height);

            _settings.SetDouble(LeftKey, bounds.Left);
            _settings.SetDouble(TopKey, bounds.Top);
            _settings.SetDouble(WidthKey, bounds.Width);
            _settings.SetDouble(HeightKey, bounds.Height);
            _settings.SetBool(MaximizedKey, isMaximized);

            if (_hwndSource != null)
            {
                ClipboardMonitor.RemoveListener(_hwndSource.Handle);
                _hwndSource.RemoveHook(WndProc);
            }
        }
    }
}
