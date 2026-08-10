using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace RouteJumper.Behaviors
{
    /// <summary>
    /// Attached behavior that invokes a Command when the element it's attached to is clicked -
    /// for elements (e.g. a DataGridRow via DataGrid.RowStyle) that have no Command property of
    /// their own, so the whole element becomes clickable without code-behind. Same view-layer-
    /// glue role as the Converters/ classes, just for input instead of value conversion.
    /// </summary>
    public static class ClickCommandBehavior
    {
        public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
            "Command", typeof(ICommand), typeof(ClickCommandBehavior), new PropertyMetadata(null, OnCommandChanged));

        public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.RegisterAttached(
            "CommandParameter", typeof(object), typeof(ClickCommandBehavior), new PropertyMetadata(null));

        public static void SetCommand(DependencyObject element, ICommand? value) => element.SetValue(CommandProperty, value);

        public static ICommand? GetCommand(DependencyObject element) => (ICommand?)element.GetValue(CommandProperty);

        public static void SetCommandParameter(DependencyObject element, object? value) => element.SetValue(CommandParameterProperty, value);

        public static object? GetCommandParameter(DependencyObject element) => element.GetValue(CommandParameterProperty);

        private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not UIElement element)
            {
                return;
            }

            element.PreviewMouseLeftButtonUp -= OnElementClicked;
            if (e.NewValue is ICommand)
            {
                element.PreviewMouseLeftButtonUp += OnElementClicked;
            }
        }

        private static void OnElementClicked(object sender, MouseButtonEventArgs e)
        {
            if (sender is not DependencyObject element)
            {
                return;
            }

            // PreviewMouseLeftButtonUp tunnels from the root down through every ancestor on its
            // way to whatever was actually clicked - so an interactive control (e.g. an Edit/
            // Delete icon button) nested inside the element this behavior is attached to would
            // otherwise also re-trigger this command. Skip it in that case, so the element's
            // command only fires for clicks on the "plain" parts of it.
            if (OriginatesFromButton(e.OriginalSource as DependencyObject, element))
            {
                return;
            }

            var command = GetCommand(element);
            var parameter = GetCommandParameter(element);
            if (command != null && command.CanExecute(parameter))
            {
                command.Execute(parameter);
            }
        }

        private static bool OriginatesFromButton(DependencyObject? source, DependencyObject stopAt)
        {
            for (var node = source; node != null && !ReferenceEquals(node, stopAt); node = VisualTreeHelper.GetParent(node))
            {
                if (node is ButtonBase)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
