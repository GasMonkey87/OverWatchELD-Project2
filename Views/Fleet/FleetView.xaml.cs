using OverWatchELD.Models;
using OverWatchELD.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace OverWatchELD.Views.Fleet
{
    public partial class FleetView : UserControl
    {
        private readonly FleetService _fleetService = new();
        private readonly FinanceService _financeService = new();
        private readonly DriverSubmissionService _submissionService = new();
        private readonly VtcAccessService _accessService = new();

        private VtcAccessProfile _profile = new();

        public FleetView()
        {
            InitializeComponent();
            LoadProfile();
            RefreshAll();
        }

        private void LoadProfile()
        {
            _profile = _accessService.LoadCurrentProfile();

            var displayName = string.IsNullOrWhiteSpace(_profile.DiscordUsername)
                ? "No linked Discord user"
                : _profile.DiscordUsername;

            DriverTextBox.Text = displayName;
            FinanceUserTextBox.Text = displayName;
            RoleStatusText.Text = $"Role: {_profile.Role}";
            AccessSummaryText.Text = _profile.Role;
            ApplyRoleVisibility();
        }

        private void ApplyRoleVisibility()
        {
            bool isManagement = _accessService.IsManagement(_profile.Role);

            AdminFleetBorder.Visibility = isManagement ? Visibility.Visible : Visibility.Collapsed;
            AdminFinanceBorder.Visibility = isManagement ? Visibility.Visible : Visibility.Collapsed;
            AdminFinanceGridBorder.Visibility = isManagement ? Visibility.Visible : Visibility.Collapsed;
            ApproveSubmissionButton.Visibility = isManagement ? Visibility.Visible : Visibility.Collapsed;
            DeleteSubmissionButton.Visibility = isManagement ? Visibility.Visible : Visibility.Collapsed;

            ManagementConsoleText.Text = isManagement
                ? "Management access confirmed. Fleet and finance controls are unlocked."
                : "Management tools are limited to Owner and Manager roles.";

            ManagementAccessText.Text = isManagement
                ? "You are signed in with management access."
                : "You are on the general fleet page. Management tools are hidden.";

            PageStatusText.Text = isManagement
                ? "Management tools unlocked for fleet and finance."
                : "General fleet page loaded for submissions, truck visibility, and map access.";

            FinanceStatusText.Text = isManagement
                ? "Track company-wide finances."
                : "Finance controls are restricted to management.";
        }

        private void RefreshAll()
        {
            RefreshFleet();
            RefreshFinance();
            RefreshSubmissions();
            PrefillDriverTruck();
        }

        private void RefreshFleet()
        {
            List<FleetTruck> items = _fleetService.LoadAll();

            FleetGrid.ItemsSource = null;
            FleetGrid.ItemsSource = items;
            FleetAssetCountText.Text = items.Count.ToString();

            var myTruck = items.FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(_profile.DiscordUserId) &&
                string.Equals(x.DiscordUserId, _profile.DiscordUserId, StringComparison.OrdinalIgnoreCase));

            if (myTruck != null)
            {
                var summary = string.IsNullOrWhiteSpace(myTruck.PlateNumber)
                    ? myTruck.TruckName
                    : $"{myTruck.TruckName} / {myTruck.PlateNumber}";

                SubmissionTruckTextBox.Text = summary;
                MyTruckSummaryText.Text = summary;
                MyTruckDetailsText.Text = $"Assigned truck: {summary}\nModel: {myTruck.Model}\nLast Seen UTC: {myTruck.LastSeenUtc:g}";
            }
            else
            {
                MyTruckSummaryText.Text = "Unassigned";
                MyTruckDetailsText.Text = "No truck assigned yet.";
            }
        }

        private void RefreshFinance()
        {
            var items = _financeService.LoadAll();

            FinanceGrid.ItemsSource = null;
            FinanceGrid.ItemsSource = items;

            BalanceText.Text = _financeService.GetBalance().ToString("C");
            IncomeText.Text = _financeService.GetTotalIncome().ToString("C");
            ExpenseText.Text = _financeService.GetTotalExpenses().ToString("C");
            FuelText.Text = _financeService.GetCategoryTotal("Fuel").ToString("C");
            MaintenanceText.Text = _financeService.GetCategoryTotal("Maintenance").ToString("C");
            RepairsText.Text = _financeService.GetCategoryTotal("Repairs").ToString("C");
        }

        private void RefreshSubmissions()
        {
            List<DriverSubmission> items = _submissionService.LoadAll();

            if (!_accessService.IsManagement(_profile.Role))
            {
                items = items.Where(x =>
                    string.Equals(x.DiscordUserId, _profile.DiscordUserId, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            SubmissionsGrid.ItemsSource = null;
            SubmissionsGrid.ItemsSource = items;

            SubmissionStatusText.Text = _accessService.IsManagement(_profile.Role)
                ? $"All driver submissions: {items.Count}"
                : $"My submissions: {items.Count}";

            OpenSubmissionsCountText.Text = items.Count(x => !x.IsApproved).ToString();
        }

        private void PrefillDriverTruck()
        {
            var myTruck = _fleetService.LoadAll().FirstOrDefault(x =>
                string.Equals(x.DiscordUserId, _profile.DiscordUserId, StringComparison.OrdinalIgnoreCase));

            if (myTruck != null)
            {
                SubmissionTruckTextBox.Text = string.IsNullOrWhiteSpace(myTruck.PlateNumber)
                    ? myTruck.TruckName
                    : $"{myTruck.TruckName} / {myTruck.PlateNumber}";
            }
        }

        private bool EnsureManagementAccess()
        {
            if (_accessService.IsManagement(_profile.Role))
                return true;

            MessageBox.Show("Only Owner and Manager roles can access management tools.",
                "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        private void AddManualButton_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureManagementAccess()) return;

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
                TruckName = truckName,
                Model = model,
                PlateNumber = plate,
                DiscordUserId = _profile.DiscordUserId,
                DiscordUsername = _profile.DiscordUsername,
                DriverName = _profile.DiscordUsername,
                LastSeenUtc = DateTime.UtcNow,
                IsActive = true
            };

            _fleetService.AddOrUpdate(truck);
            ClearFleetInputs();
            RefreshFleet();
        }

        private void AddTelemetryButton_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureManagementAccess()) return;

            var snap = TelemetryTruckCaptureService.Capture();
            if (snap == null ||
                (string.IsNullOrWhiteSpace(snap.TruckName) &&
                 string.IsNullOrWhiteSpace(snap.DriverName) &&
                 string.IsNullOrWhiteSpace(snap.Plate)))
            {
                MessageBox.Show("No active telemetry truck data was found.", "Telemetry",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var truck = new FleetTruck
            {
                TruckName = string.IsNullOrWhiteSpace(TruckNameTextBox.Text) ? (snap.TruckName ?? "").Trim() : TruckNameTextBox.Text.Trim(),
                Model = string.IsNullOrWhiteSpace(ModelTextBox.Text) ? "Unknown ATS Model" : ModelTextBox.Text.Trim(),
                PlateNumber = string.IsNullOrWhiteSpace(PlateTextBox.Text) ? (snap.Plate ?? "").Trim() : PlateTextBox.Text.Trim(),
                DiscordUserId = _profile.DiscordUserId,
                DiscordUsername = _profile.DiscordUsername,
                DriverName = string.IsNullOrWhiteSpace(_profile.DiscordUsername)
                    ? ((snap.DriverName ?? "").Trim())
                    : _profile.DiscordUsername,
                LastSeenUtc = DateTime.UtcNow,
                OdometerMiles = Math.Max(0, snap.Odometer),
                FuelPercent = ClampInt((int)Math.Round(snap.FuelPct), 0, 100),
                HealthPercent = ClampInt((int)Math.Round(snap.ConditionPct), 0, 100),
                IsActive = true
            };

            if (string.IsNullOrWhiteSpace(truck.TruckName))
                truck.TruckName = "Current Truck";

            if (string.IsNullOrWhiteSpace(truck.DriverName))
                truck.DriverName = "Unlinked Driver";

            _fleetService.AddOrUpdate(truck);
            ClearFleetInputs();
            RefreshFleet();
        }

        private void DeleteFleetButton_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureManagementAccess()) return;

            if (FleetGrid.SelectedItem is not FleetTruck selected)
            {
                MessageBox.Show("Select a fleet truck first.", "Fleet Management",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show($"Delete truck '{selected.TruckName}'?",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            _fleetService.Remove(selected.Id);
            RefreshFleet();
        }

        private void AddFinanceEntryButton_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureManagementAccess()) return;

            var entryType = ((ComboBoxItem)EntryTypeComboBox.SelectedItem)?.Content?.ToString() ?? "Expense";
            var category = ((ComboBoxItem)CategoryComboBox.SelectedItem)?.Content?.ToString() ?? "Misc";
            var truckName = (FinanceTruckTextBox.Text ?? "").Trim();
            var description = (DescriptionTextBox.Text ?? "").Trim();

            if (!decimal.TryParse(AmountTextBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount) &&
                !decimal.TryParse(AmountTextBox.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out amount))
            {
                MessageBox.Show("Enter a valid amount.", "Finance",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (amount < 0) amount = Math.Abs(amount);

            _financeService.Add(new FinanceEntry
            {
                DateUtc = DateTime.UtcNow,
                EntryType = entryType,
                Category = category,
                Amount = amount,
                TruckName = truckName,
                DiscordUserId = _profile.DiscordUserId,
                DiscordUsername = _profile.DiscordUsername,
                Description = description,
                EnteredBy = _profile.DiscordUsername,
                Source = "Manual"
            });

            ClearFinanceInputs();
            RefreshFinance();
        }

        private void DeleteFinanceEntryButton_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureManagementAccess()) return;

            if (FinanceGrid.SelectedItem is not FinanceEntry selected)
            {
                MessageBox.Show("Select a finance entry first.", "Finance",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show($"Delete {selected.EntryType} entry '{selected.Category}' for {selected.Amount:C}?",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            _financeService.Remove(selected.Id);
            RefreshFinance();
        }

        private void SubmitDriverItemButton_Click(object sender, RoutedEventArgs e)
        {
            var submissionType = ((ComboBoxItem)SubmissionTypeComboBox.SelectedItem)?.Content?.ToString() ?? "Report";
            var title = (SubmissionTitleTextBox.Text ?? "").Trim();
            var truck = (SubmissionTruckTextBox.Text ?? "").Trim();
            var details = (SubmissionDetailsTextBox.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(title))
            {
                MessageBox.Show("Title is required.", "Submission",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            decimal? amount = NoneAmount(SubmissionAmountTextBox.Text);

            _submissionService.Add(new DriverSubmission
            {
                DateUtc = DateTime.UtcNow,
                SubmissionType = submissionType,
                Title = title,
                Amount = amount,
                TruckName = truck,
                DiscordUserId = _profile.DiscordUserId,
                DiscordUsername = _profile.DiscordUsername,
                Details = details,
                IsApproved = false
            });

            ClearSubmissionInputs();
            RefreshSubmissions();

            MessageBox.Show($"{submissionType} submitted successfully.", "Submission",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private decimal? NoneAmount(string value)
        {
            if (!string.IsNullOrWhiteSpace(value) &&
                (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ||
                 decimal.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out parsed)))
            {
                return Math.Abs(parsed);
            }
            return null;
        }

        private void ApproveSubmissionButton_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureManagementAccess()) return;

            if (SubmissionsGrid.SelectedItem is not DriverSubmission selected)
            {
                MessageBox.Show("Select a submission first.", "Submissions",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            selected.IsApproved = true;
            _submissionService.Update(selected);
            RefreshSubmissions();
        }

        private void DeleteSubmissionButton_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureManagementAccess()) return;

            if (SubmissionsGrid.SelectedItem is not DriverSubmission selected)
            {
                MessageBox.Show("Select a submission first.", "Submissions",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show($"Delete submission '{selected.Title}'?",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            _submissionService.Remove(selected.Id);
            RefreshSubmissions();
        }

        private void ManagementLoginButton_Click(object sender, RoutedEventArgs e)
        {
            LoadProfile();
            RefreshAll();
        }

        private void OpenMapButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var owner = Window.GetWindow(this) as MainWindow;
                if (owner != null)
                {
                    foreach (Window w in Application.Current.Windows)
                    {
                        if (w is OverWatchELD.Views.LiveMapWindow existing)
                        {
                            if (!existing.IsVisible) existing.Show();
                            existing.Activate();
                            return;
                        }
                    }

                    var map = new OverWatchELD.Views.LiveMapWindow { Owner = owner };
                    map.Show();
                    map.Activate();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to open live map.\n{ex.Message}", "Map",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void RefreshAllButton_Click(object sender, RoutedEventArgs e)
        {
            LoadProfile();
            RefreshAll();
        }

        private void ClearFleetInputs()
        {
            TruckNameTextBox.Text = "";
            ModelTextBox.Text = "";
            PlateTextBox.Text = "";
        }

        private void ClearFinanceInputs()
        {
            AmountTextBox.Text = "";
            FinanceTruckTextBox.Text = "";
            DescriptionTextBox.Text = "";
        }

        private void ClearSubmissionInputs()
        {
            SubmissionTitleTextBox.Text = "";
            SubmissionAmountTextBox.Text = "";
            SubmissionDetailsTextBox.Text = "";
        }

        private static int ClampInt(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}