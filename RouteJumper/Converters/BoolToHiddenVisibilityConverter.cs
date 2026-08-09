using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RouteJumper.Converters
{
    /// <summary>
    /// Converts bool to Visibility, using Hidden (not Collapsed) for false - keeps the element's
    /// layout space reserved even while invisible, so a row's height doesn't visibly shift as it
    /// toggles. Same "Hidden, not Collapsed" convention RowIconToVisibilityConverter already uses
    /// for the icon column, and for the same reason - here, the Status column's countdown
    /// progress bar (§4.4): without this, a row currently showing the bar would render taller
    /// than one that isn't.
    /// </summary>
    public class BoolToHiddenVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is true ? Visibility.Visible : Visibility.Hidden;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
