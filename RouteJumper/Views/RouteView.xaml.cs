using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RouteJumper.Services;

namespace RouteJumper.Views
{
    public partial class RouteView : UserControl
    {
        // Fixed column order (see the DataGrid.Columns declared in RouteView.xaml): blank icon,
        // #, System, Status.
        private const string IconColumnWidthKey = "RouteColumnWidth.Icon";
        private const string NumberColumnWidthKey = "RouteColumnWidth.Number";
        private const string SystemColumnWidthKey = "RouteColumnWidth.System";
        private const string StatusColumnWidthKey = "RouteColumnWidth.Status";

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
            RestoreColumnWidth(3, StatusColumnWidthKey);
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
            SaveColumnWidth(3, StatusColumnWidthKey);
        }

        /// <summary>DisplayValue, not Value - resolves to the actual current pixel width regardless of whether the column's Width is Auto/Star/Pixel.</summary>
        private void SaveColumnWidth(int columnIndex, string key)
        {
            if (columnIndex < RouteDataGrid.Columns.Count)
            {
                _settings.SetDouble(key, RouteDataGrid.Columns[columnIndex].Width.DisplayValue);
            }
        }
    }
}
