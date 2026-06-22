using OverWatchELD.Services;
using OverWatchELD.Services.Operations;
using OverWatchELD.Services.Economy;
using OverWatchELD.Services.Dispatch;
using OverWatchELD.Services.LoadBoard;
using OverWatchELD.Services.Fleet;
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Collections.Generic;
using System.IO;


namespace OverWatchELD.Views
{
    public partial class DispatchView : UserControl
    {
        private bool _loadBoardHistoryLoaded;
        private DispatchTelemetrySyncService? _dispatchTelemetrySync;
        private bool _dispatchTelemetrySyncStarted;

        public DispatchView()
        {
            InitializeComponent();

            if (FilterBox != null)
                FilterBox.SelectedIndex = 0;

            LoadSavedLoadBoardHistoryIntoDispatch();
            RefreshGrid();
            StartDispatchTelemetrySync();
            Unloaded += DispatchView_Unloaded;
        }

        private void StartDispatchTelemetrySync()
        {
            if (_dispatchTelemetrySyncStarted)
                return;

            _dispatchTelemetrySyncStarted = true;

            try
            {
                var app = Application.Current as App;
                var telemetry = app?.Telemetry;

                if (telemetry == null)
                    return;

                _dispatchTelemetrySync = new DispatchTelemetrySyncService(
                    telemetry,
                    () => CurrentDriverName);

                _dispatchTelemetrySync.Start();
            }
            catch
            {
            }
        }

        private void DispatchView_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _dispatchTelemetrySync?.Stop();
                _dispatchTelemetrySync = null;
                _dispatchTelemetrySyncStarted = false;
            }
            catch
            {
            }
        }

        private DispatchJob? SelectedJob => JobsGrid?.SelectedItem as DispatchJob;

        private string CurrentDriverName
        {
            get
            {
                try
                {
                    var app = Application.Current as App;
                    var name = Convert.ToString(app?.Session?.DriverName)?.Trim() ?? "";

                    if (!string.IsNullOrWhiteSpace(name) &&
                        !name.Equals("Driver", StringComparison.OrdinalIgnoreCase))
                        return name;
                }
                catch { }

                return "User";
            }
        }

        private void LoadSavedLoadBoardHistoryIntoDispatch()
        {
            if (_loadBoardHistoryLoaded)
                return;

            _loadBoardHistoryLoaded = true;

            try
            {
                var deletedLoadNumbers = LoadDeletedLoadNumbers();

                var loads = LoadBoardStore.LoadAll()
                    .Where(x => !string.IsNullOrWhiteSpace(x.LoadNumber))
                    .Where(x => !deletedLoadNumbers.Contains(x.LoadNumber.Trim()))
                    ;
                foreach (var load in loads)
                {
                    var existing = DispatchService.Jobs.FirstOrDefault(x =>
                        string.Equals(x.LoadNumber, load.LoadNumber, StringComparison.OrdinalIgnoreCase));

                    var job = BuildDispatchJobFromLoadBoard(load, existing);

                    if (existing == null)
                        DispatchService.Jobs.Add(job);
                    else
                        CopyJobValues(existing, job);
                }

                DispatchService.SaveJobs();
            }
            catch
            {
                // Never let saved history loading break the Dispatch screen.
            }
        }

        private DispatchJob BuildDispatchJobFromLoadBoard(LoadBoardLoad load, DispatchJob? existing)
        {
            var updated = GetLoadSortDate(load).UtcDateTime;
            if (updated == default)
                updated = DateTime.UtcNow;

            var status = FirstNonEmpty(load.Status, "Imported");

            var job = existing ?? new DispatchJob
            {
                Id = Guid.NewGuid().ToString("N"),
                CreatedUtc = updated,
                PostedUtc = updated
            };

            job.LoadNumber = FirstNonEmpty(load.LoadNumber, job.LoadNumber);
            job.AssignedDriver = FirstNonEmpty(load.DriverName, CurrentDriverName);
            job.ClaimedBy = FirstNonEmpty(load.DriverName, CurrentDriverName);
            job.AssignedTruck = FirstNonEmpty(load.TruckName, job.AssignedTruck);
            job.LastKnownTruckName = FirstNonEmpty(load.TruckName, job.LastKnownTruckName);
            job.Trailer = FirstNonEmpty(load.TrailerName, job.Trailer);
            job.Cargo = FirstNonEmpty(load.Commodity, job.Cargo);
            job.CargoWeight = load.WeightLbs;
            job.ActualCargoWeightLbs = load.WeightLbs;

            var importedRevenue = GetDecimalProperty(load, "RevenueUsd", "Payout", "Pay", "Income", "Revenue");
            if (job.RevenueUsd <= 0 && importedRevenue > 0)
                job.RevenueUsd = importedRevenue;
            if (job.Payout <= 0 && importedRevenue > 0)
                job.Payout = importedRevenue;
            if (job.RatePerMile <= 0 && job.Miles > 0 && job.BestRevenue > 0)
                job.RatePerMile = Math.Round(job.BestRevenue / Math.Max(1, job.Miles), 2);
            job.Status = NormalizeDispatchStatus(status);
            job.DispatchMode = "Telemetry";
            job.IsClaimLocked = true;
            job.UpdatedUtc = updated;
            job.LastStatusChangeUtc ??= updated;

            SplitCityState(load.ShipperCity, out var originCity, out var originState);
            SplitCityState(load.ReceiverCity, out var destinationCity, out var destinationState);

            job.Company = FirstNonEmpty(load.ShipperName, job.Company);
            job.OriginCity = FirstNonEmpty(originCity, load.ShipperCity, job.OriginCity);
            job.OriginState = FirstNonEmpty(originState, job.OriginState);
            job.DestinationCity = FirstNonEmpty(destinationCity, load.ReceiverCity, job.DestinationCity);
            job.DestinationState = FirstNonEmpty(destinationState, job.DestinationState);

            if (job.Status.Equals("Delivered", StringComparison.OrdinalIgnoreCase) ||
                job.Status.Equals("BOL Complete", StringComparison.OrdinalIgnoreCase))
            {
                job.Status = "Delivered";
                job.DeliveredUtc ??= updated;
                job.IsOverdue = false;
            }
            else
            {
                job.IsOverdue = DispatchService.CalculateIsOverdue(job);
            }

            return job;
        }

        private static void CopyJobValues(DispatchJob target, DispatchJob source)
        {
            target.LoadNumber = source.LoadNumber;
            target.Company = source.Company;
            target.OriginCity = source.OriginCity;
            target.OriginState = source.OriginState;
            target.DestinationCity = source.DestinationCity;
            target.DestinationState = source.DestinationState;
            target.Miles = source.Miles;
            target.Cargo = source.Cargo;
            target.Trailer = source.Trailer;
            target.AssignedDriver = source.AssignedDriver;
            target.AssignedTruck = source.AssignedTruck;
            target.Status = source.Status;
            target.CargoWeight = source.CargoWeight;
            target.ActualCargoWeightLbs = source.ActualCargoWeightLbs;
            target.Payout = source.Payout;
            target.RevenueUsd = source.RevenueUsd;
            target.RatePerMile = source.RatePerMile;
            target.RevenueCapturedUtc = source.RevenueCapturedUtc;
            target.RevenueSource = source.RevenueSource;
            target.DispatchMode = source.DispatchMode;
            target.ClaimedBy = source.ClaimedBy;
            target.IsClaimLocked = source.IsClaimLocked;
            target.LastKnownTruckName = source.LastKnownTruckName;
            target.UpdatedUtc = source.UpdatedUtc;
            target.LastStatusChangeUtc = source.LastStatusChangeUtc;
            target.DeliveredUtc = source.DeliveredUtc;
            target.IsOverdue = source.IsOverdue;
        }

        private void RefreshGrid()
        {
            LoadSavedLoadBoardHistoryIntoDispatch();

            if (JobsGrid != null)
            {
                JobsGrid.ItemsSource = DispatchService
                    .GetFilteredJobs(GetSearchText(), GetFilterText(), CurrentDriverName)
                    .OrderByDescending(x => x.UpdatedUtc)
                    .ToList();
            }

            if (ActiveLoadsText != null)
                ActiveLoadsText.Text = DispatchService.ActiveLoadsCount().ToString();

            if (InTransitText != null)
                InTransitText.Text = DispatchService.InTransitCount().ToString();

            if (DeliveredTodayText != null)
                DeliveredTodayText.Text = DispatchService.DeliveredTodayCount().ToString();

            if (OverdueText != null)
                OverdueText.Text = DispatchService.OverdueCount().ToString();

            if (UnassignedText != null)
                UnassignedText.Text = DispatchService.UnassignedCount().ToString();

            if (ActiveDriversText != null)
                ActiveDriversText.Text = DispatchService.ActiveDriversCount().ToString();

            var stats = DispatchService.GetMemberStats(CurrentDriverName);

            if (MyActiveLoadsText != null)
                MyActiveLoadsText.Text = stats.ActiveLoads.ToString();

            if (MyCompletedLoadsText != null)
                MyCompletedLoadsText.Text = stats.CompletedLoads.ToString();

            if (MyOverdueLoadsText != null)
                MyOverdueLoadsText.Text = stats.OverdueLoads.ToString();

            if (MyMilesText != null)
                MyMilesText.Text = stats.TotalMiles.ToString("N0");

            if (MyPayoutText != null)
                MyPayoutText.Text = stats.TotalPayout <= 0 ? "$0" : stats.TotalPayout.ToString("C0");

            if (MyOnTimeText != null)
                MyOnTimeText.Text = $"{stats.OnTimePercent:0.#}%";

            var leaderboard = DispatchService.GetLeaderboard();

            if (LeaderboardGrid != null)
                LeaderboardGrid.ItemsSource = leaderboard;

            var topByLoads = leaderboard.FirstOrDefault();
            var topByRevenue = leaderboard.OrderByDescending(x => x.TotalRevenue).FirstOrDefault();
            var bestOnTime = leaderboard
                .Where(x => x.CompletedLoads > 0)
                .OrderByDescending(x => x.OnTimePercent)
                .ThenByDescending(x => x.CompletedLoads)
                .FirstOrDefault();

            if (TopDriverText != null)
                TopDriverText.Text = topByLoads == null ? "--" : $"{topByLoads.DriverName} ({topByLoads.CompletedLoads} loads)";

            if (TopRevenueText != null)
                TopRevenueText.Text = topByRevenue == null ? "--" : $"{topByRevenue.DriverName} ({topByRevenue.RevenueDisplay})";

            if (BestOnTimeText != null)
                BestOnTimeText.Text = bestOnTime == null ? "--" : $"{bestOnTime.DriverName} ({bestOnTime.OnTimeDisplay})";
        }

        private string GetSearchText() => SearchBox?.Text ?? "";

        private string GetFilterText()
        {
            if (FilterBox?.SelectedItem is ComboBoxItem cbi)
                return cbi.Content?.ToString() ?? "All";

            return "All";
        }

        private void CreateLoad_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new CleanCreateLoadWindow
                {
                    Owner = Window.GetWindow(this)
                };

                if (win.ShowDialog() == true && win.SavedJob != null)
                {
                    DispatchService.UpdateJob(win.SavedJob);
                    RefreshGrid();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Create Load failed.\n\n" + ex.Message, "Dispatch Tracker", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AssignDriver_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var job = SelectedJob;
                if (job == null)
                {
                    MessageBox.Show("Select a load first.", "Assign Driver", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var win = new AssignDriverWindow(job)
                {
                    Owner = Window.GetWindow(this)
                };

                if (win.ShowDialog() == true)
                {
                    job.UpdatedUtc = DateTime.UtcNow;
                    job.IsOverdue = DispatchService.CalculateIsOverdue(job);

                    if (!string.IsNullOrWhiteSpace(job.AssignedDriver) &&
                        !job.AssignedDriver.Equals("Unassigned", StringComparison.OrdinalIgnoreCase))
                    {
                        job.DispatchMode = "Assigned";
                        if (string.IsNullOrWhiteSpace(job.ClaimedBy))
                        {
                            job.ClaimedBy = job.AssignedDriver;
                            job.ClaimedUtc ??= DateTime.UtcNow;
                        }

                        job.IsClaimLocked = true;
                    }

                    DispatchService.UpdateJob(job);
                    RefreshGrid();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Assign Driver failed.\n\n" + ex.Message, "Dispatch Tracker", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteLoad_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var job = SelectedJob;
                if (job == null)
                {
                    MessageBox.Show("Select a load first.", "Delete Load", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var confirm = MessageBox.Show($"Delete load {job.LoadNumber}?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (confirm == MessageBoxResult.Yes)
                {
                    MarkLoadNumberDeleted(job.LoadNumber);
                    TryDeleteFromLoadBoardStore(job.LoadNumber);

                    DispatchService.DeleteJob(job);
                    RefreshGrid();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Delete Load failed.\n\n" + ex.Message, "Dispatch Tracker", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string DeletedLoadsFilePath
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "OverWatchELD");

                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "deleted_dispatch_loads.json");
            }
        }

        private static HashSet<string> LoadDeletedLoadNumbers()
        {
            try
            {
                if (!File.Exists(DeletedLoadsFilePath))
                    return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var json = File.ReadAllText(DeletedLoadsFilePath);
                var list = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();

                return new HashSet<string>(
                    list.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()),
                    StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static void MarkLoadNumberDeleted(string? loadNumber)
        {
            if (string.IsNullOrWhiteSpace(loadNumber))
                return;

            try
            {
                var deleted = LoadDeletedLoadNumbers();
                deleted.Add(loadNumber.Trim());

                var json = JsonSerializer.Serialize(
                    deleted.OrderBy(x => x).ToList(),
                    new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(DeletedLoadsFilePath, json);
            }
            catch
            {
            }
        }

        private static void TryDeleteFromLoadBoardStore(string? loadNumber)
        {
            if (string.IsNullOrWhiteSpace(loadNumber))
                return;

            try
            {
                var storeType = typeof(LoadBoardStore);

                var method =
                    storeType.GetMethod("DeleteByLoadNumber") ??
                    storeType.GetMethod("Delete") ??
                    storeType.GetMethod("Remove") ??
                    storeType.GetMethod("RemoveByLoadNumber");

                if (method != null)
                    method.Invoke(null, new object[] { loadNumber.Trim() });
            }
            catch
            {
            }
        }

        private void Claim_Click(object sender, RoutedEventArgs e)
        {
            var job = SelectedJob;
            if (job == null)
            {
                MessageBox.Show("Select a load first.", "Dispatch Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!DispatchService.ClaimJob(job, CurrentDriverName))
            {
                MessageBox.Show("This load is already claimed or locked.", "Dispatch Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            RefreshGrid();
        }

        private void Accept_Click(object sender, RoutedEventArgs e)
        {
            var job = SelectedJob;
            if (job == null)
            {
                MessageBox.Show("Select a load first.", "Dispatch Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            DispatchService.AcceptJob(job);
            RefreshGrid();
        }



        private void ImportTelemetryLoad_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var app = Application.Current as App;
                var snap = app?.Telemetry?.LastSnapshot;

                if (snap == null)
                {
                    MessageBox.Show("No telemetry snapshot found. Make sure ATS and telemetry are running.");
                    return;
                }

                var loadNumber = $"ATS-{DateTime.Now:yyyyMMdd-HHmmss}";
                var driver = FirstNonEmpty(snap.DriverName, CurrentDriverName);
                var truck = FirstNonEmpty(snap.TruckName, snap.TruckMakeModel, "ATS Truck");
                var cargo = FirstNonEmpty(snap.CargoName, GetBestCargoName(snap), "ATS Current Load");
                var weight = GetBestWeight(snap);
                var now = DateTime.UtcNow;
                var telemetryRevenue = ParseRevenue(snap.RevenueDisplay);

                var load = new LoadBoardLoad
                {
                    LoadNumber = loadNumber,
                    Status = snap.EngineOn ? "In Transit" : "At Shipper",
                    DriverName = driver,
                    TruckName = truck,
                    TrailerName = FirstNonEmpty(snap.TrailerName, ""),
                    Commodity = cargo,
                    WeightLbs = weight,
                    ShipperName = FirstNonEmpty(snap.SourceCompany, ""),
                    ShipperCity = FirstNonEmpty(snap.SourceCity, JoinLocation(snap.City, snap.State)),
                    ReceiverName = FirstNonEmpty(snap.DestinationCompany, ""),
                    ReceiverCity = FirstNonEmpty(snap.DestinationCity, ""),
                    CurrentLocation = JoinLocation(snap.City, snap.State),
                    RevenueUsd = telemetryRevenue,
                    RevenueCapturedUtc = telemetryRevenue > 0 ? new DateTimeOffset(now, TimeSpan.Zero) : null,
                    RevenueSource = telemetryRevenue > 0 ? "ATS Telemetry Import" : "",
                    CreatedUtc = new DateTimeOffset(now, TimeSpan.Zero),
                    UpdatedUtc = new DateTimeOffset(now, TimeSpan.Zero)
                };

                LoadBoardStore.Upsert(load);

                var job = new DispatchJob
                {
                    Id = Guid.NewGuid().ToString("N"),
                    LoadNumber = loadNumber,
                    AssignedDriver = driver,
                    ClaimedBy = driver,
                    AssignedTruck = truck,
                    LastKnownTruckName = truck,
                    Cargo = cargo,
                    Trailer = load.TrailerName,
                    CargoWeight = weight,
                    ActualCargoWeightLbs = weight,
                    DispatchMode = "Telemetry",
                    Status = snap.EngineOn ? "In Transit" : "At Shipper",
                    Miles = snap.PlannedMiles.HasValue ? (int)Math.Round(snap.PlannedMiles.Value) : 0,
                    RevenueUsd = telemetryRevenue,
                    Payout = telemetryRevenue,
                    RatePerMile = snap.PlannedMiles.HasValue && snap.PlannedMiles.Value > 0 && telemetryRevenue > 0
                        ? Math.Round(telemetryRevenue / Math.Max(1, (decimal)snap.PlannedMiles.Value), 2)
                        : 0,
                    RevenueCapturedUtc = telemetryRevenue > 0 ? now : null,
                    RevenueSource = telemetryRevenue > 0 ? "ATS Telemetry Pickup" : "",
                    UpdatedUtc = now,
                    CreatedUtc = now,
                    PostedUtc = now,
                    LastStatusChangeUtc = now,
                    ClaimedUtc = now,
                    IsClaimLocked = true
                };

                SplitCityState(load.ShipperCity, out var originCity, out var originState);
                SplitCityState(load.ReceiverCity, out var destinationCity, out var destinationState);

                job.Company = load.ShipperName;
                job.OriginCity = originCity;
                job.OriginState = originState;
                job.DestinationCity = destinationCity;
                job.DestinationState = destinationState;

                var existing = DispatchService.Jobs.FirstOrDefault(x =>
                    string.Equals(x.LoadNumber, job.LoadNumber, StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                    DispatchService.Jobs.Add(job);
                else
                    CopyJobValues(existing, job);

                DispatchService.SaveJobs();

                if (SearchBox != null)
                    SearchBox.Text = "";

                if (FilterBox != null)
                    FilterBox.SelectedIndex = 0;

                RefreshGrid();

                JobsGrid.SelectedItem = DispatchService.Jobs.FirstOrDefault(x =>
                    string.Equals(x.LoadNumber, loadNumber, StringComparison.OrdinalIgnoreCase));

                if (JobsGrid.SelectedItem != null)
                    JobsGrid.ScrollIntoView(JobsGrid.SelectedItem);

                MessageBox.Show($"Imported telemetry load and saved it to Load Board history:\n{loadNumber}", "Dispatch Tracker");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Import telemetry load failed.\n\n" + ex.Message, "Dispatch Tracker", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PickedUp_Click(object sender, RoutedEventArgs e)
        {
            var job = SelectedJob;
            if (job == null)
            {
                MessageBox.Show("Select a load first.", "Dispatch Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            DispatchService.MarkPickedUp(job);
            RefreshGrid();
        }

        private void InTransit_Click(object sender, RoutedEventArgs e)
        {
            var job = SelectedJob;
            if (job == null)
            {
                MessageBox.Show("Select a load first.", "Dispatch Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            DispatchService.MarkInTransit(job);
            RefreshGrid();
        }

        private void Delivered_Click(object sender, RoutedEventArgs e)
        {
            var job = SelectedJob;
            if (job == null)
            {
                MessageBox.Show("Select a load first.", "Dispatch Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            DispatchService.MarkDelivered(job);
            RunPostDeliveryOperations(job);
            RefreshGrid();
        }


        private void OpenOperationsHub_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                FleetEconomyIntegration.OpenOperationsCommandCenterWindow(Window.GetWindow(this));
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Operations Command Center",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private async void SyncOperations_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = OperationsOrchestratorService.RunDispatchOperations();
                var portalResult = await LoadBoardPortalSyncService.Shared.SyncAllAsync(
                    LoadBoardStore.LoadAll(),
                    DispatchService.Jobs);

                var fleetResult = await FleetPortalSyncService.Shared.SyncRepositoryAsync();

                MessageBox.Show(
                    result.Summary + "\n\n" + portalResult.Summary + "\n\n" + fleetResult.Summary,
                    "Dispatch Operations + Website Sync",
                    MessageBoxButton.OK,
                    result.Success && portalResult.Success && fleetResult.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);

                RefreshGrid();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Dispatch Operations + Website Sync",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void RunPostDeliveryOperations(DispatchJob job)
        {
            try
            {
                OperationsOrchestratorService.RunPostDeliveryOperations(job);
            }
            catch
            {
            }
        }

        private void Cancelled_Click(object sender, RoutedEventArgs e)
        {
            var job = SelectedJob;
            if (job == null)
            {
                MessageBox.Show("Select a load first.", "Dispatch Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            DispatchService.MarkCancelled(job);
            RefreshGrid();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshGrid();

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshGrid();

        private void FilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded)
                RefreshGrid();
        }

        private void JobsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var job = SelectedJob;
                if (job == null)
                    return;

                var win = new CleanCreateLoadWindow(job)
                {
                    Owner = Window.GetWindow(this)
                };

                if (win.ShowDialog() == true && win.SavedJob != null)
                {
                    DispatchService.UpdateJob(win.SavedJob);
                    RefreshGrid();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Edit Load failed.\n\n" + ex.Message, "Dispatch Tracker", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenBol_Click(object sender, RoutedEventArgs e)
        {
            OpenBolForSelectedJob();
        }

        private void OpenBolFromRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is DispatchJob job)
                OpenBolForJob(job);
            else
                OpenBolForSelectedJob();
        }

        private void OpenBolForSelectedJob()
        {
            var job = SelectedJob;
            if (job == null)
            {
                MessageBox.Show("Select a load first.", "BOL", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            OpenBolForJob(job);
        }

        private void OpenBolForJob(DispatchJob job)
        {
            try
            {
                var win = new LoadBoardBolWindow(job.LoadNumber ?? "")
                {
                    Owner = Window.GetWindow(this)
                };

                win.ShowDialog();

                _loadBoardHistoryLoaded = false;
                LoadSavedLoadBoardHistoryIntoDispatch();
                RefreshGrid();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Open BOL failed.\n\n" + ex.Message, "Dispatch Tracker", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AssignActiveTruck_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Use Fleet Command Center to assign the active truck.", "Dispatch Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void AssignActiveTrailer_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Use Fleet Command Center to assign the active trailer.", "Dispatch Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void EnRoutePickup_Click(object sender, RoutedEventArgs e)
        {
            var job = SelectedJob;
            if (job == null)
            {
                MessageBox.Show("Select a load first.", "Dispatch Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            job.Status = "En Route Pickup";
            job.LastStatusChangeUtc = DateTime.UtcNow;
            job.UpdatedUtc = DateTime.UtcNow;
            DispatchService.SaveJobs();
            RefreshGrid();
        }

        private void AtShipper_Click(object sender, RoutedEventArgs e)
        {
            var job = SelectedJob;
            if (job == null)
            {
                MessageBox.Show("Select a load first.", "Dispatch Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            job.Status = "At Shipper";
            job.LastStatusChangeUtc = DateTime.UtcNow;
            job.UpdatedUtc = DateTime.UtcNow;
            DispatchService.SaveJobs();
            RefreshGrid();
        }

        private void ForcePickup_Click(object sender, RoutedEventArgs e) => PickedUp_Click(sender, e);

        private void AtReceiver_Click(object sender, RoutedEventArgs e)
        {
            var job = SelectedJob;
            if (job == null)
            {
                MessageBox.Show("Select a load first.", "Dispatch Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            job.Status = "At Receiver";
            job.LastStatusChangeUtc = DateTime.UtcNow;
            job.UpdatedUtc = DateTime.UtcNow;
            DispatchService.SaveJobs();
            RefreshGrid();
        }

        private void ForceDelivered_Click(object sender, RoutedEventArgs e) => Delivered_Click(sender, e);

        private static DateTimeOffset GetLoadSortDate(LoadBoardLoad load)
        {
            try
            {
                var bol = load.BolCompletedUtc;
                if (bol.HasValue)
                    return bol.Value;
            }
            catch { }

            return DateTimeOffset.UtcNow;
        }

        private static string NormalizeDispatchStatus(string status)
        {
            status = (status ?? "").Trim();

            if (status.Equals("BOL Complete", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("Complete", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("Completed", StringComparison.OrdinalIgnoreCase))
                return "Delivered";

            if (string.IsNullOrWhiteSpace(status))
                return "Imported";

            return status;
        }

        private static decimal ParseRevenue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0;

            var cleaned = new string(value.Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());

            return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
                ? result
                : 0;
        }

        private static decimal GetDecimalProperty(object obj, params string[] propertyNames)
        {
            if (obj == null)
                return 0;

            foreach (var name in propertyNames)
            {
                try
                {
                    var p = obj.GetType().GetProperty(name);
                    var v = p?.GetValue(obj);

                    if (v == null)
                        continue;

                    if (v is decimal d) return d;
                    if (v is double db) return (decimal)db;
                    if (v is float f) return (decimal)f;
                    if (v is int i) return i;
                    if (v is long l) return l;

                    var text = v.ToString();
                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    var chars = new List<char>();
                    foreach (var c in text)
                    {
                        if (char.IsDigit(c) || c == '.' || c == '-')
                            chars.Add(c);
                    }

                    var cleaned = new string(chars.ToArray());
                    if (decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                        return parsed;
                }
                catch
                {
                }
            }

            return 0;
        }

        private static string GetBestCargoName(TelemetrySnapshot snap)
        {
            if (!string.IsNullOrWhiteSpace(snap.CargoName))
                return snap.CargoName.Trim();

            if (snap.CargoWeightLbs.HasValue && snap.CargoWeightLbs.Value > 0)
                return $"Cargo ({snap.CargoWeightLbs.Value:N0} lbs)";

            if (snap.TrailerWeightLbs.HasValue && snap.TrailerWeightLbs.Value > 0)
                return $"Trailer Load ({snap.TrailerWeightLbs.Value:N0} lbs)";

            if (snap.GrossWeightLbs.HasValue && snap.GrossWeightLbs.Value > 0)
                return $"Freight ({snap.GrossWeightLbs.Value:N0} lbs gross)";

            return "Freight";
        }

        private static double GetBestWeight(TelemetrySnapshot snap)
        {
            if (snap.CargoWeightLbs.HasValue && snap.CargoWeightLbs.Value > 0)
                return snap.CargoWeightLbs.Value;

            if (snap.TrailerWeightLbs.HasValue && snap.TrailerWeightLbs.Value > 0)
                return snap.TrailerWeightLbs.Value;

            if (snap.GrossWeightLbs.HasValue && snap.GrossWeightLbs.Value > 0)
                return snap.GrossWeightLbs.Value;

            return 0;
        }

        private static string JoinLocation(string? city, string? state)
        {
            city = (city ?? "").Trim();
            state = (state ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(city) && !string.IsNullOrWhiteSpace(state))
                return $"{city}, {state}";

            return FirstNonEmpty(city, state);
        }

        private static void SplitCityState(string? value, out string city, out string state)
        {
            city = "";
            state = "";

            var text = (value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text))
                return;

            var parts = text.Split(',', 2);
            city = parts[0].Trim();

            if (parts.Length > 1)
                state = parts[1].Trim();
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }
    }
}
