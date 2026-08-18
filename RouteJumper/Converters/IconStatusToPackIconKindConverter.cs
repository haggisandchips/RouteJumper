using System.Globalization;
using System.Windows.Data;
using MaterialDesignThemes.Wpf;
using RouteJumper.Models;

namespace RouteJumper.Converters
{
    /// <summary>
    /// Maps a row's icon state and current status to the PackIconKind used to render it:
    ///   Complete                          -> Check (tick)
    ///   InProgress, Status == "Targeted" -> Target (Ship mode only - see RowEventKind.Targeted)
    ///   InProgress, Status == "Plotting" -> ProgressClock
    ///   InProgress, Status == "Plotted"  -> Hourglass
    ///   InProgress, Status == "Jumping"  -> RocketLaunch
    ///   InProgress, any other status     -> Play (right-pointing triangle)
    ///   None                             -> Play (unused; the icon is hidden via RowIconToVisibilityConverter)
    /// Takes both values because the per-status icon swaps depend on Status, not just Icon.
    /// </summary>
    public class IconStatusToPackIconKindConverter : IMultiValueConverter
    {
        public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
        {
            var icon = values.Length > 0 ? values[0] as RowIcon? : null;
            var status = values.Length > 1 ? values[1] as string : null;

            if (icon == RowIcon.Complete)
            {
                return PackIconKind.Check;
            }

            if (icon == RowIcon.InProgress)
            {
                switch (status)
                {
                    case "Targeted":
                        return PackIconKind.Target;
                    case "Plotting":
                        return PackIconKind.ProgressClock;
                    case "Plotted":
                        return PackIconKind.Hourglass;
                    case "Jumping":
                        return PackIconKind.RocketLaunch;
                }
            }

            return PackIconKind.Play;
        }

        public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
