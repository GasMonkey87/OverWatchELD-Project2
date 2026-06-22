using OverWatchELD.Models;
using OverWatchELD.Services;
using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace OverWatchELD.Views.Management
{
    public partial class DriverSubmissionsWindow : Window
    {
        private readonly DriverSubmissionService _submissionService = new();
        private readonly DiscordIdentityService _identityService = new();
        private readonly WebhookNotificationService _webhookService = new();
        private readonly FleetService _fleetService = new();

        public DriverSubmissionsWindow()
        {
            InitializeComponent();
            PrefillTruck();
            RefreshGrid();
        }

        private void PrefillTruck()
        {
            var id = _identityService.LoadOrDefault();
            var truck = _fleetService.LoadAll()
                .FirstOrDefault(x => string.Equals(x.DiscordUserId, id.DiscordUserId, StringComparison.OrdinalIgnoreCase)
                                     && x.IsActive);

            if (truck != null)
            {
                TruckTextBox.Text = string.IsNullOrWhiteSpace(truck.PlateNumber)
                    ? truck.TruckName
                    : $"{truck.TruckName} / {truck.PlateNumber}";
            }
        }

        private void RefreshGrid()
        {
            var id = _identityService.LoadOrDefault();
            var items = _submissionService.LoadAll()
                .Where(x => string.Equals(x.DiscordUserId, id.DiscordUserId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.DateUtc)
                .ToList();

            SubmissionsGrid.ItemsSource = null;
            SubmissionsGrid.ItemsSource = items;
        }

        private async void SubmitButton_Click(object sender, RoutedEventArgs e)
        {
            var id = _identityService.LoadOrDefault();
            var type = ((ComboBoxItem)SubmissionTypeComboBox.SelectedItem)?.Content?.ToString() ?? "Report";
            var title = (TitleTextBox.Text ?? "").Trim();
            var truck = (TruckTextBox.Text ?? "").Trim();
            var details = (DetailsTextBox.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(title))
            {
                MessageBox.Show("Title is required.", "Driver Submission",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            decimal? amount = null;
            if (!string.IsNullOrWhiteSpace(AmountTextBox.Text))
            {
                if (decimal.TryParse(AmountTextBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                    || decimal.TryParse(AmountTextBox.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out parsed))
                {
                    amount = Math.Abs(parsed);
                }
            }

            var item = new DriverSubmission
            {
                DateUtc = DateTime.UtcNow,
                SubmissionType = type,
                Title = title,
                Amount = amount,
                TruckName = truck,
                DiscordUserId = id.DiscordUserId,
                DiscordUsername = id.DiscordUsername,
                Details = details,
                IsApproved = false
            };

            _submissionService.Add(item);

            if (SendWebhookCheckBox.IsChecked == true)
            {
                await _webhookService.PostDriverSubmissionAsync(item);
            }

            TitleTextBox.Text = "";
            AmountTextBox.Text = "";
            DetailsTextBox.Text = "";

            RefreshGrid();

            MessageBox.Show("Submission saved successfully.", "Driver Submission",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
