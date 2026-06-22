using OverWatchELD.Models.Fleet;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using OverWatchELD.Services;

namespace OverWatchELD.Views
{
    public partial class FleetTruckEditWindow : Window
    {
        public FleetCommandTruck Truck { get; private set; }

        public FleetTruckEditWindow(FleetCommandTruck? truck = null)
        {
            InitializeComponent();

            Truck = truck ?? new FleetCommandTruck();
            DriverDropdownService.Bind(AssignedDriverBox, Truck.AssignedDriver, includeUnassigned: true);

            TruckNumberBox.Text = Truck.TruckNumber;
            DriverDiscordIdBox.Text = Truck.DriverDiscordId;
            LocationBox.Text = Truck.Location;
            HealthBox.Text = Truck.HealthPercent.ToString(CultureInfo.InvariantCulture);
            ServiceDueBox.Text = Truck.ServiceDueDate?.ToString("MM/dd/yyyy") ?? "";
            LastServiceBox.Text = Truck.LastServiceDate?.ToString("MM/dd/yyyy") ?? "";
            InspectionDueBox.Text = Truck.InspectionDueDate?.ToString("MM/dd/yyyy") ?? "";
            LastInspectionBox.Text = Truck.LastInspectionDate?.ToString("MM/dd/yyyy") ?? "";

            SelectStatus(Truck.Status);
        }

        private void SelectStatus(string? status)
        {
            var wanted = (status ?? "Active").Trim();

            foreach (var item in StatusBox.Items)
            {
                if (item is ComboBoxItem cbi &&
                    string.Equals((cbi.Content?.ToString() ?? "").Trim(), wanted, StringComparison.OrdinalIgnoreCase))
                {
                    StatusBox.SelectedItem = cbi;
                    return;
                }
            }

            StatusBox.SelectedIndex = 0;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            Truck.TruckNumber = (TruckNumberBox.Text ?? "").Trim();
            Truck.AssignedDriver = DriverDropdownService.SelectedName(AssignedDriverBox, "Unassigned");
            Truck.DriverDiscordId = DriverDropdownService.SelectedDiscordId(AssignedDriverBox, (DriverDiscordIdBox.Text ?? "").Trim());
            Truck.Location = (LocationBox.Text ?? "").Trim();
            Truck.Status = ((StatusBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Active").Trim();

            if (!int.TryParse((HealthBox.Text ?? "").Trim(), out var health))
                health = 100;
            Truck.HealthPercent = Math.Clamp(health, 0, 100);

            Truck.ServiceDueDate = ParseDate(ServiceDueBox.Text);
            Truck.LastServiceDate = ParseDate(LastServiceBox.Text);
            Truck.InspectionDueDate = ParseDate(InspectionDueBox.Text);
            Truck.LastInspectionDate = ParseDate(LastInspectionBox.Text);

            if (string.IsNullOrWhiteSpace(Truck.TruckNumber))
            {
                MessageBox.Show("Truck Number is required.", "Fleet Truck", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private static DateTime? ParseDate(string? text)
        {
            var raw = (text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            if (DateTime.TryParse(raw, out var dt))
                return dt.Date;

            return null;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}