using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using OverWatchELD.Models;
using OverWatchELD.Services;

namespace OverWatchELD.ViewModels
{
    public sealed class VtcGaragePurchaseWindowViewModel : ViewModelBase
    {
        private VtcGarage? _selectedGarage;

        public ObservableCollection<VtcGarage> AvailableGarages { get; } = new();

        public ICollectionView GarageView { get; }

        public VtcGarage? SelectedGarage
        {
            get => _selectedGarage;
            set
            {
                _selectedGarage = value;
                OnPropertyChanged();
            }
        }

        public ICommand RefreshCommand { get; }
        public ICommand PurchaseCommand { get; }

        public VtcGaragePurchaseWindowViewModel()
        {
            GarageView = CollectionViewSource.GetDefaultView(AvailableGarages);

            GarageView.GroupDescriptions.Add(
                new PropertyGroupDescription(nameof(VtcGarage.State)));

            RefreshCommand = new RelayCommand(_ => LoadGarages());

            PurchaseCommand = new RelayCommand(x =>
                PurchaseGarage(x as VtcGarage));

            LoadGarages();
        }

        private void LoadGarages()
        {
            AvailableGarages.Clear();

            var owned = VtcGarageStore.Load();

            var atsGarages = VtcGarageDiscoveryService.LoadAtsGarages();

            foreach (var garage in atsGarages
                         .Where(g => !owned.Any(o =>
                             string.Equals(o.Id, g.Id, StringComparison.OrdinalIgnoreCase) &&
                             o.IsOwned))
                         .OrderBy(g => g.State)
                         .ThenBy(g => g.CityName))
            {
                garage.ApplyEconomyDefaults();

                AvailableGarages.Add(garage);
            }
        }

        private void PurchaseGarage(VtcGarage? garage)
        {
            if (garage == null)
                return;

            var all = VtcGarageStore.Load();

            var match = all.FirstOrDefault(g =>
                string.Equals(g.Id,
                              garage.Id,
                              StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                match = garage;
                all.Add(match);
            }


            var alreadyOwnsGarage = all.Any(g => g.IsOwned);

            match.IsOwned = true;
            match.IsHomeGarage = !alreadyOwnsGarage;
            match.Size = garage.Size;

            match.TruckCapacity =
                VtcGarage.CapacityForSize(match.Size);

            match.ApplyEconomyDefaults();

            if (match.IsHomeGarage)
            {
                match.PurchasePrice = 0;
            }

            VtcGarageStore.Save(all);

            MessageBox.Show(
    match.IsHomeGarage
        ? $"{match.CityName} is now your free VTC home garage."
        : $"Purchased {match.CityName} garage for ${match.PurchasePrice:N0}.",
    "Garage Purchased");

            LoadGarages();
        }
    }
}