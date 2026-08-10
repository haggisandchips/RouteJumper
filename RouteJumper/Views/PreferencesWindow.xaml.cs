using System.Windows;
using RouteJumper.Services;

namespace RouteJumper.Views
{
    /// <summary>Modal dialog (File > Preferences) for spoken-announcement voice/volume - see SpeechAnnouncer, which this binds to directly as its own ViewModel.</summary>
    public partial class PreferencesWindow : Window
    {
        public PreferencesWindow(SpeechAnnouncer speechAnnouncer)
        {
            InitializeComponent();
            DataContext = speechAnnouncer;
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    }
}
