using System.Windows;
using RouteJumper.Services;
using Velopack;

namespace RouteJumper
{
    public partial class App : Application
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // Must run before any Application/window is created - handles Velopack's
            // own install/update/uninstall command-line hooks and exits immediately
            // for those cases without ever showing the UI.
            VelopackApp.Build().Run();

            var app = new App();
            app.InitializeComponent();
            app.Run();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            _ = UpdateService.CheckForUpdatesAsync();
        }
    }
}
