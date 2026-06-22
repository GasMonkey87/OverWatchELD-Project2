using OverWatchELD.Models;
using OverWatchELD.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Windows;

namespace OverWatchELD.Views.Management
{
    public partial class ManagementWindow : Window
    {
        private readonly FinanceService _financeService = new();
        private readonly FleetService _fleetService = new();
        private readonly DiscordWebhookSettingsService _settingsService = new();

        private static readonly HttpClient _http = new HttpClient();
        private static readonly JsonSerializerOptions _json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public ManagementWindow()
        {
            InitializeComponent();
            RefreshAll();
        }

        private void RefreshAll()
        {
            RefreshFinance();
            RefreshDriverManagement();
            RefreshWebhookSettings();
        }

        private void RefreshFinance()
        {
            var finance = _financeService.LoadAll();

            FinanceGrid.ItemsSource = null;
            FinanceGrid.ItemsSource = finance;

            BalanceText.Text = _financeService.GetBalance().ToString("C");
            IncomeText.Text = _financeService.GetTotalIncome().ToString("C");
            ExpenseText.Text = _financeService.GetTotalExpenses().ToString("C");
            EntryCountText.Text = finance.Count.ToString();
        }

        private void RefreshDriverManagement()
        {
            try
            {
                var rows = LoadDriverManagementRows();

                DriverGrid.ItemsSource = null;
                DriverGrid.ItemsSource = rows;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to load VTC roster.\n{ex.Message}",
                    "Management", MessageBoxButton.OK, MessageBoxImage.Warning);

                DriverGrid.ItemsSource = null;
            }
        }

        private List<DriverManagementRow> LoadDriverManagementRows()
        {
            var roster = LoadRosterFromBot();
            var fleet = _fleetService.LoadAll();

            var rows = new List<DriverManagementRow>();

            foreach (var driver in roster)
            {
                var assignedTruck = fleet
                    .Where(x => string.Equals(x.DiscordUserId, driver.DiscordUserId, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => x.IsActive)
                    .ThenByDescending(x => x.LastSeenUtc)
                    .FirstOrDefault();

                rows.Add(new DriverManagementRow
                {
                    DriverName = FirstNonEmpty(driver.DriverName, driver.DiscordUsername),
                    DiscordUsername = driver.DiscordUsername,
                    DiscordUserId = driver.DiscordUserId,
                    TruckName = assignedTruck?.TruckName ?? "",
                    Model = assignedTruck?.Model ?? "",
                    ModName = assignedTruck?.ModName ?? "",
                    PlateNumber = assignedTruck?.PlateNumber ?? "",
                    IsActive = assignedTruck?.IsActive ?? false
                });
            }

            return rows
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.DriverName)
                .ToList();
        }

        private List<RosterDriverDto> LoadRosterFromBot()
        {
            var cfg = VtcConfigService.Load();
            var baseUrl = (cfg.BotApiBaseUrl ?? "").Trim().TrimEnd('/');

            var pairing = VtcPairingStore.Load();
            var guildId = (pairing?.GuildId ?? cfg.Discord?.GuildId ?? "").Trim();

            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new InvalidOperationException("BotApiBaseUrl is empty in vtc.config.json.");

            if (string.IsNullOrWhiteSpace(guildId))
                throw new InvalidOperationException("GuildId is empty. Pair the ELD or set the guild in vtc.config.json.");

            var url = $"{baseUrl}/api/vtc/roster?guildId={Uri.EscapeDataString(guildId)}";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Accept.Clear();
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var resp = _http.Send(req);
            if (!resp.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Roster request failed: {(int)resp.StatusCode} {resp.ReasonPhrase}");
            }

            var json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var payload = JsonSerializer.Deserialize<RosterResponseDto>(json, _json);

            if (payload == null)
                throw new InvalidOperationException("Roster response could not be parsed.");

            return payload.Drivers ?? new List<RosterDriverDto>();
        }

        private void RefreshWebhookSettings()
        {
            var settings = _settingsService.Load();

            SubmissionWebhookTextBox.Text = settings.SubmissionWebhookUrl;
            FleetWebhookTextBox.Text = settings.FleetWebhookUrl;
            FinanceWebhookTextBox.Text = settings.FinanceWebhookUrl;
            DriverManagementWebhookTextBox.Text = settings.DriverManagementWebhookUrl;
            DiscordGuildIdTextBox.Text = settings.DiscordGuildId;
            DiscordBotNameTextBox.Text = settings.DiscordBotName;
            DispatchChannelIdTextBox.Text = settings.DispatchChannelId;
            NotesTextBox.Text = settings.Notes;
        }

        private void SaveWebhookSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            _settingsService.Save(new DiscordWebhookSettings
            {
                SubmissionWebhookUrl = SubmissionWebhookTextBox.Text ?? "",
                FleetWebhookUrl = FleetWebhookTextBox.Text ?? "",
                FinanceWebhookUrl = FinanceWebhookTextBox.Text ?? "",
                DriverManagementWebhookUrl = DriverManagementWebhookTextBox.Text ?? "",
                DiscordGuildId = DiscordGuildIdTextBox.Text ?? "",
                DiscordBotName = DiscordBotNameTextBox.Text ?? "",
                DispatchChannelId = DispatchChannelIdTextBox.Text ?? "",
                Notes = NotesTextBox.Text ?? ""
            });

            MessageBox.Show("Discord and webhook settings saved.", "Management",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshAll();
        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
            return "";
        }

        private sealed class RosterResponseDto
        {
            public bool Ok { get; set; }
            public string GuildId { get; set; } = "";
            public List<RosterDriverDto> Drivers { get; set; } = new();
        }

        private sealed class RosterDriverDto
        {
            public string DiscordUserId { get; set; } = "";
            public string DiscordUsername { get; set; } = "";
            public string DriverName { get; set; } = "";
        }
    }
}
