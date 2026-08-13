using Velopack;
using Velopack.Sources;

namespace RouteJumper.Services
{
    /// <summary>Result of a manually-triggered update check (File &gt; Check for Updates) - the silent startup check has no need for this, since it never reports anything back to the user.</summary>
    public enum UpdateCheckOutcome
    {
        NotInstalled,
        UpToDate,
        UpdateDownloaded,
        Error,
    }

    /// <summary>
    /// Self-update checks against the project's GitHub Releases page, via Velopack. The silent
    /// startup check (<see cref="CheckForUpdatesAsync"/>) and the manual, user-triggered one
    /// (<see cref="CheckForUpdatesManuallyAsync"/>) share the same download-and-apply-on-next-exit
    /// behaviour - the only difference is that the manual one reports what happened, since it was
    /// an explicit request rather than a background check. Both no-op entirely when running an
    /// unpackaged build (<c>dotnet run</c>/F5), since <see cref="UpdateManager.IsInstalled"/> is
    /// only true for a real Velopack-installed copy.
    /// </summary>
    public static class UpdateService
    {
        private const string RepoUrl = "https://github.com/haggisandchips/RouteJumper";

        public static async Task CheckForUpdatesAsync()
        {
            await CheckForUpdatesCoreAsync();
        }

        public static Task<UpdateCheckOutcome> CheckForUpdatesManuallyAsync() => CheckForUpdatesCoreAsync();

        /// <summary>The installed app's release number (e.g. "Version 1.2.2"), or a fallback label for an unpackaged build - shown on the About dialog.</summary>
        public static string GetCurrentVersionDisplay()
        {
            try
            {
                var manager = new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: false));
                return manager.IsInstalled && manager.CurrentVersion is { } version
                    ? $"Version {version}"
                    : "Development build";
            }
            catch (Exception)
            {
                return "Development build";
            }
        }

        private static async Task<UpdateCheckOutcome> CheckForUpdatesCoreAsync()
        {
            try
            {
                var manager = new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: false));
                if (!manager.IsInstalled)
                {
                    return UpdateCheckOutcome.NotInstalled;
                }

                var newVersion = await manager.CheckForUpdatesAsync();
                if (newVersion == null)
                {
                    return UpdateCheckOutcome.UpToDate;
                }

                await manager.DownloadUpdatesAsync(newVersion);
                manager.WaitExitThenApplyUpdates(newVersion);
                return UpdateCheckOutcome.UpdateDownloaded;
            }
            catch (Exception)
            {
                // Update checks are best-effort against an external service (offline, GitHub
                // outage, rate limiting, etc.) - never let a failed check affect the running app.
                return UpdateCheckOutcome.Error;
            }
        }
    }
}
