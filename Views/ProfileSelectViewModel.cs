using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace OverWatchELD.Views
{
    public sealed class ProfileSelectViewModel
    {
        public ObservableCollection<string> Profiles { get; } = new();

        public string? SelectedProfile { get; set; }

        // ✅ Fix CS0053: expose as ICommand (public interface), not RelayCommand
        public ICommand CreateCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand ContinueCommand { get; }

        public ProfileSelectViewModel()
        {
            CreateCommand = new SimpleCommand(_ => CreateProfile());
            DeleteCommand = new SimpleCommand(_ => DeleteProfile());
            ContinueCommand = new SimpleCommand(_ => Continue());
        }

        private void CreateProfile()
        {
            // Minimal placeholder (you can wire to your real profile system later)
            var name = "Profile " + DateTime.Now.ToString("HHmmss");
            Profiles.Add(name);
            SelectedProfile = name;
        }

        private void DeleteProfile()
        {
            if (string.IsNullOrWhiteSpace(SelectedProfile)) return;

            Profiles.Remove(SelectedProfile);
            SelectedProfile = Profiles.Count > 0 ? Profiles[0] : null;
        }

        private void Continue()
        {
            // If you already navigate elsewhere, wire it there.
            // For now: just close dialog if this VM is used in one.
        }

        /// <summary>
        /// Tiny ICommand implementation to avoid toolkit dependency issues.
        /// </summary>
        private sealed class SimpleCommand : ICommand
        {
            private readonly Action<object?> _execute;
            private readonly Func<object?, bool>? _canExecute;

            public SimpleCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
            {
                _execute = execute;
                _canExecute = canExecute;
            }

            public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

            public void Execute(object? parameter) => _execute(parameter);

            public event EventHandler? CanExecuteChanged
            {
                add { CommandManager.RequerySuggested += value; }
                remove { CommandManager.RequerySuggested -= value; }
            }

            public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
        }
    }
}
