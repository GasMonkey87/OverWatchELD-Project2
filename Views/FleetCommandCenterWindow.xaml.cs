using OverWatchELD.Models.Fleet;
using OverWatchELD.Services;
using OverWatchELD.Services.Fleet;
using OverWatchELD.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace OverWatchELD.Views
{
    public partial class FleetCommandCenterWindow : Window
    {
        private FleetCommandCenterViewModel Vm => (FleetCommandCenterViewModel)DataContext;

        private readonly FleetDriverDirectoryService _driverDirectory = new FleetDriverDirectoryService();
        private readonly DispatcherTimer _fleetPollTimer = new DispatcherTimer();
        private readonly string _accessRole;
        private readonly bool _canOpenAllBols;

        private bool _fleetPollInFlight;
        private bool _gridSaveInFlight;
        private List<string> _driverNames = new();

        public FleetCommandCenterWindow() : this("")
        {
        }

        public FleetCommandCenterWindow(string accessRole)
        {
            _accessRole = (accessRole ?? "").Trim();
            _canOpenAllBols = BolAccessService.CanViewAllBols()
                || AdminAccessService.CanManageDiscordSettings("", _accessRole);

            InitializeComponent();
            DataContext = new FleetCommandCenterViewModel();

            Loaded += FleetCommandCenterWindow_Loaded;
            Closed += FleetCommandCenterWindow_Closed;

            try
            {
                FleetBolsButton.Visibility = _canOpenAllBols ? Visibility.Visible : Visibility.Collapsed;
                FleetBolsButton.IsEnabled = _canOpenAllBols;
            }
            catch { }
        }

        private async void FleetCommandCenterWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            await RefreshFleetCommandAsync();

            _fleetPollTimer.Interval = TimeSpan.FromSeconds(8);
            _fleetPollTimer.Tick -= FleetPollTimer_Tick;
            _fleetPollTimer.Tick += FleetPollTimer_Tick;
            _fleetPollTimer.Start();
        }

        private void FleetCommandCenterWindow_Closed(object? sender, EventArgs e)
        {
            try
            {
                _fleetPollTimer.Stop();
                _fleetPollTimer.Tick -= FleetPollTimer_Tick;
            }
            catch { }
        }

        private async void FleetPollTimer_Tick(object? sender, EventArgs e)
        {
            await RefreshFleetCommandAsync();
        }

        private async Task RefreshFleetCommandAsync()
        {
            if (_fleetPollInFlight)
                return;

            _fleetPollInFlight = true;

            try
            {
                var selectedTruckNumber = "";

                try
                {
                    if (Vm.SelectedTruck != null)
                        selectedTruckNumber = ReadObjectString(Vm.SelectedTruck, "TruckNumber");

                    if (string.IsNullOrWhiteSpace(selectedTruckNumber))
                        selectedTruckNumber = NormalizeTruckNumberInput();
                }
                catch
                {
                    selectedTruckNumber = "";
                }

                Vm.Refresh();
                PurgeFakeDriverRows();

                await LoadDiscordDriversAsync();
                await PullVtcPresenceIntoFleetAsync();
                SyncLocalTelemetryIntoSelectedOrAssignedTruck();

                Vm.Refresh();

                if (!string.IsNullOrWhiteSpace(selectedTruckNumber))
                    RepickTruck(selectedTruckNumber);
                else
                    PopulateFormFromSelectedTruck();
            }
            catch { }
            finally
            {
                _fleetPollInFlight = false;
            }
        }

        private void TruckGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (_gridSaveInFlight)
                return;

            if (e.Row?.Item is not FleetCommandCenterViewModel.FleetCommandTruckRow row)
                return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                SaveEditedGridRow(row);
            }), DispatcherPriority.Background);
        }

        private void SaveEditedGridRow(FleetCommandCenterViewModel.FleetCommandTruckRow row)
        {
            if (_gridSaveInFlight)
                return;

            _gridSaveInFlight = true;

            try
            {
                var truckNumber = (row.TruckNumber ?? "").Trim();

                if (string.IsNullOrWhiteSpace(truckNumber) || truckNumber.Equals("--", StringComparison.OrdinalIgnoreCase))
                    return;

                if (truckNumber.StartsWith("DRV-", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("Truck ID must be the real assigned truck number, not a DRV placeholder.", "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Warning);
                    Vm.Refresh();
                    return;
                }

                var original = Vm.LoadSelectedFull();
                if (original == null || !string.Equals(original.Id, row.Id, StringComparison.OrdinalIgnoreCase))
                    original = Vm.CreateOrGetTruck(truckNumber);

                original.TruckNumber = truckNumber;
                original.PlateNumber = CleanGridText(row.PlateNumber);
                original.TruckName = CleanGridText(row.TruckName);
                original.Model = CleanGridText(row.Model);
                original.AssignedDriver = CleanDriverGridText(row.AssignedDriver);
                original.CurrentLoadNumber = CleanGridText(row.LoadNumber);
                original.Status = CleanGridText(row.Status);
                original.Location = CleanGridText(row.Location);
                original.FuelPercent = ParseDouble(row.FuelPercent, original.FuelPercent, 0, 100);
                original.HealthPercent = ParseInt(row.HealthPercent, original.HealthPercent, 0, 100);
                original.OdometerMiles = Math.Max(0, ParseDouble(row.OdometerMiles, original.OdometerMiles, 0, double.MaxValue));
                original.UpdatedUtc = DateTimeOffset.UtcNow;

                Vm.SaveTruck(original);
                RepickTruck(truckNumber);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to save edited truck row.\n\n" + ex.Message, "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _gridSaveInFlight = false;
            }
        }

        private void SyncLocalTelemetryIntoSelectedOrAssignedTruck()
        {
            try
            {
                var app = Application.Current as App;
                var snapshot = app?.Telemetry?.LastSnapshot;
                if (snapshot == null)
                    return;

                var driverName = FirstNonEmpty(
                    ReadObjectString(snapshot, "DriverName"),
                    ReadObjectString(snapshot, "Driver"),
                    EldDriverIdentityResolver.DriverName());

                var plate = FirstNonEmpty(
                    ReadObjectString(snapshot, "Plate"),
                    ReadObjectString(snapshot, "PlateNumber"),
                    ReadObjectString(snapshot, "TruckLicensePlate"),
                    ReadObjectString(snapshot, "LicensePlate"),
                    ReadObjectString(snapshot, "LicensePlateNumber"),
                    ReadObjectString(snapshot, "TruckPlate"),
                    ReadObjectString(snapshot, "VehiclePlate"));

                var location = FirstNonEmpty(
                    ReadObjectString(snapshot, "Location"),
                    ReadObjectString(snapshot, "CityState"),
                    JoinLocation(ReadObjectString(snapshot, "City"), ReadObjectString(snapshot, "State")),
                    JoinLocation(ReadObjectString(snapshot, "CurrentCity"), ReadObjectString(snapshot, "CurrentState")),
                    JoinLocation(ReadObjectString(snapshot, "NearestCity"), ReadObjectString(snapshot, "NearestState")),
                    JoinLocation(ReadObjectString(snapshot, "GameCity"), ReadObjectString(snapshot, "GameState")));

                var fuelText = FirstNonEmpty(
                    ReadObjectString(snapshot, "FuelPercent"),
                    ReadObjectString(snapshot, "FuelPercentage"),
                    ReadObjectString(snapshot, "FuelPct"),
                    ReadObjectString(snapshot, "Fuel"),
                    ReadObjectString(snapshot, "FuelLevel"),
                    ReadObjectString(snapshot, "FuelRatio"));

                var odometerText = FirstNonEmpty(
                    ReadObjectString(snapshot, "OdometerMiles"),
                    ReadObjectString(snapshot, "OdometerMi"),
                    ReadObjectString(snapshot, "Odometer"),
                    ReadObjectString(snapshot, "Mileage"),
                    ReadObjectString(snapshot, "Miles"),
                    ReadObjectString(snapshot, "OdometerKm"),
                    ReadObjectString(snapshot, "OdometerKilometers"));

                var conditionText = FirstNonEmpty(
                    ReadObjectString(snapshot, "ConditionPercent"),
                    ReadObjectString(snapshot, "HealthPercent"),
                    ReadObjectString(snapshot, "ConditionPct"),
                    ReadObjectString(snapshot, "Condition"),
                    ReadObjectString(snapshot, "Health"),
                    ReadObjectString(snapshot, "TruckHealthPercent"));

                var damageText = FirstNonEmpty(
                    ReadObjectString(snapshot, "DamagePct"),
                    ReadObjectString(snapshot, "TruckDamagePct"),
                    ReadObjectString(snapshot, "DamagePercent"),
                    ReadObjectString(snapshot, "TruckDamagePercent"),
                    ReadObjectString(snapshot, "Damage"),
                    ReadObjectString(snapshot, "WearPct"),
                    ReadObjectString(snapshot, "WearPercent"),
                    ReadObjectString(snapshot, "TruckDamage"),
                    ReadObjectString(snapshot, "TruckWear"),
                    ReadObjectString(snapshot, "TruckWearPercent"),
                    ReadObjectString(snapshot, "ChassisWear"),
                    ReadObjectString(snapshot, "EngineWear"),
                    ReadObjectString(snapshot, "TransmissionWear"),
                    ReadObjectString(snapshot, "CabinWear"),
                    ReadObjectString(snapshot, "WheelsWear"));

                var truckName = FirstNonEmpty(
                    ReadObjectString(snapshot, "TruckName"),
                    ReadObjectString(snapshot, "VehicleName"),
                    ReadObjectString(snapshot, "Truck"),
                    ReadObjectString(snapshot, "Vehicle"),
                    ReadObjectString(snapshot, "TruckId"),
                    ReadObjectString(snapshot, "UnitNumber"));

                var make = FirstNonEmpty(
                    ReadObjectString(snapshot, "TruckMake"),
                    ReadObjectString(snapshot, "Make"),
                    ReadObjectString(snapshot, "Brand"),
                    ReadObjectString(snapshot, "TruckBrand"),
                    ReadObjectString(snapshot, "Manufacturer"));

                var model = FirstNonEmpty(
                    ReadObjectString(snapshot, "TruckModel"),
                    ReadObjectString(snapshot, "Model"),
                    ReadObjectString(snapshot, "TruckMakeModel"),
                    ReadObjectString(snapshot, "MakeModel"));

                Vm.Refresh();

                FleetCommandCenterViewModel.FleetCommandTruckRow? row = null;

                if (!string.IsNullOrWhiteSpace(plate))
                    row = Vm.Trucks.FirstOrDefault(x => string.Equals((x.PlateNumber ?? "").Trim(), plate.Trim(), StringComparison.OrdinalIgnoreCase));

                if (row == null && !string.IsNullOrWhiteSpace(driverName))
                    row = Vm.Trucks.FirstOrDefault(x => string.Equals((x.AssignedDriver ?? "").Trim(), driverName.Trim(), StringComparison.OrdinalIgnoreCase));

                row ??= Vm.SelectedTruck;

                if (row == null)
                    return;

                Vm.SelectedTruck = row;
                var truck = Vm.LoadSelectedFull();
                if (truck == null)
                    return;

                // Telemetry-controlled fields. Do not wipe manager-controlled truck #, assigned driver, or load.
                if (!string.IsNullOrWhiteSpace(plate))
                    truck.PlateNumber = plate.Trim();

                if (!string.IsNullOrWhiteSpace(location))
                    truck.Location = location.Trim();

                if (TryParseDoubleLoose(fuelText, out var fuel))
                {
                    if (fuel > 0 && fuel <= 1)
                        fuel *= 100;

                    truck.FuelPercent = ClampDouble(fuel, 0, 100);
                }

                if (TryParseDoubleLoose(odometerText, out var odo))
                    truck.OdometerMiles = Math.Max(0, odo);

                if (TryParseDoubleLoose(conditionText, out var condition))
                {
                    if (condition > 0 && condition <= 1)
                        condition *= 100;

                    truck.HealthPercent = (int)Math.Round(ClampDouble(condition, 0, 100));
                }
                else if (TryParseDoubleLoose(damageText, out var damage))
                {
                    if (damage > 0 && damage <= 1)
                        damage *= 100;

                    truck.HealthPercent = (int)Math.Round(ClampDouble(100 - damage, 0, 100));
                }

                if (string.IsNullOrWhiteSpace(truck.TruckName) && !string.IsNullOrWhiteSpace(truckName))
                    truck.TruckName = truckName.Trim();

                var makeModel = BuildMakeModelText(make, model);
                if (string.IsNullOrWhiteSpace(truck.Model) && !string.IsNullOrWhiteSpace(makeModel))
                    truck.Model = makeModel;

                if (string.IsNullOrWhiteSpace(truck.AssignedDriver) && !string.IsNullOrWhiteSpace(driverName))
                    truck.AssignedDriver = driverName.Trim();

                truck.Status = "Active";
                truck.UpdatedUtc = DateTimeOffset.UtcNow;

                Vm.SaveTruck(truck);
            }
            catch
            {
            }
        }

        private void PurgeFakeDriverRows()
        {
            try
            {
                Vm.Refresh();

                var fakeRows = Vm.Trucks
                    .Where(row =>
                    {
                        var truckNumber = ReadObjectString(row, "TruckNumber");
                        var truck = ReadObjectString(row, "Truck");
                        var model = ReadObjectString(row, "Model");
                        var assignedDriver = ReadObjectString(row, "AssignedDriver");

                        return truckNumber.StartsWith("DRV-", StringComparison.OrdinalIgnoreCase) &&
                               (
                                   truck.Equals("Driver Presence", StringComparison.OrdinalIgnoreCase) ||
                                   model.Equals("Driver Presence", StringComparison.OrdinalIgnoreCase) ||
                                   assignedDriver.Equals("User", StringComparison.OrdinalIgnoreCase)
                               );
                    })
                    .ToList();

                foreach (var row in fakeRows)
                {
                    Vm.SelectedTruck = row;
                    Vm.DeleteSelected();
                }

                Vm.Refresh();
            }
            catch
            {
            }
        }

        private async Task LoadDiscordDriversAsync()
        {
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var cfg = VtcConfigService.Load();
                var drivers = await _driverDirectory.LoadDriversAsync(cfg.BotApiBaseUrl ?? "");

                foreach (var d in drivers)
                {
                    var name = d?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(name) &&
                        !name.Equals("Unassigned", StringComparison.OrdinalIgnoreCase))
                    {
                        set.Add(name);
                    }
                }
            }
            catch { }

            try
            {
                foreach (var d in DiscordIdentityStore.LoadAll())
                {
                    var name = FirstNonEmpty(d.DiscordUsername, d.DiscordUserId);
                    if (!string.IsNullOrWhiteSpace(name))
                        set.Add(name.Trim());
                }
            }
            catch { }

            try
            {
                foreach (var d in DispatchService.Drivers)
                {
                    if (!string.IsNullOrWhiteSpace(d) &&
                        !d.Equals("Unassigned", StringComparison.OrdinalIgnoreCase))
                    {
                        set.Add(d.Trim());
                    }
                }
            }
            catch { }

            try
            {
                foreach (var d in Vm.DriverOptions)
                {
                    if (!string.IsNullOrWhiteSpace(d) &&
                        !d.Equals("Unassigned", StringComparison.OrdinalIgnoreCase))
                    {
                        set.Add(d.Trim());
                    }
                }
            }
            catch { }

            try
            {
                foreach (var row in Vm.Trucks)
                {
                    var name = ReadObjectString(row, "AssignedDriver");

                    if (!string.IsNullOrWhiteSpace(name) &&
                        !name.Equals("Unassigned", StringComparison.OrdinalIgnoreCase))
                    {
                        set.Add(name.Trim());
                    }
                }
            }
            catch { }

            _driverNames = set
                .Concat(DriverDropdownService.LoadDriverNames(includeUnassigned: false))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            DriverDropdownService.Bind(DriverBox, includeUnassigned: true);
        }

        private async Task PullVtcPresenceIntoFleetAsync()
        {
            try
            {
                var cfg = VtcConfigService.Load();
                var pairing = VtcPairingStore.Load();

                var baseUrl = (cfg.BotApiBaseUrl ?? "https://overwatcheld.up.railway.app").Trim().TrimEnd('/');
                var guildId = FirstNonEmpty(pairing?.GuildId, cfg.Discord?.GuildId);

                if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(guildId))
                    return;

                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };

                var url = $"{baseUrl}/api/drivers/presence?guildId={Uri.EscapeDataString(guildId)}";
                var json = await http.GetStringAsync(url);

                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("drivers", out var drivers) ||
                    drivers.ValueKind != JsonValueKind.Array)
                    return;

                foreach (var driver in drivers.EnumerateArray())
                {
                    var discordUserId = JsonString(driver, "discordUserId", "DiscordUserId", "userId", "UserId");
                    var driverName = JsonString(driver, "driverName", "DriverName", "discordUsername", "DiscordUsername", "username", "Username", "name", "Name");
                    var status = JsonString(driver, "status", "Status", "presenceStatus", "PresenceStatus");
                    var isOnline = JsonBool(driver, "isOnline", "IsOnline", "online", "Online");

                    if (!string.IsNullOrWhiteSpace(status))
                    {
                        isOnline =
                            status.Equals("Online", StringComparison.OrdinalIgnoreCase) ||
                            status.Equals("Active", StringComparison.OrdinalIgnoreCase) ||
                            status.Equals("Driving", StringComparison.OrdinalIgnoreCase);
                    }

                    if (string.IsNullOrWhiteSpace(driverName) && string.IsNullOrWhiteSpace(discordUserId))
                        continue;

                    var match = FindTruckRowForDriver(driverName, discordUserId);

                    if (match == null)
                        continue;

                    Vm.SelectedTruck = match;

                    var truck = Vm.LoadSelectedFull();
                    if (truck == null)
                        continue;

                    if (!string.IsNullOrWhiteSpace(driverName))
                        truck.AssignedDriver = driverName;

                    if (!string.IsNullOrWhiteSpace(discordUserId))
                        truck.DriverDiscordId = discordUserId;

                    ApplyLiveStatusAndLocation(truck, isOnline);

                    Vm.SaveTruck(truck);
                }
            }
            catch { }
        }

        private void ApplyLiveStatusAndLocation(object truck, bool isOnline)
        {
            try
            {
                var app = Application.Current as App;
                var snapshot = app?.Telemetry?.LastSnapshot;

                if (snapshot != null)
                    isOnline = true;

                var currentLocation = ReadObjectString(truck, "Location");

                var liveLocation = FirstNonEmpty(
                    ReadObjectString(snapshot, "CityState"),
                    JoinLocation(ReadObjectString(snapshot, "City"), ReadObjectString(snapshot, "State")),
                    JoinLocation(ReadObjectString(snapshot, "CurrentCity"), ReadObjectString(snapshot, "CurrentState")),
                    JoinLocation(ReadObjectString(snapshot, "NearestCity"), ReadObjectString(snapshot, "NearestState")),
                    JoinLocation(ReadObjectString(snapshot, "GameCity"), ReadObjectString(snapshot, "GameState")),
                    currentLocation);

                SetObjectString(truck, "Status", isOnline ? "Active" : "Offline");
                SetObjectString(truck, "Location", isOnline ? FirstNonEmpty(liveLocation, "Driving") : "Offline");
            }
            catch
            {
                SetObjectString(truck, "Status", isOnline ? "Active" : "Offline");
            }
        }

        private FleetCommandCenterViewModel.FleetCommandTruckRow? FindTruckRowForDriver(string driverName, string discordUserId)
        {
            try
            {
                Vm.Refresh();

                foreach (var row in Vm.Trucks)
                {
                    var truckNumber = ReadObjectString(row, "TruckNumber");
                    if (truckNumber.StartsWith("DRV-", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var assignedDriver = ReadObjectString(row, "AssignedDriver");
                    var rowDiscordId = ReadObjectString(row, "DriverDiscordId");

                    if (!string.IsNullOrWhiteSpace(discordUserId) &&
                        rowDiscordId.Equals(discordUserId, StringComparison.OrdinalIgnoreCase))
                        return row;

                    if (!string.IsNullOrWhiteSpace(driverName) &&
                        assignedDriver.Equals(driverName, StringComparison.OrdinalIgnoreCase))
                        return row;
                }
            }
            catch { }

            return null;
        }

        private void TruckGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PopulateFormFromSelectedTruck();
        }

        private void PopulateFormFromSelectedTruck()
        {
            try
            {
                var selected = Vm.LoadSelectedFull();
                if (selected == null)
                    return;

                var selectedTruckNumber = ReadObjectString(selected, "TruckNumber");
                var selectedAssignedDriver = ReadObjectString(selected, "AssignedDriver");
                var selectedLoadNumber = FirstNonEmpty(
                    ReadObjectString(selected, "CurrentLoadNumber"),
                    ReadObjectString(selected, "LoadNumber"),
                    ReadObjectString(selected, "LoadNo"),
                    ReadObjectString(selected, "AssignedLoad"),
                    ReadObjectString(selected, "LoadId"));

                TruckNumberBox.Text = selectedTruckNumber;
                SetComboSelectionOrText(TruckNumberBox, selectedTruckNumber);

                DriverDropdownService.Select(DriverBox, selectedAssignedDriver);

                LoadNumberBox.Text = selectedLoadNumber;
                SetComboSelectionOrText(LoadNumberBox, selectedLoadNumber);

                TruckNameBox.Text = ReadObjectString(selected, "TruckName");
                ModelBox.Text = ReadObjectString(selected, "Model");
                PlateBox.Text = ReadObjectString(selected, "PlateNumber");
                LocationBox.Text = ReadObjectString(selected, "Location");

                var healthText = ReadObjectString(selected, "HealthPercent");
                var fuelText = ReadObjectString(selected, "FuelPercent");
                var odometerText = ReadObjectString(selected, "OdometerMiles");

                ConditionBox.Text = int.TryParse(healthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hp)
                    ? Math.Clamp(hp, 0, 100).ToString(CultureInfo.InvariantCulture)
                    : "100";

                FuelBox.Text = double.TryParse(fuelText, NumberStyles.Float, CultureInfo.InvariantCulture, out var fuel)
                    ? ClampDouble(fuel, 0, 100).ToString("0.##", CultureInfo.InvariantCulture)
                    : "100";

                OdometerBox.Text = double.TryParse(odometerText, NumberStyles.Float, CultureInfo.InvariantCulture, out var odo)
                    ? Math.Max(0, odo).ToString("0.##", CultureInfo.InvariantCulture)
                    : "0";
            }
            catch { }
        }

        private static void SetComboSelectionOrText(ComboBox box, string? value)
        {
            var text = (value ?? "").Trim();

            if (string.IsNullOrWhiteSpace(text))
            {
                box.SelectedItem = null;
                box.Text = "";
                return;
            }

            if (box.ItemsSource != null)
            {
                foreach (var item in box.ItemsSource)
                {
                    if (item is string s && string.Equals(s.Trim(), text, StringComparison.OrdinalIgnoreCase))
                    {
                        box.SelectedItem = item;
                        box.Text = s;
                        return;
                    }

                    var truckNumber = ReadObjectString(item, "TruckNumber");
                    if (!string.IsNullOrWhiteSpace(truckNumber) &&
                        string.Equals(truckNumber, text, StringComparison.OrdinalIgnoreCase))
                    {
                        box.SelectedItem = item;
                        box.Text = text;
                        return;
                    }
                }
            }

            box.SelectedItem = null;
            box.Text = text;
        }

        private string NormalizeTruckNumberInput(bool useNextAvailableWhenBlank = false)
        {
            var truckNumber = (TruckNumberBox.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(truckNumber))
                truckNumber = ReadComboText(TruckNumberBox);

            truckNumber = (truckNumber ?? "").Trim();

            if (useNextAvailableWhenBlank && string.IsNullOrWhiteSpace(truckNumber))
                truckNumber = GetNextTruckNumberText();

            return truckNumber;
        }

        private string GetNextTruckNumberText()
        {
            try
            {
                var vmNext = Vm.GetNextAvailableTruckNumber();
                if (!string.IsNullOrWhiteSpace(vmNext))
                    return vmNext.Trim();
            }
            catch { }

            return "001";
        }

        private void ApplyTruckFormFields(FleetCommandTruck truck, bool preserveExistingWhenBlank = false)
        {
            if (truck == null)
                return;

            var truckName = (TruckNameBox.Text ?? "").Trim();
            var model = (ModelBox.Text ?? "").Trim();
            var plate = (PlateBox.Text ?? "").Trim();
            var location = (LocationBox.Text ?? "").Trim();

            if (!preserveExistingWhenBlank || !string.IsNullOrWhiteSpace(truckName)) truck.TruckName = truckName;
            if (!preserveExistingWhenBlank || !string.IsNullOrWhiteSpace(model)) truck.Model = model;
            if (!preserveExistingWhenBlank || !string.IsNullOrWhiteSpace(plate)) truck.PlateNumber = plate;
            if (!preserveExistingWhenBlank || !string.IsNullOrWhiteSpace(location)) truck.Location = location;

            truck.HealthPercent = ParseInt(ConditionBox.Text, truck.HealthPercent <= 0 ? 100 : truck.HealthPercent, 0, 100);
            truck.FuelPercent = ParseDouble(FuelBox.Text, truck.FuelPercent <= 0 ? 100 : truck.FuelPercent, 0, 100);
            truck.OdometerMiles = Math.Max(0, ParseDouble(OdometerBox.Text, truck.OdometerMiles, 0, double.MaxValue));

            if (truck.ServiceDueDate == null)
                truck.ServiceDueDate = DateTime.Today.AddDays(14);

            if (truck.InspectionDueDate == null)
                truck.InspectionDueDate = DateTime.Today.AddDays(7);
        }

        private bool ValidateTruckNumberForManualAction(string truckNumber)
        {
            if (string.IsNullOrWhiteSpace(truckNumber))
            {
                MessageBox.Show("Truck ID is required.", "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (truckNumber.StartsWith("DRV-", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Truck ID must be the real assigned truck number, not a DRV placeholder.", "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private void AddTruck_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var truckNumber = NormalizeTruckNumberInput(useNextAvailableWhenBlank: true);
                if (!ValidateTruckNumberForManualAction(truckNumber))
                    return;

                if (Vm.TruckExists(truckNumber))
                {
                    MessageBox.Show($"Truck {truckNumber} is already registered. Use Update Truck instead.", "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var truck = Vm.CreateOrGetTruck(truckNumber);
                ApplyTruckFormFields(truck);
                Vm.SaveTruck(truck);
                RepickTruck(truckNumber);

                MessageBox.Show($"Truck {truckNumber} was added.", "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Add Truck failed.\n\n" + ex.Message, "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateTruck_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var truckNumber = NormalizeTruckNumberInput();
                if (!ValidateTruckNumberForManualAction(truckNumber))
                    return;

                var truck = Vm.GetTruckByNumber(truckNumber);
                if (truck == null)
                {
                    MessageBox.Show($"Truck {truckNumber} is not registered yet. Use Add Truck first.", "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                ApplyTruckFormFields(truck);
                Vm.SaveTruck(truck);
                RepickTruck(truckNumber);

                MessageBox.Show($"Truck {truckNumber} was updated.", "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Update Truck failed.\n\n" + ex.Message, "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddOrUpdateTruck_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var truckNumber = NormalizeTruckNumberInput(useNextAvailableWhenBlank: true);

                if (string.IsNullOrWhiteSpace(truckNumber))
                {
                    MessageBox.Show("Truck ID is required.", "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (truckNumber.StartsWith("DRV-", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("Truck ID must be the real assigned truck number, not a DRV placeholder.", "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var truck = Vm.CreateOrGetTruck(truckNumber);
                if (truck == null)
                {
                    MessageBox.Show("Unable to create or load that truck record.", "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                truck.TruckName = (TruckNameBox.Text ?? "").Trim();
                truck.Model = (ModelBox.Text ?? "").Trim();
                truck.PlateNumber = (PlateBox.Text ?? "").Trim();
                truck.Location = (LocationBox.Text ?? "").Trim();
                truck.HealthPercent = ParseInt(ConditionBox.Text, 100, 0, 100);
                truck.FuelPercent = ParseDouble(FuelBox.Text, 100, 0, 100);
                truck.OdometerMiles = Math.Max(0, ParseDouble(OdometerBox.Text, 0, 0, double.MaxValue));

                if (truck.ServiceDueDate == null)
                    truck.ServiceDueDate = DateTime.Today.AddDays(14);

                if (truck.InspectionDueDate == null)
                    truck.InspectionDueDate = DateTime.Today.AddDays(7);

                var driverName = ReadSelectedDriverName(DriverBox);
                var driverDiscordId = ReadSelectedDriverDiscordId(DriverBox);
                var loadNumber = ReadComboText(LoadNumberBox);

                Vm.SaveTruck(truck);

                if (!string.IsNullOrWhiteSpace(driverName))
                    Vm.AssignTruck(truck, driverName, loadNumber, driverDiscordId);
                else
                    Vm.Refresh();

                RepickTruck(truckNumber);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Add / Update Truck failed.\n\n" + ex.Message, "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddFromTelemetry_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var capture = TelemetryTruckCaptureService.Capture();

                var app = Application.Current as App;
                var liveSnapshot = app?.Telemetry?.LastSnapshot;

                if (capture == null && liveSnapshot == null)
                {
                    MessageBox.Show("No telemetry data available.\n\nStart ATS and the telemetry reader first.", "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var truckNumber = NormalizeTruckNumberInput(useNextAvailableWhenBlank: true);
                if (string.IsNullOrWhiteSpace(truckNumber))
                    truckNumber = GetNextTruckNumberText();

                TruckNumberBox.Text = truckNumber;
                SetComboSelectionOrText(TruckNumberBox, truckNumber);

                if (Vm.TruckExists(truckNumber))
                {
                    MessageBox.Show($"Truck {truckNumber} is already registered. Use Update Truck instead.", "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (capture != null)
                    ApplyTelemetryCaptureToForm(capture);

                if (liveSnapshot != null)
                    ApplyTelemetryCaptureToForm(liveSnapshot);

                if (IsFleetTelemetryFormEmpty())
                {
                    MessageBox.Show("Telemetry is running, but no truck details were found yet.\n\nLoad into ATS, enter a truck, then try Add From Telemetry again.", "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var truck = Vm.CreateOrGetTruck(truckNumber);
                if (truck == null)
                {
                    MessageBox.Show("Unable to create or load that truck record.", "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                truck.TruckName = (TruckNameBox.Text ?? "").Trim();
                truck.Model = (ModelBox.Text ?? "").Trim();
                truck.PlateNumber = (PlateBox.Text ?? "").Trim();
                truck.Location = (LocationBox.Text ?? "").Trim();
                truck.HealthPercent = ParseInt(ConditionBox.Text, 100, 0, 100);
                truck.FuelPercent = ParseDouble(FuelBox.Text, 100, 0, 100);
                truck.OdometerMiles = Math.Max(0, ParseDouble(OdometerBox.Text, 0, 0, double.MaxValue));

                if (truck.ServiceDueDate == null)
                    truck.ServiceDueDate = DateTime.Today.AddDays(14);

                if (truck.InspectionDueDate == null)
                    truck.InspectionDueDate = DateTime.Today.AddDays(7);

                Vm.SaveTruck(truck);
                RepickTruck(truckNumber);

                MessageBox.Show("Telemetry truck imported. Use Assign Driver or Assign Load when ready.", "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Add From Telemetry failed.\n\n" + ex, "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool IsFleetTelemetryFormEmpty()
        {
            return string.IsNullOrWhiteSpace(TruckNameBox.Text) &&
                   string.IsNullOrWhiteSpace(ModelBox.Text) &&
                   string.IsNullOrWhiteSpace(PlateBox.Text) &&
                   string.IsNullOrWhiteSpace(LocationBox.Text) &&
                   string.IsNullOrWhiteSpace(DriverDropdownService.SelectedName(DriverBox));
        }

        private void ApplyTelemetryCaptureToForm(object capture)
        {
            var truckName = FirstNonEmpty(ReadObjectString(capture, "TruckName"), ReadObjectString(capture, "VehicleName"), ReadObjectString(capture, "Truck"), ReadObjectString(capture, "Vehicle"), ReadObjectString(capture, "TruckId"), ReadObjectString(capture, "UnitNumber"));
            var truckMake = FirstNonEmpty(ReadObjectString(capture, "TruckMake"), ReadObjectString(capture, "Make"), ReadObjectString(capture, "Brand"), ReadObjectString(capture, "TruckBrand"), ReadObjectString(capture, "TruckManufacturer"), ReadObjectString(capture, "Manufacturer"));
            var truckModel = FirstNonEmpty(ReadObjectString(capture, "TruckModel"), ReadObjectString(capture, "Model"), ReadObjectString(capture, "TruckMakeModel"), ReadObjectString(capture, "MakeModel"), ReadObjectString(capture, "TruckMakeAndModel"));
            var driverName = FirstNonEmpty(ReadObjectString(capture, "DriverName"), ReadObjectString(capture, "Driver"), ReadObjectString(capture, "DiscordUsername"), ReadObjectString(capture, "Username"));
            var plate = FirstNonEmpty(ReadObjectString(capture, "Plate"), ReadObjectString(capture, "PlateNumber"), ReadObjectString(capture, "TruckLicensePlate"), ReadObjectString(capture, "LicensePlate"), ReadObjectString(capture, "LicensePlateNumber"), ReadObjectString(capture, "TruckPlate"), ReadObjectString(capture, "VehiclePlate"));
            var location = FirstNonEmpty(ReadObjectString(capture, "Location"), ReadObjectString(capture, "CityState"), JoinLocation(ReadObjectString(capture, "City"), ReadObjectString(capture, "State")), JoinLocation(ReadObjectString(capture, "NearestCity"), ReadObjectString(capture, "NearestState")), JoinLocation(ReadObjectString(capture, "GameCity"), ReadObjectString(capture, "GameState")), JoinLocation(ReadObjectString(capture, "CurrentCity"), ReadObjectString(capture, "CurrentState")));
            var fuelText = FirstNonEmpty(ReadObjectString(capture, "FuelPercent"), ReadObjectString(capture, "FuelPercentage"), ReadObjectString(capture, "FuelPct"), ReadObjectString(capture, "Fuel"), ReadObjectString(capture, "FuelLevel"), ReadObjectString(capture, "FuelRatio"));
            var conditionText = FirstNonEmpty(ReadObjectString(capture, "ConditionPercent"), ReadObjectString(capture, "HealthPercent"), ReadObjectString(capture, "ConditionPct"), ReadObjectString(capture, "Condition"), ReadObjectString(capture, "Health"), ReadObjectString(capture, "TruckHealthPercent"));
            var damageText = FirstNonEmpty(ReadObjectString(capture, "DamagePct"), ReadObjectString(capture, "TruckDamagePct"), ReadObjectString(capture, "DamagePercent"), ReadObjectString(capture, "TruckDamagePercent"), ReadObjectString(capture, "Damage"), ReadObjectString(capture, "WearPct"), ReadObjectString(capture, "WearPercent"), ReadObjectString(capture, "TruckDamage"), ReadObjectString(capture, "TruckWear"), ReadObjectString(capture, "TruckWearPercent"), ReadObjectString(capture, "ChassisWear"), ReadObjectString(capture, "EngineWear"), ReadObjectString(capture, "TransmissionWear"), ReadObjectString(capture, "CabinWear"), ReadObjectString(capture, "WheelsWear"));
            var odometerText = FirstNonEmpty(ReadObjectString(capture, "OdometerMiles"), ReadObjectString(capture, "OdometerMi"), ReadObjectString(capture, "Odometer"), ReadObjectString(capture, "Mileage"), ReadObjectString(capture, "Miles"), ReadObjectString(capture, "OdometerKm"), ReadObjectString(capture, "OdometerKilometers"));

            if (!string.IsNullOrWhiteSpace(truckName))
                TruckNameBox.Text = truckName;

            var makeModel = BuildMakeModelText(truckMake, truckModel);
            if (!string.IsNullOrWhiteSpace(makeModel))
                ModelBox.Text = makeModel;

            if (!string.IsNullOrWhiteSpace(driverName))
                DriverDropdownService.Select(DriverBox, driverName);

            if (!string.IsNullOrWhiteSpace(plate))
                PlateBox.Text = plate;

            if (!string.IsNullOrWhiteSpace(location))
                LocationBox.Text = location;

            if (TryParseDoubleLoose(fuelText, out var fuel))
            {
                if (fuel > 0 && fuel <= 1)
                    fuel *= 100;

                FuelBox.Text = ClampDouble(fuel, 0, 100).ToString("0.##", CultureInfo.InvariantCulture);
            }

            if (TryParseDoubleLoose(conditionText, out var condition))
            {
                if (condition > 0 && condition <= 1)
                    condition *= 100;

                ConditionBox.Text = ClampDouble(condition, 0, 100).ToString("0.##", CultureInfo.InvariantCulture);
            }
            else if (TryParseDoubleLoose(damageText, out var damage))
            {
                if (damage > 0 && damage <= 1)
                    damage *= 100;

                ConditionBox.Text = ClampDouble(100 - damage, 0, 100).ToString("0.##", CultureInfo.InvariantCulture);
            }

            if (TryParseDoubleLoose(odometerText, out var odo))
                OdometerBox.Text = Math.Max(0, odo).ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string BuildMakeModelText(string? make, string? model)
        {
            make = (make ?? "").Trim();
            model = (model ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(make) &&
                !string.IsNullOrWhiteSpace(model) &&
                !model.Contains(make, StringComparison.OrdinalIgnoreCase))
                return $"{make} {model}".Trim();

            return FirstNonEmpty(model, make);
        }

        private async void AssignDriverOnly_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var truckNumber = NormalizeTruckNumberInput();
                var driverName = ReadSelectedDriverName(DriverBox);
                var driverDiscordId = ReadSelectedDriverDiscordId(DriverBox);

                if (!ValidateTruckNumberForManualAction(truckNumber))
                    return;

                if (string.IsNullOrWhiteSpace(driverName))
                {
                    MessageBox.Show("Choose a driver first.", "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var truck = Vm.GetTruckByNumber(truckNumber);
                if (truck == null)
                {
                    MessageBox.Show($"Truck {truckNumber} is not registered yet. Use Add Truck first.", "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                ApplyTruckFormFields(truck, preserveExistingWhenBlank: true);
                Vm.SaveTruck(truck);

                truck = Vm.GetTruckByNumber(truckNumber);
                if (truck == null)
                    return;

                truck = Vm.AssignTruckByNumber(truckNumber, driverName, truck.CurrentLoadNumber ?? "", driverDiscordId) ?? truck;
                Vm.Refresh();
                await LoadDiscordDriversAsync();
                RepickTruck(truckNumber);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Assign Driver failed.\n\n" + ex.Message, "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AssignLoad_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var truckNumber = NormalizeTruckNumberInput();
                var loadNumber = ReadComboText(LoadNumberBox);

                if (!ValidateTruckNumberForManualAction(truckNumber))
                    return;

                if (string.IsNullOrWhiteSpace(loadNumber))
                {
                    MessageBox.Show("Enter or choose a load number first.", "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var truck = Vm.GetTruckByNumber(truckNumber);
                if (truck == null)
                {
                    MessageBox.Show($"Truck {truckNumber} is not registered yet. Use Add Truck first.", "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                ApplyTruckFormFields(truck, preserveExistingWhenBlank: true);
                Vm.SaveTruck(truck);

                truck = Vm.GetTruckByNumber(truckNumber);
                if (truck == null)
                    return;

                var driverName = ReadSelectedDriverName(DriverBox);
                var driverDiscordId = ReadSelectedDriverDiscordId(DriverBox);
                if (string.IsNullOrWhiteSpace(driverName))
                    driverName = truck.AssignedDriver ?? "";

                truck = Vm.AssignTruckByNumber(truckNumber, driverName, loadNumber, driverDiscordId) ?? truck;
                RepickTruck(truckNumber);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Assign Load failed.\n\n" + ex.Message, "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void AssignDriver_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var truckNumber = NormalizeTruckNumberInput();
                var driverName = ReadSelectedDriverName(DriverBox);
                var driverDiscordId = ReadSelectedDriverDiscordId(DriverBox);
                var loadNumber = ReadComboText(LoadNumberBox);

                if (string.IsNullOrWhiteSpace(truckNumber))
                {
                    MessageBox.Show("Choose or type a Truck ID first.", "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrWhiteSpace(driverName))
                {
                    MessageBox.Show("Choose or type a driver first.", "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var truck = Vm.CreateOrGetTruck(truckNumber);
                if (truck == null)
                {
                    MessageBox.Show("Unable to create or load that truck record.", "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var typedTruckName = (TruckNameBox.Text ?? "").Trim();
                var typedModel = (ModelBox.Text ?? "").Trim();
                var typedPlate = (PlateBox.Text ?? "").Trim();
                var typedLocation = (LocationBox.Text ?? "").Trim();

                if (!string.IsNullOrWhiteSpace(typedTruckName)) truck.TruckName = typedTruckName;
                if (!string.IsNullOrWhiteSpace(typedModel)) truck.Model = typedModel;
                if (!string.IsNullOrWhiteSpace(typedPlate)) truck.PlateNumber = typedPlate;
                if (!string.IsNullOrWhiteSpace(typedLocation)) truck.Location = typedLocation;

                truck.HealthPercent = ParseInt(ConditionBox.Text, truck.HealthPercent, 0, 100);
                truck.FuelPercent = ParseDouble(FuelBox.Text, truck.FuelPercent, 0, 100);
                truck.OdometerMiles = Math.Max(0, ParseDouble(OdometerBox.Text, truck.OdometerMiles, 0, double.MaxValue));

                if (truck.ServiceDueDate == null)
                    truck.ServiceDueDate = DateTime.Today.AddDays(14);

                if (truck.InspectionDueDate == null)
                    truck.InspectionDueDate = DateTime.Today.AddDays(7);

                Vm.SaveTruck(truck);

                truck = Vm.CreateOrGetTruck(truckNumber);
                if (truck == null)
                {
                    MessageBox.Show("Truck record could not be reloaded after save.", "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                truck = Vm.AssignTruckByNumber(truckNumber, driverName, loadNumber, driverDiscordId) ?? truck;

                Vm.Refresh();
                await LoadDiscordDriversAsync();
                RepickTruck(truckNumber);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Assign Driver + Load failed.\n\n" + ex.Message, "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UnassignDriver_Click(object sender, RoutedEventArgs e)
        {
            var selected = Vm.LoadSelectedFull();
            if (selected == null)
            {
                MessageBox.Show("Select a truck first.", "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var truckNumber = ReadObjectString(selected, "TruckNumber");

            if (MessageBox.Show($"Unassign driver from truck {truckNumber}?", "Fleet Command Center", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            Vm.UnassignDriver(selected);
            RepickTruck(truckNumber);
        }

        private void DeleteTruck_Click(object sender, RoutedEventArgs e)
        {
            if (Vm.SelectedTruck == null)
            {
                MessageBox.Show("Select a truck first.", "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selectedTruckNumber = ReadObjectString(Vm.SelectedTruck, "TruckNumber");

            if (MessageBox.Show($"Delete truck {selectedTruckNumber}?", "Fleet Command Center", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            ClearEditorForm();
            Vm.DeleteSelected();
        }

        private void MarkInspection_Click(object sender, RoutedEventArgs e)
        {
            var selected = Vm.LoadSelectedFull();
            if (selected == null)
            {
                MessageBox.Show("Select a truck first.", "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var truckNumber = ReadObjectString(selected, "TruckNumber");
            Vm.MarkInspectionComplete(selected);
            RepickTruck(truckNumber);
        }

        private void MarkService_Click(object sender, RoutedEventArgs e)
        {
            var selected = Vm.LoadSelectedFull();
            if (selected == null)
            {
                MessageBox.Show("Select a truck first.", "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var truckNumber = ReadObjectString(selected, "TruckNumber");
            Vm.MarkServiceComplete(selected);
            RepickTruck(truckNumber);
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            var currentTruck = NormalizeTruckNumberInput();
            await RefreshFleetCommandAsync();
            RepickTruck(currentTruck);
        }

        private void TruckGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                var selected = Vm.LoadSelectedFull();

                if (selected == null)
                {
                    MessageBox.Show("Select a truck first.", "Maintenance History", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var win = new FleetTruckMaintenanceHistoryWindow(selected) { Owner = this };
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to open maintenance history.\n\n" + ex.Message, "Maintenance History", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }



        private void OpenReceiptTickets_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new ExpenseReceiptsWindow
                {
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                win.ShowDialog();
                Vm.Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to open Receipt / Ticket History.\n\n" + ex.Message,
                    "Receipt / Ticket History",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OpenFleetBols_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_canOpenAllBols)
                {
                    MessageBox.Show("Only Owners, Admins, and Dispatchers can open all driver BOLs from Fleet Command.", "Fleet Command BOLs", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var win = new BolWindow(forceViewAllBols: true)
                {
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                win.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to open Fleet BOLs.\n\n" + ex.Message, "Fleet Command BOLs", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void OpenDispatchForSelectedTruck_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selected = Vm.LoadSelectedFull();
                if (selected == null)
                {
                    MessageBox.Show("Select a truck first.", "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var driverName = (selected.AssignedDriver ?? "").Trim();
                if (string.IsNullOrWhiteSpace(driverName) ||
                    driverName.Equals("Unassigned", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("This truck does not have an assigned driver yet.", "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var ctx = new DispatchLaunchContextService.DispatchTruckContext
                {
                    Recipient = driverName,
                    DiscordUserId = selected.DriverDiscordId ?? "",
                    DiscordUsername = driverName,
                    DriverName = driverName,
                    TruckId = FirstNonEmpty(ReadObjectString(selected, "Id"), ReadObjectString(selected, "TruckNumber")),
                    TruckName = FirstNonEmpty(ReadObjectString(selected, "TruckName"), ReadObjectString(selected, "Model"), ReadObjectString(selected, "TruckNumber")),
                    Model = ReadObjectString(selected, "Model"),
                    ModName = "",
                    PlateNumber = ReadObjectString(selected, "PlateNumber"),
                    IsActive =
                        string.Equals(ReadObjectString(selected, "Status"), "Active", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(ReadObjectString(selected, "Status"), "Driving", StringComparison.OrdinalIgnoreCase),
                    OdometerMiles = TryParseNullableDouble(ReadObjectString(selected, "OdometerMiles")),
                    LastSeenUtc = DateTime.UtcNow
                };

                DispatchLaunchContextService.PendingTruckContext = ctx;

                var inbox = new DispatchInboxWindow(ctx) { Owner = this };
                inbox.Show();
                inbox.Activate();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to open Dispatch Inbox.\n\n" + ex.Message, "Fleet Command Center", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static double? TryParseNullableDouble(string? text)
        {
            if (double.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return value;

            return null;
        }

        private static void SetObjectString(object? obj, string propertyName, string value)
        {
            try
            {
                if (obj == null) return;

                var prop = obj.GetType().GetProperty(propertyName);
                if (prop == null || !prop.CanWrite) return;

                prop.SetValue(obj, value ?? "");
            }
            catch
            {
            }
        }

        private void RepickTruck(string? truckNumber)
        {
            var key = (truckNumber ?? "").Trim();

            try
            {
                Vm.Refresh();

                if (!string.IsNullOrWhiteSpace(key) && Vm.Trucks != null)
                {
                    foreach (var row in Vm.Trucks)
                    {
                        var rowTruck = ReadObjectString(row, "TruckNumber");
                        if (string.Equals((rowTruck ?? "").Trim(), key, StringComparison.OrdinalIgnoreCase))
                        {
                            Vm.SelectedTruck = row;
                            break;
                        }
                    }
                }

                TruckNumberBox.Text = key;
                PopulateFormFromSelectedTruck();
            }
            catch
            {
                TruckNumberBox.Text = key;
            }
        }

        private void OpenLoadBoard_Click(object sender, RoutedEventArgs e)
        {
            var host = new Window
            {
                Title = "Dispatch Tracker",
                Content = new DispatchView(),
                Width = 1600,
                Height = 900,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = System.Windows.Media.Brushes.Black
            };

            host.ShowDialog();
        }

        private void ClearEditorForm()
        {
            TruckNumberBox.SelectedItem = null;
            TruckNumberBox.Text = "";
            DriverDropdownService.Select(DriverBox, "Unassigned");
            LoadNumberBox.SelectedItem = null;
            LoadNumberBox.Text = "";
            TruckNameBox.Text = "";
            ModelBox.Text = "";
            PlateBox.Text = "";
            LocationBox.Text = "";
            ConditionBox.Text = "100";
            FuelBox.Text = "100";
            OdometerBox.Text = "0";
        }


        private static string ReadSelectedDriverName(ComboBox box)
        {
            try
            {
                var name = DriverDropdownService.SelectedName(box);
                if (!string.IsNullOrWhiteSpace(name) &&
                    !name.Equals("Unassigned", StringComparison.OrdinalIgnoreCase))
                    return name.Trim();
            }
            catch { }

            var text = ReadComboText(box);
            return text.Equals("Unassigned", StringComparison.OrdinalIgnoreCase) ? "" : text;
        }

        private static string ReadSelectedDriverDiscordId(ComboBox box)
        {
            try
            {
                var id = DriverDropdownService.SelectedDiscordId(box);
                if (!string.IsNullOrWhiteSpace(id))
                    return id.Trim();
            }
            catch { }

            return "";
        }

        private static string ReadComboText(ComboBox box)
        {
            if (box == null)
                return "";

            if (box.SelectedValue is string sv && !string.IsNullOrWhiteSpace(sv))
                return sv.Trim();

            if (box.SelectedItem is string s && !string.IsNullOrWhiteSpace(s))
                return s.Trim();

            if (box.SelectedItem != null)
            {
                var truckNumber = ReadObjectString(box.SelectedItem, "TruckNumber");
                if (!string.IsNullOrWhiteSpace(truckNumber))
                    return truckNumber;

                var text = box.SelectedItem.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text.Trim();
            }

            return (box.Text ?? "").Trim();
        }

        private static string ReadObjectString(object? obj, string propertyName)
        {
            try
            {
                if (obj == null) return "";

                var prop = obj.GetType().GetProperty(propertyName);
                if (prop == null) return "";

                var value = prop.GetValue(obj);
                return value?.ToString()?.Trim() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static string JsonString(JsonElement element, params string[] names)
        {
            try
            {
                foreach (var name in names)
                {
                    if (element.TryGetProperty(name, out var value))
                        return value.GetString()?.Trim() ?? "";
                }
            }
            catch { }

            return "";
        }

        private static bool JsonBool(JsonElement element, params string[] names)
        {
            try
            {
                foreach (var name in names)
                {
                    if (!element.TryGetProperty(name, out var value))
                        continue;

                    if (value.ValueKind == JsonValueKind.True)
                        return true;

                    if (value.ValueKind == JsonValueKind.False)
                        return false;

                    if (value.ValueKind == JsonValueKind.String)
                    {
                        var s = value.GetString() ?? "";
                        return s.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                               s.Equals("online", StringComparison.OrdinalIgnoreCase) ||
                               s.Equals("active", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            catch { }

            return false;
        }

        private static int ParseInt(string? text, int fallback, int min, int max)
        {
            if (!int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                value = fallback;

            return Math.Clamp(value, min, max);
        }

        private static double ParseDouble(string? text, double fallback, double min, double max)
        {
            if (!double.TryParse((text ?? "").Trim().Replace("%", "").Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                value = fallback;

            if (value < min) value = min;
            if (value > max) value = max;
            return value;
        }

        private static bool TryParseDoubleLoose(string? text, out double value)
        {
            value = 0;
            var cleaned = (text ?? "").Trim().Replace("%", "").Replace(",", "");

            return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static double ClampDouble(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static string JoinLocation(string? city, string? state)
        {
            city = (city ?? "").Trim();
            state = (state ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(city) && !string.IsNullOrWhiteSpace(state))
                return $"{city}, {state}";

            return FirstNonEmpty(city, state);
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

        private static string CleanGridText(string? value)
        {
            var text = (value ?? "").Trim();
            return text == "--" ? "" : text;
        }

        private static string CleanDriverGridText(string? value)
        {
            var text = CleanGridText(value);
            return text.Equals("Unassigned", StringComparison.OrdinalIgnoreCase) ? "" : text;
        }
    }
}