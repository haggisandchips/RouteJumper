using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RouteJumper.Converters
{
    /// <summary>
    /// Given a Thickness (typically a DataGridCell's own Padding) and a comma-separated
    /// ConverterParameter naming which sides to counteract (any of "Left", "Top", "Right",
    /// "Bottom"), returns a Thickness with just those sides negated and the rest 0 - applied as
    /// an element's own Margin, this lets it visually reach the cell's true edge on the sides
    /// that matter without hardcoding whatever pixel padding the active Material theme happens
    /// to use (see RouteView.xaml's Status column, whose progress bar needs to reach the cell's
    /// left/right/bottom edges despite sitting inside a padded cell).
    /// </summary>
    public class NegatePaddingConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not Thickness padding)
            {
                return new Thickness(0);
            }

            var sides = (parameter as string)?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                ?? Array.Empty<string>();

            double Side(string name, double amount) =>
                sides.Contains(name, StringComparer.OrdinalIgnoreCase) ? -amount : 0;

            return new Thickness(
                Side("Left", padding.Left),
                Side("Top", padding.Top),
                Side("Right", padding.Right),
                Side("Bottom", padding.Bottom));
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
