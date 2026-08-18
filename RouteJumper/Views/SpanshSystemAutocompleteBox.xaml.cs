using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RouteJumper.Models;
using RouteJumper.ViewModels;

namespace RouteJumper.Views
{
    /// <summary>
    /// Live-search autocomplete field for one Spansh system pick (the Spansh menu's
    /// Source/Destination fields) - a plain TextBox plus a manually-driven Popup/ListBox, not an
    /// editable ComboBox. Confirmed live (via UI Automation) that a stock editable ComboBox bound
    /// the way this needs (Text two-way, SelectedItem two-way, an async-populated ItemsSource)
    /// caused two real usability problems, both stemming from behaviour WPF's ComboBox owns
    /// internally and can't be told not to do:
    /// 1. The instant the typed text exactly matched one of the freshly-arrived suggestions (the
    ///    common case - the CMDR's own in-progress typing is often itself a real system name),
    ///    ComboBox auto-selected that item and highlighted the *entire* text box, so the very next
    ///    keystroke replaced everything already typed instead of extending it.
    /// 2. Every arrow key press changes a ComboBox's own SelectedItem live (not just Enter/click),
    ///    which - because SpanshSystemPickerViewModel.Selected's own setter closes the dropdown on
    ///    any change - closed the suggestions list after the very first Down/Up press, even though
    ///    the CMDR was still mid-browse.
    ///
    /// This control avoids both by never binding SelectedItem to anything ComboBox-like: typing
    /// only ever touches Picker.Query; arrow keys inside SuggestionsListBox only ever move that
    /// ListBox's own local, cosmetic highlight; Picker.Selected changes only on an explicit commit
    /// (Enter, or a click) - see CommitSelection.
    /// </summary>
    public partial class SpanshSystemAutocompleteBox : UserControl
    {
        public static readonly DependencyProperty PickerProperty = DependencyProperty.Register(
            nameof(Picker), typeof(SpanshSystemPickerViewModel), typeof(SpanshSystemAutocompleteBox));

        public static readonly DependencyProperty HintProperty = DependencyProperty.Register(
            nameof(Hint), typeof(string), typeof(SpanshSystemAutocompleteBox), new PropertyMetadata(string.Empty));

        public SpanshSystemAutocompleteBox()
        {
            InitializeComponent();
        }

        public SpanshSystemPickerViewModel? Picker
        {
            get => (SpanshSystemPickerViewModel?)GetValue(PickerProperty);
            set => SetValue(PickerProperty, value);
        }

        public string Hint
        {
            get => (string)GetValue(HintProperty);
            set => SetValue(HintProperty, value);
        }

        /// <summary>Moves keyboard focus into the query text box - called by SpanshImportWindow on its own Loaded event to focus the Source field as soon as the dialog opens.</summary>
        public void FocusQueryBox() => Keyboard.Focus(QueryTextBox);

        /// <summary>
        /// Down opens the popup (if there's anything to show) and moves keyboard focus into
        /// SuggestionsListBox, highlighting its first item - from there, the ListBox's own default
        /// arrow-key handling takes over (native ListBox behaviour, no code needed). Escape closes
        /// the popup without committing anything, leaving Query exactly as typed.
        /// </summary>
        private void OnQueryTextBoxPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Picker is null)
            {
                return;
            }

            if (e.Key == Key.Down)
            {
                if (SuggestionsListBox.Items.Count == 0)
                {
                    return;
                }

                Picker.IsDropDownOpen = true;
                SuggestionsListBox.SelectedIndex = 0;
                SuggestionsListBox.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && Picker.IsDropDownOpen)
            {
                Picker.IsDropDownOpen = false;
                e.Handled = true;
            }
        }

        /// <summary>Enter commits whichever item is currently highlighted; Escape closes the popup and returns focus to the text box, discarding the browse without changing Query/Selected.</summary>
        private void OnSuggestionsListBoxPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (SuggestionsListBox.SelectedItem is SpanshSystemSuggestion suggestion)
                {
                    CommitSelection(suggestion);
                }

                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                if (Picker != null)
                {
                    Picker.IsDropDownOpen = false;
                }

                QueryTextBox.Focus();
                e.Handled = true;
            }
        }

        /// <summary>
        /// Resolves the clicked item via the visual tree (not SuggestionsListBox.SelectedItem,
        /// which a plain click already updates by this point anyway) - PreviewMouseLeftButtonUp,
        /// not MouseUp/Click, so this fires before the Popup's own focus-loss handling could
        /// otherwise interfere.
        /// </summary>
        private void OnSuggestionMouseUp(object sender, MouseButtonEventArgs e)
        {
            var container = FindAncestorListBoxItem(e.OriginalSource as DependencyObject);
            if (container?.DataContext is SpanshSystemSuggestion suggestion)
            {
                CommitSelection(suggestion);
            }
        }

        private static ListBoxItem? FindAncestorListBoxItem(DependencyObject? element)
        {
            while (element != null && element is not ListBoxItem)
            {
                element = VisualTreeHelper.GetParent(element);
            }

            return element as ListBoxItem;
        }

        private void CommitSelection(SpanshSystemSuggestion suggestion)
        {
            if (Picker is null)
            {
                return;
            }

            Picker.Selected = suggestion;
            QueryTextBox.Focus();
            QueryTextBox.CaretIndex = QueryTextBox.Text.Length;
        }

        /// <summary>
        /// Closes the popup (without committing anything) once keyboard focus leaves this control
        /// entirely - e.g. Tab or a click to Calculate/the other field. Necessary because the
        /// Popup itself uses StaysOpen="True" (see this control's own XAML comment) precisely so
        /// that moving focus *into* SuggestionsListBox for arrow-key browsing doesn't auto-close
        /// it; this is the corresponding "actually left" case that still needs to close it. Walks
        /// up from the new focus target checking only for QueryTextBox/SuggestionsListBox
        /// specifically, rather than a generic visual-ancestry check - the Popup's own content can
        /// render in a separate top-level window (AllowsTransparency="True"), which a plain
        /// Visual.IsAncestorOf(this) check would not correctly see as "part of this control".
        /// </summary>
        private void OnControlPreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (IsWithinThisControl(e.NewFocus as DependencyObject))
            {
                return;
            }

            if (Picker != null)
            {
                Picker.IsDropDownOpen = false;
            }
        }

        /// <summary>
        /// Walks logical-parent-then-visual-parent, the same as before, but the new focus target
        /// isn't always a Visual - e.g. a Hyperlink (a FrameworkContentElement, part of the
        /// logical/content tree only) elsewhere in the same window. VisualTreeHelper.GetParent
        /// throws InvalidOperationException for anything that isn't a Visual/Visual3D, so a
        /// FrameworkContentElement must walk via its own logical Parent exclusively, never falling
        /// through to VisualTreeHelper.
        /// </summary>
        private bool IsWithinThisControl(DependencyObject? element)
        {
            while (element != null)
            {
                if (ReferenceEquals(element, QueryTextBox) || ReferenceEquals(element, SuggestionsListBox))
                {
                    return true;
                }

                element = element switch
                {
                    FrameworkElement { Parent: { } parent } => parent,
                    FrameworkContentElement { Parent: { } parent } => parent,
                    Visual or Visual3D => VisualTreeHelper.GetParent(element),
                    _ => null,
                };
            }

            return false;
        }
    }
}
