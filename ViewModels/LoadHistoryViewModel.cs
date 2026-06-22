using OverWatchELD.Services.ATS;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace OverWatchELD.ViewModels
{
    public sealed class LoadHistoryViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<LoadHistoryRow> Rows { get; } = new();

        private LoadHistoryRow? _selectedRow;
        public LoadHistoryRow? SelectedRow
        {
            get => _selectedRow;
            set { _selectedRow = value; OnPropertyChanged(); }
        }

        private string _statusText = "Ready.";
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value ?? ""; OnPropertyChanged(); }
        }

        public ICommand RefreshCommand { get; }
        public ICommand ClearIndividualHistoryCommand { get; }

        public LoadHistoryViewModel()
        {
            RefreshCommand = new RelayCommand(_ => Refresh());
            ClearIndividualHistoryCommand = new RelayCommand(_ => ClearIndividualHistory());
            Refresh();
        }

        public void Refresh()
        {
            Rows.Clear();

            foreach (var load in CompanyLoadRequestStore.LoadAll())
            {
                Rows.Add(new LoadHistoryRow
                {
                    Type = "Company",
                    LoadNumber = load.LoadNumber,
                    Route = load.RouteDisplay,
                    Cargo = load.Cargo,
                    Trailer = load.Trailer,
                    Driver = load.AssignedDriver,
                    Truck = load.AssignedTruck,
                    Miles = load.Miles,
                    WeightLbs = load.WeightLbs,
                    Status = load.Status,
                    Source = "Company Loads",
                    SavePath = "",
                    Message = load.Notes,
                    CreatedUtc = load.CreatedUtc
                });
            }

            foreach (var load in IndividualLoadHistoryStore.LoadAll())
            {
                Rows.Add(new LoadHistoryRow
                {
                    Type = "Individual",
                    LoadNumber = load.LoadNumber,
                    Route = load.RouteDisplay,
                    Cargo = load.Cargo,
                    Trailer = load.Trailer,
                    Driver = load.AssignedDriver,
                    Truck = load.AssignedTruck,
                    Miles = load.Miles,
                    WeightLbs = load.WeightLbs,
                    Status = load.Status,
                    Source = load.SourceMod,
                    SavePath = load.SavePath,
                    Message = load.Message,
                    CreatedUtc = load.CreatedUtc
                });
            }

            var ordered = Rows.OrderByDescending(x => x.CreatedUtc).ToList();
            Rows.Clear();

            foreach (var row in ordered)
                Rows.Add(row);

            StatusText = $"{Rows.Count} saved/created load history record(s).";
        }

        private void ClearIndividualHistory()
        {
            IndividualLoadHistoryStore.Clear();
            Refresh();
            StatusText = "Individual load history cleared. Company loads were kept.";
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

    public sealed class LoadHistoryRow
    {
        public string Type { get; set; } = "";
        public string LoadNumber { get; set; } = "";
        public string Route { get; set; } = "";
        public string Cargo { get; set; } = "";
        public string Trailer { get; set; } = "";
        public string Driver { get; set; } = "";
        public string Truck { get; set; } = "";
        public int Miles { get; set; }
        public int WeightLbs { get; set; }
        public string Status { get; set; } = "";
        public string Source { get; set; } = "";
        public string SavePath { get; set; } = "";
        public string Message { get; set; } = "";
        public DateTime CreatedUtc { get; set; }

        public string CreatedDisplay => CreatedUtc.ToLocalTime().ToString("MM/dd/yyyy h:mm tt");
    }
}
