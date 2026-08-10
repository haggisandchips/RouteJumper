using RouteJumper.ViewModels;

namespace RouteJumper.Services
{
    /// <summary>
    /// Abstraction over <see cref="EliteInstanceScanner"/> so RolesViewModel's own
    /// internally-triggered rescans (see RolesViewModel.ToggleCaptain/RefreshAsync) can be given
    /// deterministic results in tests, without depending on real EliteDangerous64.exe processes.
    /// </summary>
    public interface IEliteInstanceScanner
    {
        Task<IReadOnlyList<EliteInstanceViewModel>> ScanAsync();
    }
}
