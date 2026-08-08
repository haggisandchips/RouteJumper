using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using RouteJumper.ViewModels;

namespace RouteJumper.Views
{
    public partial class ControlsView : UserControl
    {
        public ControlsView()
        {
            InitializeComponent();

            // View-layer input glue, same carve-out as RouteView.xaml.cs's focus handling:
            // whichever KeyBindingViewModel is currently "capturing" (button clicked, waiting
            // for a keypress) needs the raw Key/ModifierKeys of the next keydown, which only
            // the View's input pipeline can observe - the ViewModel has no access to routed
            // input events.
            PreviewKeyDown += OnPreviewKeyDown;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not ControlsViewModel viewModel)
            {
                return;
            }

            var capturing = viewModel.KeyBindings.FirstOrDefault(b => b.IsCapturing);
            if (capturing is null)
            {
                return;
            }

            // Alt-combinations surface the actual key via SystemKey, with e.Key reporting the
            // sentinel Key.System instead - unwrap it so e.g. Alt+F resolves to Key.F, not
            // Key.System.
            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            viewModel.CompleteCapture(capturing, key, Keyboard.Modifiers);
            e.Handled = true;
        }
    }
}
