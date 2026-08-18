using System.IO;
using Velopack;
using Velopack.Sources;

namespace RouteJumper.Services
{
    /// <summary>
    /// Resolves the %LocalAppData% folder ED:FC Auto Pilot stores its own state in - shared by
    /// <see cref="AppConfigStore"/> and <see cref="AppSettingsStore"/> so both land in the same
    /// place. Appends ".Dev" whenever this isn't a real Velopack install (dotnet run/F5, or a
    /// local publish) - see <see cref="UpdateManager.IsInstalled"/> - so local development
    /// (including database schema changes) never touches the real installed app's data.
    /// </summary>
    internal static class AppPaths
    {
        private const string AppName = "EDFCAutoPilot";
        private const string RepoUrl = "https://github.com/haggisandchips/RouteJumper";

        public static string DataDirectory { get; } = ComputeDataDirectory();

        /// <summary>Where FileLogSink writes its date-stamped log files - see that class.</summary>
        public static string LogsDirectory { get; } = Path.Combine(DataDirectory, "Logs");

        private static string ComputeDataDirectory()
        {
            var isInstalled = new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: false)).IsInstalled;
            var name = isInstalled ? AppName : AppName + ".Dev";

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), name);
        }
    }
}
