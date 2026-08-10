using System.Diagnostics;

namespace RouteJumper.Common
{
    /// <summary>
    /// Opens a URL in the user's default browser - <c>UseShellExecute</c> is required here since
    /// .NET no longer treats a URL as directly executable the way it did pre-Core.
    /// </summary>
    public static class BrowserLauncher
    {
        public static void Open(string url)
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }
}
