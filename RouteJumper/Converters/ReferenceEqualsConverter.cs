using System.Globalization;
using System.Windows.Data;

namespace RouteJumper.Converters
{
    /// <summary>Multi-value converter: true when both bound values are the same object reference - used to highlight a selected item in a list bound by reference (e.g. the Controls tab's selected instance).</summary>
    public class ReferenceEqualsConverter : IMultiValueConverter
    {
        public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) =>
            values.Length == 2 && values[0] != null && ReferenceEquals(values[0], values[1]);

        public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
