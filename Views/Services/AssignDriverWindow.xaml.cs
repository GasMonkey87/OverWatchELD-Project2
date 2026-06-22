using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using OverWatchELD.Services;
using OverWatchELD.Services.Fleet;

namespace OverWatchELD.Views
{
    public partial class AssignDriverWindow : Window
    {
        private readonly DispatchJob _job;
        private readonly FleetDriverDirectoryService _driverDirectory = new FleetDriverDirectoryService();

        public AssignDriverWindow(DispatchJob job)
        {
            InitializeComponent();
            _job = job;

            LoadText.Text = $"Assign driver for {_job.LoadNumber}";
            Loaded += AssignDriverWindow_Loaded;
        }

        private async void AssignDriverWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadDriversAsync();
        }

        private async Task LoadDriversAsync()
        {
            try
            {
                var cfg = VtcConfigService.Load();
                var drivers = await _driverDirectory.LoadDriversAsync(cfg.BotApiBaseUrl ?? "");

                var discordNames = drivers
                    .Select(d => FirstNonEmpty(
                        d.DisplayName,
                        d.DriverName,
                        d.Username,
                        d.DiscordUserId,
                        d.DriverId))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim());

                var localNames = DispatchService.Drivers
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim());

                var profileNames = DriverDropdownService.LoadDriverNames(includeUnassigned: false);

                var names = discordNames
                    .Concat(localNames)
                    .Concat(profileNames)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (!names.Any(x => string.Equals(x, "Unassigned", StringComparison.OrdinalIgnoreCase)))
                    names.Insert(0, "Unassigned");

                DriverCombo.ItemsSource = names;
                DriverCombo.SelectedItem = names.FirstOrDefault(x =>
                    string.Equals(
                        x,
                        string.IsNullOrWhiteSpace(_job.AssignedDriver) ? "Unassigned" : _job.AssignedDriver,
                        StringComparison.OrdinalIgnoreCase))
                    ?? "Unassigned";
            }
            catch
            {
                var fallback = DispatchService.Drivers
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Concat(DriverDropdownService.LoadDriverNames(includeUnassigned: false))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (!fallback.Any(x => string.Equals(x, "Unassigned", StringComparison.OrdinalIgnoreCase)))
                    fallback.Insert(0, "Unassigned");

                DriverCombo.ItemsSource = fallback;
                DriverCombo.SelectedItem = fallback.FirstOrDefault(x =>
                    string.Equals(
                        x,
                        string.IsNullOrWhiteSpace(_job.AssignedDriver) ? "Unassigned" : _job.AssignedDriver,
                        StringComparison.OrdinalIgnoreCase))
                    ?? "Unassigned";
            }
        }

        private void Assign_Click(object sender, RoutedEventArgs e)
        {
            var selected = DriverCombo.SelectedItem?.ToString() ?? "Unassigned";

            _job.AssignedDriver = selected;
            _job.Status = selected == "Unassigned" ? "Available" : "Assigned";
            _job.UpdatedUtc = DateTime.UtcNow;

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
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
