using RouteJumper.Services.Logging;
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

        private const string Category = "Update";

        private static async Task<UpdateCheckOutcome> CheckForUpdatesCoreAsync()
        {
            Log.Info(Category, "Checking for updates.");

            try
            {
                var manager = new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: false));
                if (!manager.IsInstalled)
                {
                    Log.Info(Category, "Skipped - not a packaged (Velopack-installed) build.");
                    return UpdateCheckOutcome.NotInstalled;
                }

                var newVersion = await manager.CheckForUpdatesAsync();
                if (newVersion == null)
                {
                    Log.Info(Category, "Already up to date.");
                    return UpdateCheckOutcome.UpToDate;
                }

                Log.Info(Category, "A newer version is available - downloading.");
                await manager.DownloadUpdatesAsync(newVersion);
                Log.Info(Category, "Update downloaded - will be applied on next exit.");
                manager.WaitExitThenApplyUpdates(newVersion);
                return UpdateCheckOutcome.UpdateDownloaded;
            }
            catch (Exception ex)
            {
                // Update checks are best-effort against an external service (offline, GitHub
                // outage, rate limiting, etc.) - never let a failed check affect the running app.
                Log.Warn(Category, "Update check failed.", ex);
                return UpdateCheckOutcome.Error;
            }
        }
    }
}
