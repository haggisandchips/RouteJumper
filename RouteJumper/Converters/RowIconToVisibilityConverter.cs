using System.Globalization;
using System.Windows;
using System.Windows.Data;
using RouteJumper.Models;

namespace RouteJumper.Converters
{
    /// <summary>
    /// Hides the row icon while its state is None; visible for InProgress/Complete. Uses
    /// Hidden, not Collapsed - Collapsed removes the icon from layout entirely, which let the
    /// DataGrid shrink that row's height by the icon's size while no icon was shown, causing a
    /// visible row-resize flicker every time a row's icon appeared/disappeared. Hidden keeps
    /// the layout space reserved even while invisible, so row height stays constant.
    /// </summary>
    public class RowIconToVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is RowIcon and not RowIcon.None ? Visibility.Visible : Visibility.Hidden;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
