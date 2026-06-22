using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using OverWatchELD.Services;

namespace OverWatchELD.Views
{
    public partial class InspectionsView : UserControl
    {
        public InspectionsView()
        {
            InitializeComponent();
            StartDate.SelectedDate = DateTime.Today;
            EndDate.SelectedDate = DateTime.Today;
        }

        private void Preview_Click(object sender, RoutedEventArgs e)
        {
            var (s, eDate) = GetDates();
            var reports = InspectionArchiveService.LoadRange(s, eDate);

            ResultsList.ItemsSource = reports.Select(r =>
            {
                var local = r.SubmittedUtc.ToLocalTime();
                var defects = r.HasDefects ? $"DEFECTS({r.Defects.Count})" : "OK";
                return $"{local:yyyy-MM-dd HH:mm}  [{r.Type}]  {defects}  Driver:{r.DriverName}  Vehicle:{r.VehicleId}";
            }).ToList();

            StatusText.Text = $"Found {reports.Count} inspections from {s:yyyy-MM-dd} to {eDate:yyyy-MM-dd}.";
        }

        private void ExportFile_Click(object sender, RoutedEventArgs e)
        {
            var (s, eDate) = GetDates();
            var reports = InspectionArchiveService.LoadRange(s, eDate);

            if (reports.Count == 0)
            {
                StatusText.Text = "No inspections found for that date range.";
                return;
            }

            var file = InspectionArchiveService.ExportToFile(s, eDate);
            StatusText.Text = $"Exported {reports.Count} inspections to: {file}";
        }

        private async void SendDiscord_Click(object sender, RoutedEventArgs e)
        {
            var (s, eDate) = GetDates();
            var reports = InspectionArchiveService.LoadRange(s, eDate);

            if (reports.Count == 0)
            {
                StatusText.Text = "No inspections found for that date range (nothing sent).";
                return;
            }

            var settings = new AppSettingsService().Load();

            var webhook = (settings.Discord?.InspectionsWebhookUrl ?? "").Trim();
            if (string.IsNullOrWhiteSpace(webhook)) webhook = (settings.Discord?.ExportWebhookUrl ?? "").Trim();
            if (string.IsNullOrWhiteSpace(webhook)) webhook = Environment.GetEnvironmentVariable("OVERWATCHELD_DISCORD_WEBHOOK") ?? "";

            var (ok, err) = await DiscordWebhookService.SendInspectionsAsync(webhook, s, eDate, reports);
            StatusText.Text = ok ? $"Sent {reports.Count} inspections to Discord." : $"Discord send failed: {err}";
        }

        private (DateTime start, DateTime end) GetDates()
        {
            var s = StartDate.SelectedDate ?? DateTime.Today;
            var e = EndDate.SelectedDate ?? DateTime.Today;
            return (s.Date, e.Date);
        }
    }
}