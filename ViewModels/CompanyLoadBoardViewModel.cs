using OverWatchELD.Services;
using OverWatchELD.Services.ATS;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace OverWatchELD.ViewModels
{
    public sealed class CompanyLoadBoardViewModel : INotifyPropertyChanged
    {
        private readonly AtsMileageCalculatorService _mileage = new();

        public ObservableCollection<CompanyLoadRequest> CompanyLoads { get; } = new();
        public ObservableCollection<AtsCompanyOption> PickupCompanies { get; } = new();
        public ObservableCollection<AtsCompanyOption> DropOffCompanies { get; } = new();
        public ObservableCollection<string> CargoOptions { get; } = new();
        public ObservableCollection<string> TrailerOptions { get; } = new();
        public ObservableCollection<string> DriverOptions { get; } = new();
        public ObservableCollection<string> TruckOptions { get; } = new();

        private CompanyLoadRequest? _selectedLoad;
        public CompanyLoadRequest? SelectedLoad { get => _selectedLoad; set { _selectedLoad = value; OnPropertyChanged(); } }

        private AtsCompanyOption? _selectedPickupCompany;
        public AtsCompanyOption? SelectedPickupCompany { get => _selectedPickupCompany; set { _selectedPickupCompany = value; OnPropertyChanged(); AutoMiles(); } }

        private AtsCompanyOption? _selectedDropOffCompany;
        public AtsCompanyOption? SelectedDropOffCompany { get => _selectedDropOffCompany; set { _selectedDropOffCompany = value; OnPropertyChanged(); AutoMiles(); } }

        private string _loadNumber = "CL-" + DateTime.Now.ToString("MMddHHmmss");
        public string LoadNumber { get => _loadNumber; set { _loadNumber = value ?? ""; OnPropertyChanged(); } }

        private string _selectedCargo = "General Goods";
        public string SelectedCargo { get => _selectedCargo; set { _selectedCargo = value ?? ""; OnPropertyChanged(); } }

        private string _selectedTrailer = "Dry Van";
        public string SelectedTrailer { get => _selectedTrailer; set { _selectedTrailer = value ?? ""; OnPropertyChanged(); } }

        private int _weightLbs = 42000;
        public int WeightLbs { get => _weightLbs; set { _weightLbs = value; OnPropertyChanged(); } }

        private int _miles = 500;
        public int Miles { get => _miles; set { _miles = value; OnPropertyChanged(); } }

        private string _assignedDriver = "Unassigned";
        public string AssignedDriver { get => _assignedDriver; set { _assignedDriver = value ?? ""; OnPropertyChanged(); } }

        private string _assignedTruck = "Any";
        public string AssignedTruck { get => _assignedTruck; set { _assignedTruck = value ?? ""; OnPropertyChanged(); } }

        private string _notes = "";
        public string Notes { get => _notes; set { _notes = value ?? ""; OnPropertyChanged(); } }

        private string _statusText = "Ready.";
        public string StatusText { get => _statusText; set { _statusText = value ?? ""; OnPropertyChanged(); } }

        public ICommand RefreshCommand { get; }
        public ICommand CreateCompanyLoadCommand { get; }
        public ICommand DispatchSelectedCommand { get; }
        public ICommand DeleteSelectedCommand { get; }

        public CompanyLoadBoardViewModel()
        {
            RefreshCommand = new RelayCommand(_ => Refresh());
            CreateCompanyLoadCommand = new RelayCommand(_ => CreateCompanyLoad());
            DispatchSelectedCommand = new RelayCommand(_ => DispatchSelected(), _ => SelectedLoad != null);
            DeleteSelectedCommand = new RelayCommand(_ => DeleteSelected(), _ => SelectedLoad != null);
            Refresh();
        }

        public void Refresh()
        {
            CompanyLoads.Clear();
            foreach (var load in CompanyLoadRequestStore.LoadAll().OrderByDescending(x => x.CreatedUtc))
                CompanyLoads.Add(load);

            LoadCreateOptions();
            StatusText = $"{CompanyLoads.Count} company load(s) ready.";
        }

        private void LoadCreateOptions()
        {
            PickupCompanies.Clear(); DropOffCompanies.Clear(); CargoOptions.Clear(); TrailerOptions.Clear(); DriverOptions.Clear(); TruckOptions.Clear();

            try
            {
                var scan = new AtsCleanContentScannerService().Scan();
                foreach (var c in scan.Companies.Where(x => !string.IsNullOrWhiteSpace(x.Name) && !string.IsNullOrWhiteSpace(x.City) && !string.IsNullOrWhiteSpace(x.State)).GroupBy(x => $"{x.Name}|{x.City}|{x.State}", StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderBy(x => x.State).ThenBy(x => x.City).ThenBy(x => x.Name))
                {
                    PickupCompanies.Add(c);
                    DropOffCompanies.Add(c);
                }

                foreach (var c in scan.Cargo.Select(x => CleanOptionName(x.Name, x.Token)).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
                    CargoOptions.Add(c);

                foreach (var t in scan.Trailers.Select(x => CleanOptionName(x.Name, x.Token)).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
                    TrailerOptions.Add(t);
            }
            catch { }

            if (CargoOptions.Count == 0) { CargoOptions.Add("General Goods"); CargoOptions.Add("Lumber"); CargoOptions.Add("Refrigerated Goods"); CargoOptions.Add("Fuel"); CargoOptions.Add("Large Machinery"); }
            if (TrailerOptions.Count == 0) { TrailerOptions.Add("Dry Van"); TrailerOptions.Add("Flatbed"); TrailerOptions.Add("Reefer"); TrailerOptions.Add("Lowboy"); TrailerOptions.Add("Tanker"); }

            DriverOptions.Add("Unassigned");
            TruckOptions.Add("Any");

            try
            {
                foreach (var profile in DriverProfileMasterStore.LoadAll())
                {
                    var driver = FirstNonBlank(profile.DisplayName, profile.DiscordName, profile.DiscordUserId);
                    if (!string.IsNullOrWhiteSpace(driver) && !DriverOptions.Contains(driver)) DriverOptions.Add(driver);

                    foreach (var truck in profile.ConnectedTrucks ?? new System.Collections.Generic.List<DriverTruckLink>())
                    {
                        var truckName = FirstNonBlank(truck.TruckNumber, truck.TruckName, truck.Plate);
                        if (!string.IsNullOrWhiteSpace(truckName) && !TruckOptions.Contains(truckName)) TruckOptions.Add(truckName);
                    }
                }
            }
            catch { }

            SelectedPickupCompany ??= PickupCompanies.FirstOrDefault();
            SelectedDropOffCompany ??= DropOffCompanies.Skip(1).FirstOrDefault() ?? DropOffCompanies.FirstOrDefault();
            SelectedCargo = CargoOptions.FirstOrDefault() ?? SelectedCargo;
            SelectedTrailer = TrailerOptions.FirstOrDefault() ?? SelectedTrailer;
            AssignedDriver = DriverOptions.FirstOrDefault(x => x != "Unassigned") ?? "Unassigned";
            AssignedTruck = TruckOptions.FirstOrDefault() ?? "Any";
            AutoMiles();
        }

        private void AutoMiles()
        {
            try { if (SelectedPickupCompany != null && SelectedDropOffCompany != null) { var m = _mileage.CalculateMiles(SelectedPickupCompany, SelectedDropOffCompany); if (m > 0) Miles = m; } }
            catch { }
        }

        private void CreateCompanyLoad()
        {
            if (SelectedPickupCompany == null || SelectedDropOffCompany == null) { StatusText = "Select pickup and drop-off companies first."; return; }
            var load = new CompanyLoadRequest
            {
                LoadNumber = string.IsNullOrWhiteSpace(LoadNumber) ? DispatchService.NextLoadNumber() : LoadNumber.Trim(),
                PickupCompany = SelectedPickupCompany.Name, PickupCity = SelectedPickupCompany.City, PickupState = SelectedPickupCompany.State,
                DropOffCompany = SelectedDropOffCompany.Name, DropOffCity = SelectedDropOffCompany.City, DropOffState = SelectedDropOffCompany.State,
                Cargo = SelectedCargo, Trailer = SelectedTrailer, WeightLbs = WeightLbs <= 0 ? 42000 : WeightLbs, Miles = Miles <= 0 ? 500 : Miles,
                AssignedDriver = string.IsNullOrWhiteSpace(AssignedDriver) ? "Unassigned" : AssignedDriver, AssignedTruck = string.IsNullOrWhiteSpace(AssignedTruck) ? "Any" : AssignedTruck,
                CreatedBy = EldCurrentUserService.SafeDisplayName(), Notes = Notes, Status = "Pending"
            };
            CompanyLoadRequestStore.AddOrUpdate(load);
            StatusText = $"Company load {load.LoadNumber} created for {load.AssignedDriver}.";
            LoadNumber = "CL-" + DateTime.Now.ToString("MMddHHmmss"); Notes = ""; Refresh();
        }

        private void DispatchSelected()
        {
            if (SelectedLoad == null) return;
            try { var job = CompanyLoadRequestStore.ToDispatchJob(SelectedLoad); DispatchService.AddJob(job); SelectedLoad.Status = "Dispatched"; CompanyLoadRequestStore.AddOrUpdate(SelectedLoad); StatusText = $"Dispatched {SelectedLoad.LoadNumber} to {SelectedLoad.AssignedDriver}."; Refresh(); }
            catch (Exception ex) { StatusText = "Dispatch failed: " + ex.Message; }
        }

        private void DeleteSelected()
        {
            if (SelectedLoad == null) return;
            CompanyLoadRequestStore.Delete(SelectedLoad.Id); StatusText = "Company load deleted."; Refresh();
        }

        private static string CleanOptionName(string? displayName, string? token)
        {
            var text = string.IsNullOrWhiteSpace(displayName) ? token ?? "" : displayName.Trim();
            return text.Replace("🟢", "").Replace("🟡", "").Replace("🔴", "").Replace("⚪", "").Replace("Installed / Not Verified -", "", StringComparison.OrdinalIgnoreCase).Replace("Installed Not Verified -", "", StringComparison.OrdinalIgnoreCase).Replace("Verified -", "", StringComparison.OrdinalIgnoreCase).Replace("Active -", "", StringComparison.OrdinalIgnoreCase).Replace("@@", "").Trim();
        }

        private static string FirstNonBlank(params string?[] values) { foreach (var value in values) if (!string.IsNullOrWhiteSpace(value)) return value.Trim(); return ""; }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private sealed class RelayCommand : ICommand
        {
            private readonly Action<object?> _execute; private readonly Predicate<object?>? _canExecute;
            public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null) { _execute = execute; _canExecute = canExecute; }
            public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
            public void Execute(object? parameter) => _execute(parameter);
            public event EventHandler? CanExecuteChanged { add { CommandManager.RequerySuggested += value; } remove { CommandManager.RequerySuggested -= value; } }
        }
    }
}
