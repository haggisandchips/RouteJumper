using RouteJumper.Services;
using RouteJumper.ViewModels;

namespace RouteJumper.Tests.TestSupport
{
    /// <summary>
    /// Test double for IEliteInstanceScanner: returns a settable, already-completed result list
    /// instead of scanning real OS processes. RolesViewModel's own internally-triggered rescans
    /// (its constructor, and ToggleCaptain's post-assignment refresh) run entirely synchronously
    /// against an already-completed Task, so tests can assign roles to fabricated instances
    /// without a genuine background scan racing the test's own assertions on a different thread
    /// and wiping them out from under it - the real EliteInstanceScanner always finds zero
    /// EliteDangerous64.exe processes in a dev/CI environment, so any such race would otherwise
    /// always resolve the same (losing) way.
    /// </summary>
    internal sealed class FakeEliteInstanceScanner : IEliteInstanceScanner
    {
        public IReadOnlyList<EliteInstanceViewModel> Results { get; set; } = Array.Empty<EliteInstanceViewModel>();

        public Task<IReadOnlyList<EliteInstanceViewModel>> ScanAsync() => Task.FromResult(Results);
    }
}
