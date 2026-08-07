using System.Windows.Controls;
using System.Windows.Input;

namespace RouteJumper.Views
{
    public partial class RouteView : UserControl
    {
        public RouteView()
        {
            InitializeComponent();

            // Declarative FocusManager.FocusedElement doesn't reliably take effect on a plain
            // UserControl (it isn't its own focus scope) - confirmed live (window kept focus,
            // not the TextBox). Focusing imperatively on Loaded is the standard, reliable
            // pattern; this is pure view-layer behavior (no business logic), same carve-out as
            // MainWindow.xaml.cs's startup window placement.
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
        }
    }
}
