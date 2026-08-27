using System.Net.Http;
using System.Text.Json;
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
        private const string GithubApiRepoUrl = "https://api.github.com/repos/haggisandchips/RouteJumper";
        private const string Category = "Update";

        private static readonly HttpClient GithubApiHttpClient = CreateGithubApiHttpClient();

        public static async Task CheckForUpdatesAsync()
        {
            await CheckForUpdatesCoreAsync();
        }

        public static Task<UpdateCheckOutcome> CheckForUpdatesManuallyAsync() => CheckForUpdatesCoreAsync();

        /// <summary>The installed app's release number and its actual GitHub release date/time (e.g. "Version 1.2.2 (15 Aug 2026 09:05)"), or a fallback label for an unpackaged build - shown on the About dialog.</summary>
        public static async Task<string> GetCurrentVersionDisplayAsync()
        {
            try
            {
                var manager = new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: false));
                if (!manager.IsInstalled || manager.CurrentVersion is not { } version)
                {
                    return "Development build";
                }

                var releaseDate = await GetReleaseDateAsync(version.ToString());
                return releaseDate is { } date
                    ? $"Version {version} ({date.ToLocalTime():dd MMM yyyy HH:mm})"
                    : $"Version {version}";
            }
            catch (Exception)
            {
                return "Development build";
            }
        }

        /// <summary>
        /// Looks up this version's own GitHub release (tagged v{version} per .github/workflows/release.yml)
        /// via the public REST API and reads its real <c>published_at</c> instant - the only way to get
        /// the actual release date/time, since Velopack itself exposes no such metadata and a locally
        /// installed file's own timestamp reflects install/extraction, not release (can be off by weeks
        /// or more). Best-effort: this is the app's fourth direct outbound HTTP integration
        /// (specs/non-functional.md §9),
        /// used only on demand when the About dialog opens, never blocking anything else.
        /// </summary>
        private static async Task<DateTimeOffset?> GetReleaseDateAsync(string version)
        {
            try
            {
                using var response = await GithubApiHttpClient.GetAsync($"{GithubApiRepoUrl}/releases/tags/v{version}");
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                using var stream = await response.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(stream);
                return doc.RootElement.TryGetProperty("published_at", out var publishedAt)
                    ? publishedAt.GetDateTimeOffset()
                    : null;
            }
            catch (Exception ex)
            {
                Log.Warn(Category, "Failed to fetch release date from GitHub.", ex);
                return null;
            }
        }

        private static HttpClient CreateGithubApiHttpClient()
        {
            // LoggingHttpMessageHandler wraps the real transport so every GitHub API request/response
            // is logged the same way as EDSM/Spansh/Companion (category "Update", matching this
            // service's other log lines). The GitHub API rejects requests with no User-Agent.
            var client = new HttpClient(new LoggingHttpMessageHandler(new HttpClientHandler(), Category)) { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("RouteJumper");
            return client;
        }

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
