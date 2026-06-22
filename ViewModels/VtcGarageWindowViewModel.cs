using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using OverWatchELD.Models;
using OverWatchELD.Services;

namespace OverWatchELD.ViewModels
{
    public sealed class VtcGarageWindowViewModel : ViewModelBase
    {
        private VtcGarage? _selectedGarage;
        private string _truckNumberToAssign = "";
        private string _syncStatus = "";

        public ObservableCollection<VtcGarage> Garages { get; }

        public VtcGarage? SelectedGarage
        {
            get => _selectedGarage;
            set
            {
                _selectedGarage = value;
                OnPropertyChanged();
            }
        }

        public string TruckNumberToAssign
        {
            get => _truckNumberToAssign;
            set
            {
                _truckNumberToAssign = value;
                OnPropertyChanged();
            }
        }

        public string SyncStatus
        {
            get => _syncStatus;
            set
            {
                _syncStatus = value;
                OnPropertyChanged();
            }
        }

        public ICommand SaveCommand { get; }
        public ICommand AddTruckCommand { get; }
        public ICommand RemoveTruckCommand { get; }
        public ICommand SetSmallCommand { get; }
        public ICommand SetMediumCommand { get; }
        public ICommand SetLargeCommand { get; }
        public ICommand SyncToMapCommand { get; }

        public ICommand PurchaseGarageCommand { get; }
        public ICommand SellGarageCommand { get; }
        public ICommand UpgradeGarageCommand { get; }
        public ICommand AddFuelStationCommand { get; }

        public VtcGarageWindowViewModel()
        {
            Garages = new ObservableCollection<VtcGarage>(VtcGarageStore.Load());

            foreach (var garage in Garages)
            {
                garage.ApplyEconomyDefaults();
            }

            SelectedGarage = Garages.FirstOrDefault();

            SaveCommand = new RelayCommand(_ => Save(showMessage: true));
            AddTruckCommand = new RelayCommand(_ => AddTruck());
            RemoveTruckCommand = new RelayCommand(x => RemoveTruck(x?.ToString()));

            SetSmallCommand = new RelayCommand(_ => SetSize("Small"));
            SetMediumCommand = new RelayCommand(_ => SetSize("Medium"));
            SetLargeCommand = new RelayCommand(_ => SetSize("Large"));

            SyncToMapCommand = new RelayCommand(async _ => await SyncToMapAsync());

            PurchaseGarageCommand = new RelayCommand(_ => PurchaseGarage());
            SellGarageCommand = new RelayCommand(_ => SellGarage());
            UpgradeGarageCommand = new RelayCommand(_ => UpgradeGarage());
            AddFuelStationCommand = new RelayCommand(_ => AddFuelStation());

            AvailableFleetTrucks = new ObservableCollection<string>(
    new OverWatchELD.Services.Fleet.FleetCommandStore()
        .LoadAll()
        .Select(t => t.TruckNumber)
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct()
        .OrderBy(x => x));
        }

        private void Save(bool showMessage = false)
        {
            foreach (var garage in Garages)
            {
                garage.AssignedTruckNumbers ??= new System.Collections.Generic.List<string>();
                garage.TruckCapacity = VtcGarage.CapacityForSize(garage.Size);
                garage.ApplyEconomyDefaults();
            }

            VtcGarageStore.Save(Garages.ToList());

            RefreshSelectedGarage();

            SyncStatus = "Garages saved locally.";

            if (showMessage)
                MessageBox.Show("VTC garages saved.", "Garages");
        }

        private void AddTruck()
        {
            if (SelectedGarage == null)
                return;

            var truck = (SelectedFleetTruck ?? TruckNumberToAssign ?? "").Trim();

            if (string.IsNullOrWhiteSpace(truck))
            {
                MessageBox.Show("Enter a truck number first.", "Garage");
                return;
            }

            if (!SelectedGarage.IsOwned)
            {
                MessageBox.Show("This garage must be marked owned before trucks can be assigned.", "Garage");
                return;
            }

            SelectedGarage.AssignedTruckNumbers ??= new System.Collections.Generic.List<string>();

            if (SelectedGarage.AssignedTruckNumbers.Any(x =>
                    string.Equals(x, truck, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("That truck is already assigned to this garage.", "Garage");
                return;
            }

            if (SelectedGarage.AssignedTruckNumbers.Count >= SelectedGarage.TruckCapacity)
            {
                MessageBox.Show(
                    $"This {SelectedGarage.Size} garage is full. Capacity: {SelectedGarage.TruckCapacity} trucks.",
                    "Garage Full");
                return;
            }

            SelectedGarage.AssignedTruckNumbers.Add(truck);
            TruckNumberToAssign = "";

            RefreshSelectedGarage();
            Save();
        }

        public void ReloadGarages()
        {
            Garages.Clear();

            foreach (var g in VtcGarageStore.Load())
                Garages.Add(g);

            SelectedGarage = Garages.FirstOrDefault();
        }

        private void RemoveTruck(string? truck)
        {
            if (SelectedGarage == null || string.IsNullOrWhiteSpace(truck))
                return;

            SelectedGarage.AssignedTruckNumbers ??= new System.Collections.Generic.List<string>();
            SelectedGarage.AssignedTruckNumbers.Remove(truck);

            RefreshSelectedGarage();
            Save();
        }

        public ObservableCollection<string> AvailableFleetTrucks { get; }

        private string? _selectedFleetTruck;

        public string? SelectedFleetTruck
        {
            get => _selectedFleetTruck;
            set
            {
                _selectedFleetTruck = value;
                OnPropertyChanged();
            }
        }



        private void SetSize(string size)
        {
            if (SelectedGarage == null)
                return;

            SelectedGarage.Size = size;
            SelectedGarage.TruckCapacity = VtcGarage.CapacityForSize(size);
            SelectedGarage.ApplyEconomyDefaults();

            if (SelectedGarage.AssignedTruckNumbers.Count > SelectedGarage.TruckCapacity)
            {
                MessageBox.Show(
                    $"This garage now only supports {SelectedGarage.TruckCapacity} trucks. Remove extra assigned trucks.",
                    "Garage Capacity");
            }

            RefreshSelectedGarage();
            Save();
        }

        private void PurchaseGarage()
        {
            if (SelectedGarage == null)
                return;

            SelectedGarage.IsOwned = true;
            SelectedGarage.ApplyEconomyDefaults();

            RefreshSelectedGarage();
            Save();

            MessageBox.Show(
                $"Purchased {SelectedGarage.CityName} garage for ${SelectedGarage.PurchasePrice:N0}.",
                "Garage Purchased");
        }

        private void SellGarage()
        {
            if (SelectedGarage == null)
                return;

            SelectedGarage.IsOwned = false;
            SelectedGarage.HasFuelStation = false;
            SelectedGarage.AssignedTruckNumbers.Clear();

            RefreshSelectedGarage();
            Save();

            MessageBox.Show("Garage sold and assigned trucks removed.", "Garage Sold");
        }

        private void UpgradeGarage()
        {
            if (SelectedGarage == null)
                return;

            if (!SelectedGarage.IsOwned)
            {
                MessageBox.Show("Purchase the garage before upgrading it.", "Garage");
                return;
            }

            var current = (SelectedGarage.Size ?? "Small").Trim().ToLowerInvariant();

            if (current == "small")
            {
                SelectedGarage.Size = "Medium";
            }
            else if (current == "medium" || current == "med")
            {
                SelectedGarage.Size = "Large";
            }
            else
            {
                MessageBox.Show("Garage is already Large.", "Garage");
                return;
            }

            SelectedGarage.TruckCapacity = VtcGarage.CapacityForSize(SelectedGarage.Size);
            SelectedGarage.ApplyEconomyDefaults();

            RefreshSelectedGarage();
            Save();

            MessageBox.Show($"Garage upgraded to {SelectedGarage.Size}.", "Garage Upgraded");
        }

        private void AddFuelStation()
        {
            if (SelectedGarage == null)
                return;

            if (!SelectedGarage.IsOwned)
            {
                MessageBox.Show("Purchase the garage first.", "Garage");
                return;
            }

            if (SelectedGarage.HasFuelStation)
            {
                MessageBox.Show("This garage already has a fuel station.", "Garage Upgrade");
                return;
            }

            SelectedGarage.HasFuelStation = true;

            RefreshSelectedGarage();
            Save();

            MessageBox.Show("Fuel station added.", "Garage Upgrade");
        }

        private async Task SyncToMapAsync()
        {
            Save();

            try
            {
                var config = VtcConfigService.Load();

                var botUrl = "https://overwatcheld.up.railway.app";

                var guildId =
                    config?.GuildId ??
                    "";

                var result = await VtcGarageSyncService.SyncAsync(botUrl, guildId);

                SyncStatus = result.Message;

                MessageBox.Show(result.Message, result.Ok ? "Garage Sync" : "Garage Sync Failed");
            }
            catch (Exception ex)
            {
                SyncStatus = ex.Message;
                MessageBox.Show(ex.Message, "Garage Sync Failed");
            }
        }

        private void RefreshSelectedGarage()
        {
            if (SelectedGarage == null)
                return;

            SelectedGarage.OnPropertyChanged(nameof(VtcGarage.IsOwned));
            SelectedGarage.OnPropertyChanged(nameof(VtcGarage.Size));
            SelectedGarage.OnPropertyChanged(nameof(VtcGarage.TruckCapacity));
            SelectedGarage.OnPropertyChanged(nameof(VtcGarage.SlotDisplay));
            SelectedGarage.OnPropertyChanged(nameof(VtcGarage.EconomyDisplay));
            SelectedGarage.OnPropertyChanged(nameof(VtcGarage.PurchasePrice));
            SelectedGarage.OnPropertyChanged(nameof(VtcGarage.DailyUpkeep));
            SelectedGarage.OnPropertyChanged(nameof(VtcGarage.RepairBays));
            SelectedGarage.OnPropertyChanged(nameof(VtcGarage.HasFuelStation));
            SelectedGarage.OnPropertyChanged(nameof(VtcGarage.GarageIncomeBonusPercent));

            OnPropertyChanged(nameof(SelectedGarage));
        }
    }
}