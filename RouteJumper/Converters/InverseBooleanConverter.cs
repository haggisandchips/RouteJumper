using System.Globalization;
using System.Windows.Data;

namespace RouteJumper.Converters
{
    /// <summary>
    /// Inverts a bool - both ways (ConvertBack also inverts), so it can drive a two-way binding.
    /// Used to bind the "Fleet Carrier" mode-switch RadioButton's IsChecked to the inverse of
    /// IsShipMode, alongside the "Ship" RadioButton's own direct (uninverted) binding to it - the
    /// same underlying property, from both sides of one two-option choice-chip group.
    /// </summary>
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            !(value is bool b && b);

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            !(value is bool b && b);
    }
}
