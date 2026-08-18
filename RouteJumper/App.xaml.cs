using System.Windows;
using RouteJumper.Services;
using RouteJumper.Services.Logging;
using Velopack;

namespace RouteJumper
{
    public partial class App : Application
    {
        private static FileLogSink? _logSink;

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

            // Initialized before anything else - every other service/ViewModel constructed
            // during startup may log. AppConfigStore is re-read (LogSettings) periodically by
            // FileLogSink itself, not just once here, so hand-editing the retention/size keys in
            // routejumper.conf while the app is running still takes effect (see FileLogSink).
            var config = new AppConfigStore();
            _logSink = new FileLogSink(AppPaths.DataDirectory, () => new LogSettings(config.LogRetentionDays, config.LogMaxFileSizeMb, config.LogMaxTotalSizeMb));
            Log.Initialize(_logSink);
            Log.Info("App", "ED:FC Auto Pilot starting up.");

            Exit += OnExit;

            // A fresh AppSettingsStore, not a shared instance - MainViewModel's own (also
            // AppSettingsStore-backed) UpdatePreferences hasn't been constructed yet at this
            // point (MainWindow/its DataContext don't exist until base.OnStartup shows the
            // StartupUri window) - both just read/write the same underlying SQLite row, so
            // there's nothing to keep in sync between the two instances.
            if (new UpdatePreferences(new AppSettingsStore()).AutoCheckEnabled)
            {
                _ = UpdateService.CheckForUpdatesAsync();
            }
            else
            {
                Log.Info("Update", "Silent startup check skipped - disabled in Preferences.");
            }
        }

        private void OnExit(object? sender, ExitEventArgs e)
        {
            Log.Info("App", "ED:FC Auto Pilot exiting.");
            _logSink?.Dispose();
        }
    }
}
