using OverWatchELD.Models;
using OverWatchELD.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OverWatchELD.Views
{
    public partial class FleetManagementView : UserControl
    {
        private readonly FleetService _fleetService = new();
        private readonly DiscordIdentityService _identityService = new();
        

        private FleetTruck? _selectedTruck;
        private List<FleetTruck> _allItems = new();

        public FleetManagementView()
        {
            InitializeComponent();
            ClearInputs();
            RefreshIdentityDisplay();
            RefreshGrid();
        }

        private void RefreshIdentityDisplay()
        {
            var id = _identityService.LoadOrDefault();

            DriverTextBox.Text = string.IsNullOrWhiteSpace(id.DiscordUsername)
                ? "No linked Discord user"
                : id.DiscordUsername;

            if (StatusText != null)
            {
                if (string.IsNullOrWhiteSpace(id.DiscordUsername))
                    StatusText.Text = "Manage VTC fleet trucks and link them to Discord users. No linked Discord account found.";
                else if (string.IsNullOrWhiteSpace(id.VtcName))
                    StatusText.Text = $"Manage VTC fleet trucks and link them to Discord users. Linked driver: {id.DiscordUsername}";
                else
                    StatusText.Text = $"Manage VTC fleet trucks and link them to Discord users. Linked driver: {id.DiscordUsername} • VTC: {id.VtcName}";
            }
        }

        private bool EnsureLinkedDiscord()
        {
            var id = _identityService.LoadOrDefault();

            if (!string.IsNullOrWhiteSpace(id.DiscordUsername))
                return true;

            DriverTextBox.Text = "No linked Discord user";

            MessageBox.Show(
                "No Discord account is linked yet. Link your Discord account first before adding or editing fleet trucks.",
                "Fleet Management",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return false;
        }

        private void RefreshGrid()
        {
            _allItems = _fleetService.LoadAll() ?? new List<FleetTruck>();
            ApplySearchFilter();

            var active = _allItems.FirstOrDefault(x => x.IsActive);
            var activeText = active == null
                ? "No active truck"
                : $"Active: {active.TruckName} ({active.PlateNumber})";

            var id = _identityService.LoadOrDefault();
            var linkedText = string.IsNullOrWhiteSpace(id.DiscordUsername)
                ? "No linked Discord"
                : $"Linked Driver: {id.DiscordUsername}";

            if (StatusText != null)
                StatusText.Text = $"Fleet trucks: {_allItems.Count} • {linkedText} • {activeText}";
        }

        private void ApplySearchFilter()
        {
            var query = (SearchTextBox?.Text ?? "").Trim();

            IEnumerable<FleetTruck> filtered = _allItems;

            if (!string.IsNullOrWhiteSpace(query))
            {
                filtered = _allItems.Where(t =>
                    (t.TruckName ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (t.Model ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (t.PlateNumber ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (t.DiscordUsername ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (t.DriverName ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (t.LastSeenLocation ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (t.FleetStatus ?? "").Contains(query, StringComparison.OrdinalIgnoreCase));
            }

            FleetGrid.ItemsSource = null;
            FleetGrid.ItemsSource = filtered.ToList();
        }

        private void AddManualButton_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureLinkedDiscord())
                return;

            var id = _identityService.LoadOrDefault();

            var truckName = (TruckNameTextBox.Text ?? "").Trim();
            var model = (ModelTextBox.Text ?? "").Trim();
            var plate = (PlateTextBox.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(truckName) || string.IsNullOrWhiteSpace(model))
            {
                MessageBox.Show("Truck Name and Model are required.", "Fleet Management",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var truck = new FleetTruck
            {
                Id = Guid.NewGuid().ToString(),
                TruckName = truckName,
                Model = model,
                PlateNumber = plate,
                DiscordUserId = id.DiscordUserId ?? "",
                DiscordUsername = id.DiscordUsername ?? "",
                DriverName = string.IsNullOrWhiteSpace(id.DiscordUsername) ? "Unlinked Driver" : id.DiscordUsername,
                LastSeenUtc = DateTime.UtcNow,
                LastSeenLocation = "Manual Entry",
                IsActive = true
            };

            SetOnlyOneActive(truck.Id);

            _fleetService.AddOrUpdate(truck);
            RefreshIdentityDisplay();
            RefreshGrid();
            ClearInputs();
        }

        private void AddTelemetryButton_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureLinkedDiscord())
                return;

            var identity = _identityService.LoadOrDefault();
            var snap = TelemetryTruckCaptureService.Capture();

            if (string.IsNullOrWhiteSpace(snap.TruckName) &&
                string.IsNullOrWhiteSpace(snap.DriverName) &&
                string.IsNullOrWhiteSpace(snap.Plate))
            {
                MessageBox.Show("No active telemetry truck data was found.", "Telemetry",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var truck = new FleetTruck
            {
                Id = Guid.NewGuid().ToString(),
                TruckName = string.IsNullOrWhiteSpace(TruckNameTextBox.Text) ? snap.TruckName : TruckNameTextBox.Text.Trim(),
                Model = string.IsNullOrWhiteSpace(ModelTextBox.Text) ? "Unknown ATS Model" : ModelTextBox.Text.Trim(),
                PlateNumber = string.IsNullOrWhiteSpace(PlateTextBox.Text) ? snap.Plate : PlateTextBox.Text.Trim(),
                DiscordUserId = identity.DiscordUserId ?? "",
                DiscordUsername = identity.DiscordUsername ?? "",
                DriverName = string.IsNullOrWhiteSpace(identity.DiscordUsername)
                    ? (string.IsNullOrWhiteSpace(snap.DriverName) ? "Unlinked Driver" : snap.DriverName)
                    : identity.DiscordUsername,
                LastSeenUtc = DateTime.UtcNow,
                LastSeenLocation = "Telemetry Capture",
                OdometerMiles = snap.Odometer,
                FuelPercent = (int)Math.Round(Math.Clamp(snap.FuelPct, 0, 100)),
                HealthPercent = (int)Math.Round(Math.Clamp(snap.ConditionPct, 0, 100)),
                IsActive = true
            };

            if (string.IsNullOrWhiteSpace(truck.TruckName))
                truck.TruckName = "Current Truck";

            SetOnlyOneActive(truck.Id);

            _fleetService.AddOrUpdate(truck);
            RefreshIdentityDisplay();
            RefreshGrid();
            ClearInputs();
        }

        private void SaveEditTruckButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTruck == null)
            {
                MessageBox.Show("Select a truck first.", "Fleet Management",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!EnsureLinkedDiscord())
                return;

            var id = _identityService.LoadOrDefault();

            var truckName = (TruckNameTextBox.Text ?? "").Trim();
            var model = (ModelTextBox.Text ?? "").Trim();
            var plate = (PlateTextBox.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(truckName) || string.IsNullOrWhiteSpace(model))
            {
                MessageBox.Show("Truck Name and Model are required.", "Fleet Management",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _selectedTruck.TruckName = truckName;
            _selectedTruck.Model = model;
            _selectedTruck.PlateNumber = plate;
            _selectedTruck.DiscordUserId = id.DiscordUserId ?? "";
            _selectedTruck.DiscordUsername = id.DiscordUsername ?? "";
            _selectedTruck.DriverName = string.IsNullOrWhiteSpace(id.DiscordUsername) ? "Unlinked Driver" : id.DiscordUsername;
            _selectedTruck.LastSeenUtc = DateTime.UtcNow;

            if (string.IsNullOrWhiteSpace(_selectedTruck.LastSeenLocation))
                _selectedTruck.LastSeenLocation = "Edited Manually";

            _fleetService.AddOrUpdate(_selectedTruck);
            RefreshIdentityDisplay();
            RefreshGrid();
            ClearInputs();
        }

        private void SetActiveTruckButton_Click(object sender, RoutedEventArgs e)
        {
            if (FleetGrid.SelectedItem is not FleetTruck selected)
            {
                MessageBox.Show("Select a truck first.", "Fleet Management",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SetOnlyOneActive(selected.Id);
            selected.IsActive = true;
            selected.LastSeenUtc = DateTime.UtcNow;

            if (string.IsNullOrWhiteSpace(selected.LastSeenLocation))
                selected.LastSeenLocation = "Set Active";

            _fleetService.AddOrUpdate(selected);

            _selectedTruck = selected;
            RefreshGrid();
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (FleetGrid.SelectedItem is not FleetTruck selected)
            {
                MessageBox.Show("Select a truck first.", "Fleet Management",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Delete truck '{selected.TruckName}'?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            _fleetService.Remove(selected.Id);
            _selectedTruck = null;
            RefreshGrid();
            ClearInputs();
        }

        private void DeleteTruckButton_Click(object sender, RoutedEventArgs e)
        {
            DeleteButton_Click(sender, e);
        }

        private void AddManualTruckButton_Click(object sender, RoutedEventArgs e)
        {
            AddManualButton_Click(sender, e);
        }

        private void AddTelemetryTruckButton_Click(object sender, RoutedEventArgs e)
        {
            AddTelemetryButton_Click(sender, e);
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplySearchFilter();
        }

        private void RefreshFleetButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshIdentityDisplay();
            RefreshGrid();
        }

        private void SetActiveButton_Click(object sender, RoutedEventArgs e)
        {
            SetActiveTruckButton_Click(sender, e);
        }

        private void FleetGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FleetGrid.SelectedItem is not FleetTruck selected)
                return;

            _selectedTruck = selected;

            TruckNameTextBox.Text = selected.TruckName ?? "";
            ModelTextBox.Text = selected.Model ?? "";
            PlateTextBox.Text = selected.PlateNumber ?? "";

            if (!string.IsNullOrWhiteSpace(selected.DiscordUsername))
                DriverTextBox.Text = selected.DiscordUsername;
            else if (!string.IsNullOrWhiteSpace(selected.DriverName))
                DriverTextBox.Text = selected.DriverName;
            else
                DriverTextBox.Text = "No linked Discord user";
        }

        private void FleetGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FleetGrid.SelectedItem is not FleetTruck selected)
                return;

            _selectedTruck = selected;

            TruckNameTextBox.Text = selected.TruckName ?? "";
            ModelTextBox.Text = selected.Model ?? "";
            PlateTextBox.Text = selected.PlateNumber ?? "";

            if (!string.IsNullOrWhiteSpace(selected.DiscordUsername))
                DriverTextBox.Text = selected.DiscordUsername;
            else if (!string.IsNullOrWhiteSpace(selected.DriverName))
                DriverTextBox.Text = selected.DriverName;
            else
                DriverTextBox.Text = "No linked Discord user";
        }

        private void SetOnlyOneActive(string activeId)
        {
            try
            {
                var items = _fleetService.LoadAll() ?? new List<FleetTruck>();
                foreach (var truck in items)
                {
                    truck.IsActive = string.Equals(truck.Id, activeId, StringComparison.OrdinalIgnoreCase);
                    _fleetService.AddOrUpdate(truck);
                }
            }
            catch
            {
            }
        }

        private static string BuildLocationText(object snap)
        {
            try
            {
                var city = GetStringProp(snap, "City");
                var state = GetStringProp(snap, "State");

                if (!string.IsNullOrWhiteSpace(city) && !string.IsNullOrWhiteSpace(state))
                    return $"{city}, {state}";

                if (!string.IsNullOrWhiteSpace(city))
                    return city;

                if (!string.IsNullOrWhiteSpace(state))
                    return state;

                var deliveryCity = GetStringProp(snap, "CurrentCity");
                var deliveryState = GetStringProp(snap, "CurrentState");

                if (!string.IsNullOrWhiteSpace(deliveryCity) && !string.IsNullOrWhiteSpace(deliveryState))
                    return $"{deliveryCity}, {deliveryState}";

                if (!string.IsNullOrWhiteSpace(deliveryCity))
                    return deliveryCity;

                return "Unknown Location";
            }
            catch
            {
                return "Unknown Location";
            }
        }

        private static string GetStringProp(object obj, string propName)
        {
            try
            {
                var prop = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                var value = prop?.GetValue(obj);
                return value?.ToString()?.Trim() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private void ClearInputs()
        {
            _selectedTruck = null;

            TruckNameTextBox.Text = "";
            ModelTextBox.Text = "";
            PlateTextBox.Text = "";

            var id = _identityService.LoadOrDefault();
            DriverTextBox.Text = string.IsNullOrWhiteSpace(id.DiscordUsername)
                ? "No linked Discord user"
                : id.DiscordUsername;
        }
    }
}