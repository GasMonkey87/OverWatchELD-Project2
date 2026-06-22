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
    public partial class LoadBoardBolWindow : Window
    {
        private readonly string _loadNumber;

        public LoadBoardBolWindow(string loadNumber)
        {
            _loadNumber = (loadNumber ?? "").Trim();
            InitializeComponent();
            DriverDropdownService.Bind(DriverBox, includeUnassigned: true);
        }

        private void LoadBoardBolWindow_Loaded(object sender, RoutedEventArgs e)
        {
            DateTimeBox.Text = DateTime.Now.ToString("MM/dd/yyyy HH:mm");

            var load = LoadBoardStore.LoadAll().FirstOrDefault(x => Same(x.LoadNumber, _loadNumber));
            if (load == null)
            {
                LoadNumberBox.Text = string.IsNullOrWhiteSpace(_loadNumber) ? LoadBoardStore.GenerateLoadNumber() : _loadNumber;
                ImportTelemetry(saveAfterImport: true, showMessage: false);
                return;
            }

            LoadNumberBox.Text = load.LoadNumber;
            DriverDropdownService.Select(DriverBox, load.DriverName);
            TruckBox.Text = load.TruckName;
            TrailerBox.Text = load.TrailerName;
            CommodityBox.Text = load.Commodity;
            WeightBox.Text = load.WeightLbs > 0 ? load.WeightLbs.ToString("0", CultureInfo.InvariantCulture) : "";
            ShipperBox.Text = load.ShipperName;
            ShipperCityBox.Text = load.ShipperCity;
            ReceiverBox.Text = load.ReceiverName;
            ReceiverCityBox.Text = load.ReceiverCity;
            ImportTelemetry(saveAfterImport: true, showMessage: false);
        }

        private void ImportTelemetry_Click(object sender, RoutedEventArgs e) => ImportTelemetry(saveAfterImport: true, showMessage: true);
        private void SaveDraft_Click(object sender, RoutedEventArgs e)
        {
            var load = SaveDraft("BOL Draft");
            MessageBox.Show($"Saved telemetry load to history.\n\nLoad #: {load.LoadNumber}", "Load Board BOL", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private async void CompleteBol_Click(object sender, RoutedEventArgs e) => await CompleteBolAsync();
        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void ImportTelemetry(bool saveAfterImport, bool showMessage)
        {
            try
            {
                var app = Application.Current as App;
                var snap = app?.Telemetry?.LastSnapshot;
                if (snap == null)
                {
                    if (showMessage)
                        MessageBox.Show("No telemetry snapshot found. Make sure ATS and telemetry are running.", "Load Board BOL", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (DriverBox.SelectedItem == null)
                    DriverDropdownService.Select(DriverBox, FirstNonEmpty(snap.DriverName, CurrentDriverName()));

                SetIfEmpty(TruckBox, FirstNonEmpty(snap.TruckName, snap.TruckMakeModel, "ATS Truck"));
                SetIfEmpty(TrailerBox, snap.TrailerName);
                SetIfEmpty(CommodityBox, GetBestCargoName(snap));

                var weight = GetBestWeight(snap);
                if (weight > 0)
                    SetIfEmpty(WeightBox, weight.ToString("0", CultureInfo.InvariantCulture));

                SetIfEmpty(ShipperBox, snap.SourceCompany);
                SetIfEmpty(ShipperCityBox, FirstNonEmpty(snap.SourceCity, JoinLocation(snap.City, snap.State)));
                SetIfEmpty(ReceiverBox, snap.DestinationCompany);
                SetIfEmpty(ReceiverCityBox, snap.DestinationCity);

                if (saveAfterImport)
                {
                    var load = SaveDraft("BOL Draft");
                    ApplyTelemetryRevenue(load, snap);
                    LoadBoardStore.Upsert(load);
                    SyncLoadToDispatchHistory(load, snap);

                    if (showMessage)
                        MessageBox.Show($"Telemetry imported and saved to load history.\n\nLoad #: {load.LoadNumber}", "Load Board BOL", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                if (showMessage)
                    MessageBox.Show("Telemetry import failed.\n\n" + ex.Message, "Load Board BOL", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private LoadBoardLoad SaveDraft(string status)
        {
            var loadNumber = (LoadNumberBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(loadNumber))
            {
                loadNumber = LoadBoardStore.GenerateLoadNumber();
                LoadNumberBox.Text = loadNumber;
            }

            _ = double.TryParse((WeightBox.Text ?? "").Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var weight);

            var existing = LoadBoardStore.LoadAll().FirstOrDefault(x => Same(x.LoadNumber, loadNumber));
            var load = existing ?? new LoadBoardLoad { LoadNumber = loadNumber, CreatedUtc = DateTimeOffset.UtcNow };

            load.Status = status;
            load.DriverName = DriverDropdownService.SelectedName(DriverBox, FirstNonEmpty(CurrentDriverName(), "Unassigned"));
            load.DriverDiscordId = DriverDropdownService.SelectedDiscordId(DriverBox);
            load.TruckName = (TruckBox.Text ?? "").Trim();
            load.TrailerName = (TrailerBox.Text ?? "").Trim();
            load.Commodity = (CommodityBox.Text ?? "").Trim();
            load.WeightLbs = weight;
            load.ShipperName = (ShipperBox.Text ?? "").Trim();
            load.ShipperCity = (ShipperCityBox.Text ?? "").Trim();
            load.ReceiverName = (ReceiverBox.Text ?? "").Trim();
            load.ReceiverCity = (ReceiverCityBox.Text ?? "").Trim();
            load.CurrentLocation = FirstNonEmpty(load.CurrentLocation, load.ShipperCity);
            load.BolCompletedUtc = status.Equals("BOL Complete", StringComparison.OrdinalIgnoreCase) ? DateTimeOffset.UtcNow : load.BolCompletedUtc;

            LoadBoardStore.Upsert(load);
            LoadBoardFleetLinkService.ApplyLoadNumber(load.DriverName, load.DriverDiscordId, load.LoadNumber);
            SyncLoadToDispatchHistory(load, null);
            return load;
        }

        private async Task CompleteBolAsync()
        {
            try
            {
                var load = SaveDraft("BOL Complete");

                var result = await BolDiscordOnlyService.Shared.PostAsync(
                    load.LoadNumber,
                    load.DriverName,
                    load.TruckName,
                    load.Commodity,
                    load.WeightLbs,
                    load.ShipperCity,
                    load.ReceiverCity,
                    "BOL Complete");

                LoadBoardTelemetryService.Shared.MarkBolComplete(load.LoadNumber);

                MessageBox.Show($"BOL sent.\n\nResponse:\n{result}", "Load Board BOL", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to send BOL to Discord.\n\n" + ex, "Load Board BOL", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void SyncLoadToDispatchHistory(LoadBoardLoad load, TelemetrySnapshot? snap)
        {
            try
            {
                if (load == null || string.IsNullOrWhiteSpace(load.LoadNumber)) return;

                var existing = DispatchService.Jobs.FirstOrDefault(x => Same(x.LoadNumber, load.LoadNumber));
                var job = existing ?? new DispatchJob
                {
                    Id = Guid.NewGuid().ToString("N"),
                    LoadNumber = load.LoadNumber,
                    CreatedUtc = DateTime.UtcNow,
                    PostedUtc = DateTime.UtcNow,
                    ClaimedUtc = DateTime.UtcNow,
                    IsClaimLocked = true,
                    DispatchMode = "Telemetry"
                };

                SplitCityState(load.ShipperCity, out var originCity, out var originState);
                SplitCityState(load.ReceiverCity, out var destCity, out var destState);

                job.LoadNumber = load.LoadNumber;
                job.Company = load.ShipperName;
                job.OriginCity = FirstNonEmpty(originCity, load.ShipperCity);
                job.OriginState = originState;
                job.DestinationCity = FirstNonEmpty(destCity, load.ReceiverCity);
                job.DestinationState = destState;
                job.AssignedDriver = FirstNonEmpty(load.DriverName, job.AssignedDriver, "Unassigned");
                job.ClaimedBy = FirstNonEmpty(load.DriverName, job.ClaimedBy);
                job.AssignedTruck = FirstNonEmpty(load.TruckName, job.AssignedTruck);
                job.LastKnownTruckName = FirstNonEmpty(load.TruckName, job.LastKnownTruckName);
                job.Trailer = FirstNonEmpty(load.TrailerName, job.Trailer);
                job.Cargo = FirstNonEmpty(load.Commodity, job.Cargo);
                job.CargoWeight = load.WeightLbs;
                job.ActualCargoWeightLbs = load.WeightLbs;
                job.Status = NormalizeStatus(load.Status);
                job.DispatchMode = "Telemetry";
                job.UpdatedUtc = DateTime.UtcNow;
                job.LastStatusChangeUtc ??= DateTime.UtcNow;

                if (load.RevenueUsd > 0)
                {
                    job.RevenueUsd = load.RevenueUsd;
                    job.Payout = load.RevenueUsd;
                    job.RevenueCapturedUtc = (load.RevenueCapturedUtc ?? DateTimeOffset.UtcNow).UtcDateTime;
                    job.RevenueSource = FirstNonEmpty(load.RevenueSource, "ATS Telemetry");
                }

                if (snap?.PlannedMiles is double planned && planned > 0)
                {
                    job.Miles = (int)Math.Round(planned);
                    if (job.RatePerMile <= 0 && job.BestRevenue > 0)
                        job.RatePerMile = Math.Round(job.BestRevenue / Math.Max(1, job.Miles), 2);
                }

                if (existing == null)
                    DispatchService.Jobs.Add(job);

                DispatchService.SaveJobs();
            }
            catch
            {
                // Saving the BOL must never crash because dispatch history sync failed.
            }
        }

        private static void ApplyTelemetryRevenue(LoadBoardLoad load, TelemetrySnapshot snap)
        {
            var revenue = ParseRevenue(snap.RevenueDisplay);
            if (revenue <= 0) return;
            load.RevenueUsd = revenue;
            load.RevenueCapturedUtc = DateTimeOffset.UtcNow;
            load.RevenueSource = string.IsNullOrWhiteSpace(snap.RevenueDisplay) ? "ATS Telemetry" : "ATS Telemetry: " + snap.RevenueDisplay.Trim();
        }

        private static string NormalizeStatus(string? status)
        {
            var s = (status ?? "").Trim();
            if (s.Equals("BOL Draft", StringComparison.OrdinalIgnoreCase)) return "At Shipper";
            if (s.Equals("BOL Complete", StringComparison.OrdinalIgnoreCase)) return "Accepted";
            return string.IsNullOrWhiteSpace(s) ? "At Shipper" : s;
        }

        private static void SetIfEmpty(TextBox box, string? value)
        {
            if (box == null) return;
            if (string.IsNullOrWhiteSpace(box.Text) && !string.IsNullOrWhiteSpace(value))
                box.Text = value.Trim();
        }

        private static string GetBestCargoName(TelemetrySnapshot snap)
        {
            if (!string.IsNullOrWhiteSpace(snap.CargoName)) return snap.CargoName.Trim();
            if (snap.CargoWeightLbs.HasValue && snap.CargoWeightLbs.Value > 0) return $"Cargo ({snap.CargoWeightLbs.Value:N0} lbs)";
            if (snap.TrailerWeightLbs.HasValue && snap.TrailerWeightLbs.Value > 0) return $"Trailer Load ({snap.TrailerWeightLbs.Value:N0} lbs)";
            if (snap.GrossWeightLbs.HasValue && snap.GrossWeightLbs.Value > 0) return $"Freight ({snap.GrossWeightLbs.Value:N0} lbs gross)";
            return "Freight";
        }

        private static double GetBestWeight(TelemetrySnapshot snap)
        {
            if (snap.CargoWeightLbs.HasValue && snap.CargoWeightLbs.Value > 0) return snap.CargoWeightLbs.Value;
            if (snap.TrailerWeightLbs.HasValue && snap.TrailerWeightLbs.Value > 0) return snap.TrailerWeightLbs.Value;
            if (snap.GrossWeightLbs.HasValue && snap.GrossWeightLbs.Value > 0) return snap.GrossWeightLbs.Value;
            return 0;
        }

        private static decimal ParseRevenue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0m;
            var chars = value.Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray();
            return decimal.TryParse(new string(chars), NumberStyles.Number, CultureInfo.InvariantCulture, out var result) ? Math.Abs(result) : 0m;
        }

        private static string JoinLocation(string? city, string? state)
        {
            city = (city ?? "").Trim();
            state = (state ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(city) && !string.IsNullOrWhiteSpace(state)) return $"{city}, {state}";
            return FirstNonEmpty(city, state);
        }

        private static void SplitCityState(string? text, out string city, out string state)
        {
            city = "";
            state = "";
            var value = (text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value)) return;
            var parts = value.Split(',', 2);
            city = parts[0].Trim();
            if (parts.Length > 1) state = parts[1].Trim();
        }

        private static string CurrentDriverName()
        {
            try
            {
                var app = Application.Current as App;
                var name = Convert.ToString(app?.Session?.DriverName)?.Trim() ?? "";
                return string.IsNullOrWhiteSpace(name) || name.Equals("Driver", StringComparison.OrdinalIgnoreCase) ? "User" : name;
            }
            catch { return "User"; }
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var v in values)
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            return "";
        }

        private static bool Same(string? a, string? b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
