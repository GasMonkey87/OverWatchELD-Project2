using OverWatchELD.Models;
using OverWatchELD.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace OverWatchELD.Views
{
    public partial class MaintenanceWindow : Window
    {
        private readonly FleetService _fleetService = new();
        private readonly FleetMaintenanceLedgerService _ledgerService = new();

        private FleetCostEntry? _selectedEntry;
        private List<FleetTruck> _trucks = new();

        private readonly string[] _billTypes =
        {
            "Fuel",
            "Toll",
            "Repair",
            "Upgrade",
            "Ticket",
            "Damage",
            "Oil Change",
            "Tires",
            "Inspection",
            "DOT",
            "Registration",
            "Insurance",
            "Custom"
        };

        public MaintenanceWindow()
        {
            InitializeComponent();
            Loaded += MaintenanceWindow_Loaded;
        }

        private void MaintenanceWindow_Loaded(object sender, RoutedEventArgs e)
        {
            EntryDatePicker.SelectedDate = DateTime.Now;
            LoadTruckLists();
            LoadBillTypes();
            RefreshGrid();
            ClearEntryForm();
        }

        private void LoadBillTypes()
        {
            BillTypeComboBox.ItemsSource = _billTypes.ToList();
            BillTypeComboBox.SelectedIndex = 0;

            var filterItems = new List<string> { "All" };
            filterItems.AddRange(_billTypes);
            BillTypeFilterComboBox.ItemsSource = filterItems;
            BillTypeFilterComboBox.SelectedIndex = 0;
        }

        private void LoadTruckLists()
        {
            _trucks = _fleetService.LoadAll() ?? new List<FleetTruck>();

            var truckItems = _trucks
                .Select(t => new ComboTruckItem
                {
                    TruckId = t.Id ?? "",
                    TruckName = t.TruckName ?? "",
                    PlateNumber = t.PlateNumber ?? ""
                })
                .OrderBy(x => x.Display)
                .ToList();

            TruckComboBox.ItemsSource = truckItems;
            if (truckItems.Count > 0)
                TruckComboBox.SelectedIndex = 0;

            var filterItems = new List<ComboTruckItem>
            {
                new ComboTruckItem { TruckId = "__ALL__", TruckName = "All Trucks", PlateNumber = "" }
            };
            filterItems.AddRange(truckItems);

            TruckFilterComboBox.ItemsSource = filterItems;
            TruckFilterComboBox.SelectedIndex = 0;
        }

        private void RefreshGrid()
        {
            var search = SearchTextBox.Text ?? "";
            var billType = BillTypeFilterComboBox.SelectedItem?.ToString() ?? "All";
            var truckId = (TruckFilterComboBox.SelectedItem as ComboTruckItem)?.TruckId ?? "__ALL__";
            var alertsOnly = AlertsOnlyCheckBox.IsChecked == true;

            var items = _ledgerService.Search(search, billType, truckId, alertsOnly);

            LedgerGrid.ItemsSource = null;
            LedgerGrid.ItemsSource = items;

            StatusText.Text =
                $"Entries: {items.Count} • Total Cost: {_ledgerService.TotalCost():C} • This Month: {_ledgerService.ThisMonthCost():C} • Alerts: {_ledgerService.OpenAlertsCount()} • Overdue: {_ledgerService.OverdueCount()}";
        }

        private FleetCostEntry BuildEntryFromForm(bool keepExistingId)
        {
            var truck = TruckComboBox.SelectedItem as ComboTruckItem;

            decimal.TryParse((AmountTextBox.Text ?? "").Trim(), out var amount);
            double.TryParse((OdometerTextBox.Text ?? "").Trim(), out var odometer);
            double.TryParse((DueMilesTextBox.Text ?? "").Trim(), out var dueMiles);

            return new FleetCostEntry
            {
                Id = keepExistingId && _selectedEntry != null ? _selectedEntry.Id : Guid.NewGuid().ToString(),
                TruckId = truck?.TruckId ?? "",
                TruckName = truck?.TruckName ?? "",
                PlateNumber = truck?.PlateNumber ?? "",
                DateUtc = EntryDatePicker.SelectedDate?.Date ?? DateTime.UtcNow,
                BillType = BillTypeComboBox.SelectedItem?.ToString() ?? "Fuel",
                Amount = amount,
                OdometerMiles = odometer,
                Vendor = (VendorTextBox.Text ?? "").Trim(),
                Location = (LocationTextBox.Text ?? "").Trim(),
                Notes = (NotesTextBox.Text ?? "").Trim(),
                RequiresFollowUp = RequiresFollowUpCheckBox.IsChecked == true,
                IsResolved = ResolvedCheckBox.IsChecked == true,
                DueAtMiles = double.TryParse((DueMilesTextBox.Text ?? "").Trim(), out _) ? dueMiles : null,
                DueDateUtc = DueDatePicker.SelectedDate?.Date
            };
        }

        private void AddEntryButton_Click(object sender, RoutedEventArgs e)
        {
            var entry = BuildEntryFromForm(false);

            if (string.IsNullOrWhiteSpace(entry.TruckId))
            {
                MessageBox.Show("Select a truck first.", "Maintenance",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (entry.Amount < 0)
            {
                MessageBox.Show("Amount must be zero or greater.", "Maintenance",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _ledgerService.AddOrUpdate(entry);
            RefreshGrid();
            ClearEntryForm();
        }

        private void SaveEditButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedEntry == null)
            {
                MessageBox.Show("Select an entry first.", "Maintenance",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var entry = BuildEntryFromForm(true);
            _ledgerService.AddOrUpdate(entry);
            RefreshGrid();
            ClearEntryForm();
        }

        private void DeleteEntryButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedEntry == null)
            {
                MessageBox.Show("Select an entry first.", "Maintenance",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Delete selected {(_selectedEntry.BillType ?? "entry")} record?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            _ledgerService.Remove(_selectedEntry.Id);
            RefreshGrid();
            ClearEntryForm();
        }

        private void LedgerGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LedgerGrid.SelectedItem is not FleetCostEntry selected)
                return;

            _selectedEntry = selected;

            SelectTruckInCombo(selected.TruckId);
            BillTypeComboBox.SelectedItem = selected.BillType;

            EntryDatePicker.SelectedDate = selected.DateUtc.ToLocalTime().Date;
            AmountTextBox.Text = selected.Amount.ToString("0.00");
            OdometerTextBox.Text = selected.OdometerMiles.ToString("0");
            VendorTextBox.Text = selected.Vendor ?? "";
            LocationTextBox.Text = selected.Location ?? "";
            NotesTextBox.Text = selected.Notes ?? "";
            DueMilesTextBox.Text = selected.DueAtMiles?.ToString("0") ?? "";
            DueDatePicker.SelectedDate = selected.DueDateUtc?.ToLocalTime().Date;
            RequiresFollowUpCheckBox.IsChecked = selected.RequiresFollowUp;
            ResolvedCheckBox.IsChecked = selected.IsResolved;
        }

        private void SelectTruckInCombo(string truckId)
        {
            foreach (var item in TruckComboBox.Items)
            {
                if (item is ComboTruckItem truck && string.Equals(truck.TruckId, truckId, StringComparison.OrdinalIgnoreCase))
                {
                    TruckComboBox.SelectedItem = item;
                    return;
                }
            }
        }

        private void ClearEntryForm()
        {
            _selectedEntry = null;

            if (TruckComboBox.Items.Count > 0 && TruckComboBox.SelectedIndex < 0)
                TruckComboBox.SelectedIndex = 0;

            BillTypeComboBox.SelectedIndex = 0;
            EntryDatePicker.SelectedDate = DateTime.Now.Date;
            AmountTextBox.Text = "";
            OdometerTextBox.Text = "";
            VendorTextBox.Text = "";
            LocationTextBox.Text = "";
            NotesTextBox.Text = "";
            DueMilesTextBox.Text = "";
            DueDatePicker.SelectedDate = null;
            RequiresFollowUpCheckBox.IsChecked = false;
            ResolvedCheckBox.IsChecked = false;
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshGrid();
        private void BillTypeFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshGrid();
        private void TruckFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshGrid();
        private void AlertsOnlyCheckBox_Changed(object sender, RoutedEventArgs e) => RefreshGrid();
        private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshGrid();

        private void ClearFiltersButton_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Text = "";
            BillTypeFilterComboBox.SelectedIndex = 0;
            TruckFilterComboBox.SelectedIndex = 0;
            AlertsOnlyCheckBox.IsChecked = false;
            RefreshGrid();
        }

        private sealed class ComboTruckItem
        {
            public string TruckId { get; set; } = "";
            public string TruckName { get; set; } = "";
            public string PlateNumber { get; set; } = "";

            public string Display =>
                string.IsNullOrWhiteSpace(PlateNumber) ? TruckName : $"{TruckName} • {PlateNumber}";

            public override string ToString() => Display;
        }
    }
}