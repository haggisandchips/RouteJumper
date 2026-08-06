using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RouteJumper.Converters
{
    /// <summary>
    /// Converts bool to Visibility. Pass ConverterParameter="Invert" to flip the result
    /// (used so the same IsSaved flag can show the text box when false and the table when true).
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var boolValue = value is bool b && b;

            if (parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase))
            {
                boolValue = !boolValue;
            }

            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
