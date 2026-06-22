using OverWatchELD.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace OverWatchELD.ViewModels
{
    public sealed class ExpenseReceiptsViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<TelemetryExpenseReceipt> Receipts { get; } = new();

        private TelemetryExpenseReceipt? _selectedReceipt;
        public TelemetryExpenseReceipt? SelectedReceipt
        {
            get => _selectedReceipt;
            set { _selectedReceipt = value; OnPropertyChanged(); }
        }

        private string _selectedFilter = "All";
        public string SelectedFilter
        {
            get => _selectedFilter;
            set { _selectedFilter = string.IsNullOrWhiteSpace(value) ? "All" : value; OnPropertyChanged(); Refresh(); }
        }

        public ObservableCollection<string> Filters { get; } = new()
        {
            "All", "Fuel", "Toll", "Ticket"
        };

        private string _statusText = "Ready.";
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value ?? ""; OnPropertyChanged(); }
        }

        public ICommand RefreshCommand { get; }
        public ICommand ClearCommand { get; }

        public ExpenseReceiptsViewModel()
        {
            RefreshCommand = new RelayCommand(_ => Refresh());
            ClearCommand = new RelayCommand(_ => Clear());
            Refresh();
        }

        public void Refresh()
        {
            Receipts.Clear();

            var rows = TelemetryExpenseReceiptStore.LoadAll();

            if (!string.Equals(SelectedFilter, "All", StringComparison.OrdinalIgnoreCase))
                rows = rows.Where(x => string.Equals(x.EventType, SelectedFilter, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var receipt in rows.OrderByDescending(x => x.CreatedUtc))
                Receipts.Add(receipt);

            StatusText = $"{Receipts.Count} receipt(s) loaded.";
        }

        private void Clear()
        {
            TelemetryExpenseReceiptStore.Clear();
            Refresh();
            StatusText = "Receipts cleared.";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private sealed class RelayCommand : ICommand
        {
            private readonly Action<object?> _execute;
            private readonly Predicate<object?>? _canExecute;

            public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
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
        }
    }
}
