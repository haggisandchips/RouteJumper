using System.Globalization;
using System.Windows.Data;

namespace RouteJumper.Converters
{
    /// <summary>Converts IsAutoPilotRunning to the Auto Pilot button's label.</summary>
    public class AutoPilotLabelConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is true ? "Stop" : "Auto Pilot";

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
