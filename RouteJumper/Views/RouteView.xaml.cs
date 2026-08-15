using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RouteJumper.Services;
using RouteJumper.ViewModels;

namespace RouteJumper.Views
{
    public partial class RouteView : UserControl
    {
        // Fixed column order (see the DataGrid.Columns declared in RouteView.xaml): blank icon,
        // #, System, Distance, Star Type, Status. Status (the trailing column) is deliberately
        // excluded from both Restore/Save below - it stays a Star column always, filling whatever
        // the other five don't use, so its progress bar has real remaining width to stretch into
        // (§3.1's "no fixed-pixel layouts"). Capturing its ActualWidth on close and forcing that
        // back as a literal DataGridLength on next launch - what this used to do - freezes it at
        // whatever pixel width it happened to be showing at that moment, permanently defeating
        // its own fill behavior from then on regardless of the window's actual size.
        private const string IconColumnWidthKey = "RouteColumnWidth.Icon";
        private const string NumberColumnWidthKey = "RouteColumnWidth.Number";
        private const string SystemColumnWidthKey = "RouteColumnWidth.System";
        private const string DistanceColumnWidthKey = "RouteColumnWidth.Distance";
        private const string StarTypeColumnWidthKey = "RouteColumnWidth.StarType";

        private readonly AppSettingsStore _settings = new();
        private Window? _window;

        public RouteView()
        {
            InitializeComponent();

            // Declarative FocusManager.FocusedElement doesn't reliably take effect on a plain
            // UserControl, since it isn't its own focus scope. Focusing imperatively on Loaded
            // is the standard, reliable pattern; this is pure view-layer behavior (no business
            // logic), same carve-out as MainWindow.xaml.cs's startup window placement.
            RouteTextBox.Loaded += (_, _) => Keyboard.Focus(RouteTextBox);

            // Loaded only fires once (toggling the containing Grid's Visibility between Save/
            // Edit doesn't unload the TextBox), so re-focus separately whenever it becomes
            // visible again - i.e. every time Edit is clicked, not just on first launch.
            RouteTextBox.IsVisibleChanged += (_, e) =>
            {
                if (e.NewValue is true)
                {
                    Keyboard.Focus(RouteTextBox);
                }
            };

            RestoreColumnWidths();

            // DataGridColumn.Width can't be data-bound (it isn't part of the visual/logical
            // tree, so it never inherits a DataContext) - handled here in code-behind instead,
            // the same view-layer carve-out already covering window placement/bounds
            // (MainWindow.xaml.cs) and clipboard monitoring. Persisted only on the owning
            // window's Closing, not on every drag movement while resizing - same "save on close"
            // pattern already used for the window's own bounds, rather than a SQLite write per
            // pixel dragged. Hooked here (once this control actually has a Window, on Loaded)
            // rather than in MainWindow.xaml.cs itself, so this table's column widths stay a
            // self-contained concern of this view.
            Loaded += (_, _) =>
            {
                if (_window is null && Window.GetWindow(this) is { } window)
                {
                    _window = window;
                    _window.Closing += (_, _) => SaveColumnWidths();
                }
            };
        }

        private void RestoreColumnWidths()
        {
            RestoreColumnWidth(0, IconColumnWidthKey);
            RestoreColumnWidth(1, NumberColumnWidthKey);
            RestoreColumnWidth(2, SystemColumnWidthKey);
            RestoreColumnWidth(3, DistanceColumnWidthKey);
            RestoreColumnWidth(4, StarTypeColumnWidthKey);
        }

        private void RestoreColumnWidth(int columnIndex, string key)
        {
            if (_settings.GetDouble(key) is { } width && columnIndex < RouteDataGrid.Columns.Count)
            {
                RouteDataGrid.Columns[columnIndex].Width = new DataGridLength(width);
            }
        }

        private void SaveColumnWidths()
        {
            SaveColumnWidth(0, IconColumnWidthKey);
            SaveColumnWidth(1, NumberColumnWidthKey);
            SaveColumnWidth(2, SystemColumnWidthKey);
            SaveColumnWidth(3, DistanceColumnWidthKey);
            SaveColumnWidth(4, StarTypeColumnWidthKey);
        }

        /// <summary>DisplayValue, not Value - resolves to the actual current pixel width regardless of whether the column's Width is Auto/Star/Pixel.</summary>
        private void SaveColumnWidth(int columnIndex, string key)
        {
            if (columnIndex < RouteDataGrid.Columns.Count)
            {
                _settings.SetDouble(key, RouteDataGrid.Columns[columnIndex].Width.DisplayValue);
            }
        }

        /// <summary>
        /// "Import Current Route": both this and Trim for FC below overwrite the currently-saved
        /// route outright with no way back (Cancel only ever restores Edit state's own text box,
        /// not a prior Table-state route), so each confirms via a plain Yes/No dialog first -
        /// declining leaves the route untouched. Once confirmed, RouteViewModel.ImportFromNavRoute
        /// applies the result immediately on its own (no Edit-state detour) and there's nothing
        /// further to report - a failure (nothing currently plotted in-game) is logged (Help >
        /// Logs) rather than surfaced as a second dialog, since the CMDR already deliberately
        /// chose to run this and a plain "nothing happened" is enough to see for themselves.
        /// </summary>
        private void OnImportFromNavRouteClick(object sender, RoutedEventArgs e)
        {
            var confirmed = MessageBox.Show(Window.GetWindow(this),
                "This will replace your currently saved route with whatever's plotted in-game right now. Continue?",
                "Import Current Route", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirmed != MessageBoxResult.Yes)
            {
                return;
            }

            ((RouteViewModel)DataContext).ImportFromNavRoute();
        }

        /// <summary>
        /// "Trim for FC": same confirm-then-apply-unconditionally shape as
        /// OnImportFromNavRouteClick above - see its own doc comment for why there's no
        /// follow-up/outcome dialog.
        /// </summary>
        private void OnTrimToJumpRangeClick(object sender, RoutedEventArgs e)
        {
            var confirmed = MessageBox.Show(Window.GetWindow(this),
                "This will trim your saved route down to a series of hops no longer than " +
                $"{RouteViewModel.MaxCarrierJumpLightYears:0}ly, permanently discarding whichever systems aren't needed to stay within that reach. Continue?",
                "Trim for FC", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirmed != MessageBoxResult.Yes)
            {
                return;
            }

            ((RouteViewModel)DataContext).TrimToJumpRange();
        }
    }
}
