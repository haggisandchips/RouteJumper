using System.Globalization;
using System.Windows.Data;
using MaterialDesignThemes.Wpf;

namespace RouteJumper.Converters
{
    /// <summary>Converts SpeechAnnouncer.Muted to the main window's mute button icon.</summary>
    public class MuteIconConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is true ? PackIconKind.VolumeOff : PackIconKind.VolumeHigh;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
