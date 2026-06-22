using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using OverWatchELD.Services;
using OverWatchELD.Services.LoadBoard;

namespace OverWatchELD.Views
{
    public partial class LoadBoardWindow : Window
    {
        public LoadBoardWindow()
        {
            InitializeComponent();
            DriverDropdownService.Bind(DriverBox, includeUnassigned: true);
        }

        private void LoadBoardWindow_Loaded(object sender, RoutedEventArgs e) => RefreshLoads();
        private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshLoads();
        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void RefreshLoads()
        {
            LoadGrid.ItemsSource = null;
            LoadGrid.ItemsSource = LoadBoardStore.LoadAll().OrderByDescending(x => x.UpdatedUtc).ToList();
            StatusText.Text = $"Loaded {LoadGrid.Items.Count} loads.";
        }

        private void LoadGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LoadGrid.SelectedItem is not LoadBoardLoad load) return;
            LoadNumberBox.Text = load.LoadNumber;
            StatusBox.Text = load.Status;
            DriverDropdownService.Select(DriverBox, load.DriverName);
            TruckBox.Text = load.TruckName;
            TrailerBox.Text = load.TrailerName;
            CommodityBox.Text = load.Commodity;
            WeightBox.Text = load.WeightLbs > 0 ? load.WeightLbs.ToString("0", CultureInfo.InvariantCulture) : "";
            ShipperBox.Text = load.ShipperName;
            ShipperCityBox.Text = load.ShipperCity;
            ReceiverBox.Text = load.ReceiverName;
            ReceiverCityBox.Text = load.ReceiverCity;
        }

        private void NewManualLoad_Click(object sender, RoutedEventArgs e)
        {
            LoadNumberBox.Text = LoadBoardStore.GenerateLoadNumber();
            StatusBox.Text = "At Shipper";
            DriverDropdownService.Select(DriverBox, "Unassigned"); TruckBox.Text = ""; TrailerBox.Text = ""; CommodityBox.Text = ""; WeightBox.Text = "";
            ShipperBox.Text = ""; ShipperCityBox.Text = ""; ReceiverBox.Text = ""; ReceiverCityBox.Text = "";
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var load = ReadForm();
            LoadBoardStore.Upsert(load);
            LoadBoardFleetLinkService.ApplyLoadNumber(load.DriverName, load.DriverDiscordId, load.LoadNumber);
            RefreshLoads();
            StatusText.Text = $"Saved load {load.LoadNumber}.";
        }

        private void OpenBol_Click(object sender, RoutedEventArgs e)
        {
            var load = ReadForm();
            LoadBoardStore.Upsert(load);
            new LoadBoardBolWindow(load.LoadNumber) { Owner = this }.Show();
        }

        private async void BolComplete_Click(object sender, RoutedEventArgs e)
        {
            var load = ReadForm();
            load.Status = "BOL Complete";
            load.BolCompletedUtc = DateTimeOffset.UtcNow;
            LoadBoardStore.Upsert(load);
            LoadBoardFleetLinkService.ApplyLoadNumber(load.DriverName, load.DriverDiscordId, load.LoadNumber);
            await PostBolAsync(load, "BOL Complete");
            RefreshLoads();
            StatusText.Text = $"BOL sent for load {load.LoadNumber}.";
        }

        private async void Delivered_Click(object sender, RoutedEventArgs e)
        {
            var load = ReadForm();
            load.Status = "Delivered";
            load.DeliveredUtc = DateTimeOffset.UtcNow;
            LoadBoardStore.Upsert(load);
            LoadBoardFleetLinkService.ClearLoadNumber(load.DriverName, load.DriverDiscordId, load.LoadNumber);
            await PostBolAsync(load, "Delivered");
            RefreshLoads();
        }

        private static Task PostBolAsync(LoadBoardLoad load, string status)
            => BolDiscordOnlyService.Shared.PostAsync(load.LoadNumber, load.DriverName, load.TruckName, load.Commodity, load.WeightLbs, load.ShipperCity, load.ReceiverCity, status);

        private LoadBoardLoad ReadForm()
        {
            var loadNumber = (LoadNumberBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(loadNumber)) loadNumber = LoadBoardStore.GenerateLoadNumber();

            _ = double.TryParse((WeightBox.Text ?? "").Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var weight);

            var existing = LoadBoardStore.LoadAll().FirstOrDefault(x => string.Equals(x.LoadNumber, loadNumber, StringComparison.OrdinalIgnoreCase));

            return new LoadBoardLoad
            {
                LoadNumber = loadNumber,
                Status = string.IsNullOrWhiteSpace(StatusBox.Text) ? "At Shipper" : StatusBox.Text.Trim(),
                DriverName = DriverDropdownService.SelectedName(DriverBox, "Unassigned"),
                DriverDiscordId = DriverDropdownService.SelectedDiscordId(DriverBox),
                TruckName = (TruckBox.Text ?? "").Trim(),
                TrailerName = (TrailerBox.Text ?? "").Trim(),
                Commodity = (CommodityBox.Text ?? "").Trim(),
                WeightLbs = weight,
                RevenueUsd = existing?.RevenueUsd ?? 0m,
                RevenueSource = existing?.RevenueSource ?? "",
                RevenueCapturedUtc = existing?.RevenueCapturedUtc,
                ShipperName = (ShipperBox.Text ?? "").Trim(),
                ShipperCity = (ShipperCityBox.Text ?? "").Trim(),
                ReceiverName = (ReceiverBox.Text ?? "").Trim(),
                ReceiverCity = (ReceiverCityBox.Text ?? "").Trim(),
                CurrentLocation = (ShipperCityBox.Text ?? "").Trim(),
                UpdatedUtc = DateTimeOffset.UtcNow
            };
        }
    }
}
