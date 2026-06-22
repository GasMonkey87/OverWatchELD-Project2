using System;
using System.Windows.Input;

namespace OverWatchELD.ViewModels
{
    /// <summary>
    /// Simple WPF ICommand implementation.
    /// Supports:
    ///  - RelayCommand(Action)
    ///  - RelayCommand(Action&lt;object?&gt;)
    ///  - RelayCommand&lt;T&gt;(Action&lt;T?&gt;)
    /// </summary>
    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        // Parameterless command
        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            if (execute == null) throw new ArgumentNullException(nameof(execute));
            _execute = _ => execute();
            if (canExecute != null)
                _canExecute = _ => canExecute();
        }

        // Object-parameter command
        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

        public void Execute(object? parameter) => _execute(parameter);
    }

    public sealed class RelayCommand<T> : ICommand
    {
        private readonly Action<T?> _execute;
        private readonly Func<T?, bool>? _canExecute;

        public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object? parameter)
        {
            if (_canExecute == null) return true;

            // Handle null param for value types safely
            if (parameter == null)
                return _canExecute(default);

            if (parameter is T t)
                return _canExecute(t);

            // If binding passes something unexpected, don't crash
            return _canExecute((T?)parameter);
        }

        public void Execute(object? parameter)
        {
            if (parameter == null)
            {
                _execute(default);
                return;
            }

            if (parameter is T t)
            {
                _execute(t);
                return;
            }

            _execute((T?)parameter);
        }
    }
}