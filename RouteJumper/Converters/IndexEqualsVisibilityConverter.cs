using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RouteJumper.Converters
{
    /// <summary>
    /// Visible when the bound <c>int</c> (e.g. a TabControl's own SelectedIndex) equals
    /// <see cref="ConvertBack"/>'s own ConverterParameter (also an <c>int</c>, supplied as a
    /// string in XAML); Collapsed otherwise - used to show a tab-specific footnote only while
    /// that tab is the one currently selected (Integrations &gt; Spansh's own dialog, SPEC §4.12).
    /// </summary>
    public class IndexEqualsVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not int index || parameter is not string parameterText || !int.TryParse(parameterText, out var target))
            {
                return Visibility.Collapsed;
            }

            return index == target ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
