using System.Windows.Input;

namespace RouteJumper.Common
{
    /// <summary>
    /// Standard ICommand implementation used to bind Button clicks (Save/Cancel/Start/Stop)
    /// to methods on the ViewModels.
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
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

        public void Execute(object? parameter) => _execute();

        /// <summary>
        /// Forces WPF to re-query CanExecute immediately (used after we programmatically
        /// change state such as IsRunning) rather than waiting for the next UI event.
        /// </summary>
        public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
    }
}
