using System.Windows.Input;

namespace RouteJumper.Common
{
    /// <summary>
    /// ICommand implementation for async handlers (e.g. Refresh, which does file/process I/O
    /// and must not block the UI thread). Mirrors RelayCommand's shape; callers are expected to
    /// track their own "is busy" state (see RolesViewModel.IsRefreshing) and feed it into
    /// canExecute, the same way RouteViewModel does for IsRunning.
    /// </summary>
    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool>? _canExecute;

        public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

        public async void Execute(object? parameter) => await _execute();

        public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
    }
}
