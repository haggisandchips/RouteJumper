using System.Windows;
using RouteJumper.Common;
using RouteJumper.Services;

namespace RouteJumper.Views
{
    /// <summary>Modal "About" dialog (File &gt; About) - static informational content, so (like PreferencesWindow) it needs no ViewModel of its own.</summary>
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            _ = LoadVersionAsync();
        }

        private async Task LoadVersionAsync()
        {
            VersionText.Text = await UpdateService.GetCurrentVersionDisplayAsync();
        }

        private void OnRepositoryLinkClick(object sender, RoutedEventArgs e) =>
            BrowserLauncher.Open("https://github.com/haggisandchips/RouteJumper");

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    }
}
