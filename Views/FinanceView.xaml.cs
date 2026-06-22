using OverWatchELD.Models;
using OverWatchELD.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace OverWatchELD.Views
{
    public partial class FinanceView : UserControl
    {
        private readonly FinanceService _financeService = new();
        private readonly DiscordIdentityService _identityService = new();

        public FinanceView()
        {
            InitializeComponent();

            var id = _identityService.LoadOrDefault();
            UserTextBox.Text = string.IsNullOrWhiteSpace(id.DiscordUsername)
                ? "No linked Discord user"
                : id.DiscordUsername;

            RefreshAll();
        }

        private void RefreshAll()
        {
            List<FinanceEntry> items = _financeService.LoadAll();

            FinanceGrid.ItemsSource = null;
            FinanceGrid.ItemsSource = items;

            var balance = _financeService.GetBalance();
            var income = _financeService.GetTotalIncome();
            var expenses = _financeService.GetTotalExpenses();
            var fuel = _financeService.GetCategoryTotal("Fuel");
            var maintenance = _financeService.GetCategoryTotal("Maintenance");
            var repairs = _financeService.GetCategoryTotal("Repairs");

            BalanceText.Text = balance.ToString("C");
            IncomeText.Text = income.ToString("C");
            ExpenseText.Text = expenses.ToString("C");
            FuelText.Text = fuel.ToString("C");
            MaintenanceText.Text = maintenance.ToString("C");
            RepairsText.Text = repairs.ToString("C");

            FinanceStatusText.Text = $"Ledger entries: {items.Count}";
        }

        private void AddFinanceEntryButton_Click(object sender, RoutedEventArgs e)
        {
            var identity = _identityService.LoadOrDefault();

            var entryType = ((ComboBoxItem)EntryTypeComboBox.SelectedItem)?.Content?.ToString() ?? "Expense";
            var category = ((ComboBoxItem)CategoryComboBox.SelectedItem)?.Content?.ToString() ?? "Misc";
            var truckName = (TruckTextBox.Text ?? "").Trim();
            var description = (DescriptionTextBox.Text ?? "").Trim();

            if (!decimal.TryParse(AmountTextBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
            {
                if (!decimal.TryParse(AmountTextBox.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out amount))
                {
                    MessageBox.Show("Enter a valid amount.", "Finance",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            if (amount < 0)
                amount = Math.Abs(amount);

            var entry = new FinanceEntry
            {
                DateUtc = DateTime.UtcNow,
                EntryType = entryType,
                Category = category,
                Amount = amount,
                TruckName = truckName,
                DiscordUserId = identity.DiscordUserId,
                DiscordUsername = identity.DiscordUsername,
                Description = description,
                EnteredBy = identity.DiscordUsername,
                Source = "Manual"
            };

            _financeService.Add(entry);

            AmountTextBox.Text = "";
            TruckTextBox.Text = "";
            DescriptionTextBox.Text = "";

            RefreshAll();
        }

        private void DeleteFinanceEntryButton_Click(object sender, RoutedEventArgs e)
        {
            if (FinanceGrid.SelectedItem is not FinanceEntry selected)
            {
                MessageBox.Show("Select a finance entry first.", "Finance",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Delete {selected.EntryType} entry '{selected.Category}' for {selected.Amount:C}?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            _financeService.Remove(selected.Id);
            RefreshAll();
        }
    }
}