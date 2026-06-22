using OverWatchELD.Services;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace OverWatchELD.Views
{
    public partial class MaintenanceHubView : UserControl
    {
        private readonly FleetMaintenanceLedgerService _ledgerService = new();

        public MaintenanceHubView()
        {
            InitializeComponent();
            Loaded += MaintenanceHubView_Loaded;
        }

        private void MaintenanceHubView_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshHub();
        }

        private void RefreshHub()
        {
            var all = _ledgerService.LoadAll();
            var alerts = all
                .Where(x => !x.IsResolved && (x.RequiresFollowUp || x.IsOverdueByDate || x.IsOverdueByMiles))
                .OrderByDescending(x => x.IsOverdueByDate || x.IsOverdueByMiles)
                .ThenByDescending(x => x.DateUtc)
                .ToList();

            AlertsGrid.ItemsSource = null;
            AlertsGrid.ItemsSource = alerts;

            TotalCostText.Text = _ledgerService.TotalCost().ToString("C");
            MonthCostText.Text = _ledgerService.ThisMonthCost().ToString("C");
            OpenAlertsText.Text = _ledgerService.OpenAlertsCount().ToString();
            OverdueText.Text = _ledgerService.OverdueCount().ToString();
            IssuesText.Text = $"{_ledgerService.UnresolvedTicketsCount()} / {_ledgerService.UnresolvedDamageCount()}";

            HubStatusText.Text = $"Fleet maintenance alerts and billing overview. Alerts: {alerts.Count}";
        }

        private void RefreshHubButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshHub();
        }

        private void OpenLedgerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var owner = Window.GetWindow(this);
                var win = new MaintenanceWindow();

                if (owner != null)
                    win.Owner = owner;

                win.ShowDialog();
                RefreshHub();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to open Maintenance Ledger.\n{ex.Message}",
                    "Maintenance", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OpenLedgerAlertsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var owner = Window.GetWindow(this);
                var win = new MaintenanceWindow();

                if (owner != null)
                    win.Owner = owner;

                win.ShowDialog();
                RefreshHub();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to open Maintenance Ledger.\n{ex.Message}",
                    "Maintenance", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}