using System.Windows;
using RouteJumper.Services;

namespace RouteJumper.Views
{
    /// <summary>
    /// Modal dialog (File > Preferences) for spoken-announcement voice/volume - see
    /// SpeechAnnouncer, which this binds to directly as its own ViewModel (DataContext) - plus
    /// the "Updates" section's own UpdatePreferences, exposed as a plain property here and bound
    /// via RelativeSource rather than folding it into DataContext, so every existing
    /// SpeechAnnouncer-bound control in the XAML is left untouched.
    /// </summary>
    public partial class PreferencesWindow : Window
    {
        public PreferencesWindow(SpeechAnnouncer speechAnnouncer, UpdatePreferences updatePreferences)
        {
            InitializeComponent();
            DataContext = speechAnnouncer;
            UpdatePreferences = updatePreferences;
        }

        public UpdatePreferences UpdatePreferences { get; }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    }
}
