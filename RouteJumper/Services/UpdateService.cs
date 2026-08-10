using Velopack;
using Velopack.Sources;

namespace RouteJumper.Services
{
    /// <summary>
    /// Silent, non-blocking self-update check against the project's GitHub Releases page.
    /// A newer version (if any) is downloaded in the background and applied via
    /// <c>WaitExitThenApplyUpdates</c> - i.e. on the *next* normal app exit, never
    /// interrupting whatever the current session is doing (an active route, Auto Pilot,
    /// a macro mid-playback). No-ops entirely when running an unpackaged build
    /// (<c>dotnet run</c>/F5), since <see cref="UpdateManager.IsInstalled"/> is only true
    /// for a real Velopack-installed copy.
    /// </summary>
    public static class UpdateService
    {
        private const string RepoUrl = "https://github.com/haggisandchips/RouteJumper";

        public static async Task CheckForUpdatesAsync()
        {
            try
            {
                var manager = new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: false));
                if (!manager.IsInstalled)
                {
                    return;
                }

                var newVersion = await manager.CheckForUpdatesAsync();
                if (newVersion == null)
                {
                    return;
                }

                await manager.DownloadUpdatesAsync(newVersion);
                manager.WaitExitThenApplyUpdates(newVersion);
            }
            catch (Exception)
            {
                // Update checks are best-effort against an external service (offline, GitHub
                // outage, rate limiting, etc.) - never let a failed check affect the running app.
            }
        }
    }
}
