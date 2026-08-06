using System.Globalization;
using System.Windows;
using System.Windows.Data;
using RouteJumper.Models;

namespace RouteJumper.Converters
{
    /// <summary>Hides the row icon while its state is None; visible for InProgress/Complete.</summary>
    public class RowIconToVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is RowIcon and not RowIcon.None ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
