using System.Globalization;
using System.Windows.Data;
using MaterialDesignThemes.Wpf;
using RouteJumper.Models;

namespace RouteJumper.Converters
{
    /// <summary>
    /// Maps a row's icon state to the PackIconKind used to render it:
    ///   InProgress -> Play (right-pointing triangle)
    ///   Complete   -> Check (tick)
    ///   None       -> Play (unused; the icon is hidden via RowIconToVisibilityConverter)
    /// </summary>
    public class IconToPackIconKindConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value switch
            {
                RowIcon.Complete => PackIconKind.Check,
                _ => PackIconKind.Play
            };
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
