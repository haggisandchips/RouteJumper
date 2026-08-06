using System.Globalization;
using System.Windows.Data;
using RouteJumper.Models;

namespace RouteJumper.Converters
{
    /// <summary>
    /// Renders the row icon as a coloured glyph:
    ///   InProgress -> green right-pointing triangle (▶)
    ///   Complete   -> green tick (✔)
    ///   None       -> nothing
    /// </summary>
    public class IconToGlyphConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value switch
            {
                RowIcon.InProgress => "\u25B6", // ▶
                RowIcon.Complete => "\u2714",   // ✔
                _ => string.Empty
            };
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
