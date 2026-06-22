using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OverWatchELD.Services;
using OverWatchELD.ViewModels;
using OverWatchELD.Services.Economy;
using OverWatchELD.Services.Licensing;
using OverWatchELD.Services.Discord;
using OverWatchELD.Stores;

namespace OverWatchELD.Views
{
    public partial class VtcAdminWindow : Window
    {
        private readonly VtcRosterViewModel _rosterVm;
        private readonly bool _canManage;
        private readonly string _accessRole;

        public VtcAdminWindow(VtcRosterViewModel rosterVm, string accessRole)
        {
            InitializeComponent();

            _rosterVm = rosterVm;
            _accessRole = (accessRole ?? "").Trim();

            AdminRosterGrid.ItemsSource = _rosterVm.Drivers;

            _canManage = AdminAccessService.CanManageDiscordSettings(
                linkedDiscordUser: "",
                linkedRole: _accessRole);

            ApplyAccessLock();
            LoadManagers();
            EnsureNotificationSettingsInitialized();

            RoleBadgeText.Text = _canManage ? $"{_accessRole} Access" : "Restricted";
            AppendLog($"Admin window opened with access role: {accessRole}");

            LoadAdminSettings();
            AdminRosterGrid.SelectionChanged += AdminRosterGrid_SelectionChanged;
        }

        private void LoadAdminSettings()
        {
            try
            {
                var settings = AdminSettingsStore.Load();

                if (AutoLockDefectTruckCheckBox != null)
                    AutoLockDefectTruckCheckBox.IsChecked = settings.AutoLockTruckOnInspectionDefect;

                AppendLog($"Auto-lock inspection defect trucks: {(settings.AutoLockTruckOnInspectionDefect ? "Enabled" : "Disabled")}");
            }
            catch
            {
            }
        }

        private void AutoLockDefectTruck_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_canManage)
                {
                    AppendLog("Blocked: insufficient permissions.");
                    return;
                }

                var settings = AdminSettingsStore.Load();
                settings.AutoLockTruckOnInspectionDefect = AutoLockDefectTruckCheckBox?.IsChecked == true;
                AdminSettingsStore.Save(settings);

                AppendLog($"Auto-lock inspection defect trucks: {(settings.AutoLockTruckOnInspectionDefect ? "Enabled" : "Disabled")}");
            }
            catch (Exception ex)
            {
                AppendLog("Admin setting save failed: " + ex.Message);
            }
        }
        private void FleetCommand_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new FleetCommandCenterWindow(_accessRole)
                {
                    Owner = this
                };

                win.Show();
                win.Activate();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to open Fleet Command Center.\n\n" + ex.Message,
                    "Fleet Command Center",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        private void OpenAdminAllBols_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var canOpenAllBols = _canManage || BolAccessService.CanViewAllBols();
                if (!canOpenAllBols)
                {
                    MessageBox.Show("Only Owners, Admins, and Dispatchers can view all driver BOLs.", "View All BOLs", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var win = new BolWindow(forceViewAllBols: true)
                {
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                win.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to open all BOLs.\n\n" + ex.Message,
                    "View All BOLs",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }


        private bool IsOwnerAccess()
        {
            var role = (_accessRole ?? "").Trim();
            if (string.Equals(role, "Owner", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(role, "VTC Owner", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(role, "owner", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private void WipeAllVtcData_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!IsOwnerAccess())
                {
                    MessageBox.Show(
                        "Only the VTC Owner can wipe all VTC data.",
                        "Wipe All Data",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    AppendLog("Blocked wipe-all-data request: owner access required.");
                    return;
                }

                var confirm = MessageBox.Show(
                    "This will wipe ALL inputted VTC data and cannot be undone.\n\n" +
                    "This includes logs, trucks, fleet command data, linked drivers, Discord attachments/config, loads, BOL ownership, maintenance history, roster/cache data, and saved VTC setup.\n\n" +
                    "After this completes, the owner must go through initial setup again and reconnect the VTC to Discord.\n\n" +
                    "Continue?",
                    "Wipe All VTC Data - Cannot Be Undone",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Error,
                    MessageBoxResult.No);

                if (confirm != MessageBoxResult.Yes)
                    return;

                var second = MessageBox.Show(
                    "Final confirmation: wipe the VTC clean slate now?",
                    "Final Confirmation",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

                if (second != MessageBoxResult.Yes)
                    return;

                var result = VtcCleanSlateResetService.WipeAllVtcData();
                AppendLog(result.Summary);

                MessageBox.Show(
                    result.Summary + "\n\nClose and reopen OverWatch ELD to start the initial setup again.",
                    "VTC Clean Slate Complete",
                    MessageBoxButton.OK,
                    result.Warnings.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                AppendLog("Wipe all data failed: " + ex.Message);
                MessageBox.Show("Wipe all data failed.\n\n" + ex.Message, "Wipe All Data", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RealDriverPayroll_Click(object sender, RoutedEventArgs e)
        {
            FleetEconomyIntegration.OpenRealDriverEconomyWindow(this);
        }

        private void ApplyAccessLock()
        {
            try
            {
                FooterText.Text = _canManage
                    ? "Owner / Admin / Manager tools"
                    : "Restricted tools";

                if (WipeAllVtcDataButton != null)
                    WipeAllVtcDataButton.Visibility = IsOwnerAccess() ? Visibility.Visible : Visibility.Collapsed;

                if (_canManage)
                    return;

                AssignTruckButton.IsEnabled = false;
                SaveRoleButton.IsEnabled = false;

                TruckNumberTextBox.IsEnabled = false;
                RoleComboBox.IsEnabled = false;

                AddManagerButton.IsEnabled = false;
                RemoveManagerButton.IsEnabled = false;
                ManagerNameTextBox.IsEnabled = false;
                if (NotificationSettingsButton != null)
                    NotificationSettingsButton.IsEnabled = false;

                AppendLog("Access restricted: insufficient permissions.");
            }
            catch
            {
            }
        }

        private void FleetEconomy_Click(object sender, RoutedEventArgs e)
        {
            RealDriverEconomyPayrollService.SyncDeliveredLoadsAndPayroll();

            FleetEconomyIntegration.OpenEconomyWindow(this);
        }

        private void TruckApprovals_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new OverWatchELD.Views.Fleet.FleetTruckApprovalWindow
                {
                    Owner = this
                };

                win.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Truck Approvals",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void DriverEndorsements_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_canManage)
                {
                    AppendLog("Blocked: insufficient permissions.");
                    return;
                }

                if (AdminRosterGrid.SelectedItem is not VtcRosterViewModel.RosterDriverRow row)
                {
                    MessageBox.Show(
                        "Select a driver first.",
                        "DOT Endorsements",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    return;
                }

                var current =
                    DriverLicenseEndorsementService
                        .GetManualCodes(row.DiscordUserId)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var defs =
                    DriverLicenseEndorsementService.StandardDefinitions
                        .OrderBy(x => x.Code)
                        .ToList();

                var lines = defs
                    .Select(x =>
                    {
                        var enabled = current.Contains(x.Code) ? "[X]" : "[ ]";
                        return $"{enabled} {x.Code} - {x.Name}";
                    });

                var result = Microsoft.VisualBasic.Interaction.InputBox(
                    "Enter endorsement codes separated by commas.\n\n" +
                    "Available:\n\n" +
                    string.Join(Environment.NewLine, lines) +
                    "\n\nExample:\nH,N,T",
                    $"DOT Endorsements - {row.Driver}",
                    string.Join(",", current));

                if (string.IsNullOrWhiteSpace(result))
                    return;

                var codes = result
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim().ToUpperInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                DriverLicenseEndorsementService.SaveManualCodes(
                    row.DiscordUserId,
                    codes);

                DiscordNotificationPushService.PushFireAndForget(
                    "Endorsements",
                    "DOT Endorsements Updated",
                    $"{row.Driver} endorsements updated: {string.Join(", ", codes)}",
                    "Updated by VTC Admin");

                AppendLog(
                    $"DOT endorsements updated for {row.Driver}: {string.Join(", ", codes)}");

                MessageBox.Show(
                    $"DOT endorsements updated for:\n\n{row.Driver}",
                    "DOT Endorsements",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "DOT Endorsements",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }


        private void EnsureNotificationSettingsInitialized()
        {
            try
            {
                var cfg = VtcConfigService.Load(forceReload: true);
                var rows = DiscordNotificationSettingsService.LoadAll();
                DiscordNotificationSettingsService.ApplyExistingConfigFallback(rows, cfg);
                DiscordNotificationSettingsService.SaveAll(rows);
            }
            catch
            {
            }
        }

        private void NotificationSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_canManage)
                {
                    AppendLog("Blocked: insufficient permissions.");
                    return;
                }

                var rows = DiscordNotificationSettingsService.LoadAll();

                var win = new Window
                {
                    Title = "Discord Notification Settings",
                    Width = 980,
                    Height = 640,
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Background = Brushes.Black
                };

                var root = new Grid { Margin = new Thickness(14) };
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var title = new TextBlock
                {
                    Text = "Enable/disable ELD-wide Discord pushes and assign each category to its own channel/webhook.",
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 12)
                };
                Grid.SetRow(title, 0);
                root.Children.Add(title);

                var grid = new DataGrid
                {
                    AutoGenerateColumns = false,
                    CanUserAddRows = false,
                    CanUserDeleteRows = false,
                    IsReadOnly = false,
                    ItemsSource = rows,
                    Background = Brushes.Black,
                    Foreground = Brushes.White,
                    RowBackground = new SolidColorBrush(Color.FromRgb(20, 20, 20)),
                    AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(28, 28, 28)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(42, 42, 42)),
                    GridLinesVisibility = DataGridGridLinesVisibility.Horizontal
                };

                grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Enabled", Binding = new System.Windows.Data.Binding("Enabled"), Width = 80 });
                grid.Columns.Add(new DataGridTextColumn { Header = "Category", Binding = new System.Windows.Data.Binding("Category"), IsReadOnly = true, Width = 120 });
                grid.Columns.Add(new DataGridTextColumn { Header = "Display", Binding = new System.Windows.Data.Binding("DisplayName"), Width = 160 });
                grid.Columns.Add(new DataGridTextColumn { Header = "Default Channel", Binding = new System.Windows.Data.Binding("DefaultChannelName"), Width = 150 });
                grid.Columns.Add(new DataGridTextColumn { Header = "Channel ID", Binding = new System.Windows.Data.Binding("ChannelId"), Width = 170 });
                grid.Columns.Add(new DataGridTextColumn { Header = "Webhook URL", Binding = new System.Windows.Data.Binding("WebhookUrl"), Width = 260 });
                grid.Columns.Add(new DataGridTextColumn { Header = "Fallback", Binding = new System.Windows.Data.Binding("FallbackCategory"), Width = 110 });

                Grid.SetRow(grid, 1);
                root.Children.Add(grid);

                var buttons = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 12, 0, 0)
                };

                var save = new Button { Content = "Save", Width = 110, Height = 34, Margin = new Thickness(0, 0, 8, 0) };
                save.Click += (_, __) =>
                {
                    DiscordNotificationSettingsService.SaveAll(rows);
                    AppendLog("Discord notification settings saved.");
                    MessageBox.Show("Notification settings saved.", "Discord Notifications", MessageBoxButton.OK, MessageBoxImage.Information);
                };

                var test = new Button { Content = "Test Selected", Width = 120, Height = 34, Margin = new Thickness(0, 0, 8, 0) };
                test.Click += async (_, __) =>
                {
                    DiscordNotificationSettingsService.SaveAll(rows);
                    var selected = grid.SelectedItem as DiscordNotificationCategorySetting ?? rows.FirstOrDefault();
                    if (selected == null) return;

                    var ok = await DiscordNotificationPushService.PushAsync(
                        selected.Category,
                        "Test Notification",
                        $"This is a test notification for {selected.DisplayName}.",
                        "Sent from the VTC Admin notification settings panel.");

                    AppendLog(ok ? $"Test notification sent: {selected.Category}" : $"Test notification failed/skipped: {selected.Category}");
                    MessageBox.Show(ok ? "Test notification sent." : "Test notification failed or category is disabled/missing webhook.", "Discord Notifications", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
                };

                var defaults = new Button { Content = "Reset Defaults", Width = 130, Height = 34, Margin = new Thickness(0, 0, 8, 0) };
                defaults.Click += (_, __) =>
                {
                    DiscordNotificationSettingsService.ResetToDefaults();
                    rows = DiscordNotificationSettingsService.LoadAll();
                    grid.ItemsSource = rows;
                    AppendLog("Discord notification settings reset to defaults.");
                };

                var close = new Button { Content = "Close", Width = 100, Height = 34 };
                close.Click += (_, __) => win.Close();

                buttons.Children.Add(save);
                buttons.Children.Add(test);
                buttons.Children.Add(defaults);
                buttons.Children.Add(close);

                Grid.SetRow(buttons, 2);
                root.Children.Add(buttons);

                win.Content = root;
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Discord Notifications", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ProfitLeaderboards_Click(object sender, RoutedEventArgs e)
        {
            FleetEconomyIntegration.OpenTruckProfitabilityLeaderboardsWindow(this);
        }

        private void InspectionCompliance_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new InspectionComplianceWindow
                {
                    Owner = this
                };

                win.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Inspection Compliance",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void OperationsCommandCenter_Click(object sender, RoutedEventArgs e)
        {
            FleetEconomyIntegration.OpenOperationsCommandCenterWindow(this);
        }

        private void FuelMaintenance_Click(object sender, RoutedEventArgs e)
        {
            FleetEconomyIntegration.OpenFuelMaintenanceAutomationWindow(this);
        }

        private void DriverScores_Click(object sender, RoutedEventArgs e)
        {
            FleetEconomyIntegration.OpenDriverSafetyPerformanceWindow(this);
        }

        private void DispatchContracts_Click(object sender, RoutedEventArgs e)
        {
            FleetEconomyIntegration.OpenDispatchContractsWindow(this);
        }

        private void FleetAnalytics_Click(object sender, RoutedEventArgs e)
        {
            FleetEconomyIntegration.OpenFleetAnalyticsDashboardWindow(this);
        }

        private async void AutoSetupDiscord_Click(object sender, RoutedEventArgs e)
        {
            await RunDiscordSetupAsync(repair: false);
        }

        private async void RepairDiscordSetup_Click(object sender, RoutedEventArgs e)
        {
            await RunDiscordSetupAsync(repair: true);
        }

        private async Task RunDiscordSetupAsync(bool repair)
        {
            try
            {
                AutoSetupStatusText.Text = repair
                    ? "Repairing / syncing Discord setup..."
                    : "Creating Discord channels/webhooks...";

                AutoSetupStatusText.Foreground = Brushes.Orange;

                AppendLog(repair
                    ? "Repair Discord setup started."
                    : "Auto Discord setup started.");

                var cfg = VtcConfigService.LoadOrCreate();

                cfg.Discord ??= new VtcConfig.DiscordConfig();

                var guildId = !string.IsNullOrWhiteSpace(cfg.Discord.GuildId)
                    ? cfg.Discord.GuildId.Trim()
                    : (cfg.GuildId ?? "").Trim();

                if (string.IsNullOrWhiteSpace(guildId))
                {
                    DiscordSetupFail("No Discord guild/server is linked yet. Use !link first.");
                    return;
                }

                var botBase = (cfg.BotApiBaseUrl ?? "").Trim().TrimEnd('/');

                if (string.IsNullOrWhiteSpace(botBase))
                {
                    DiscordSetupFail("Bot API URL is missing in vtc_config.json.");
                    return;
                }

                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

                var url =
                    $"{botBase}/api/vtc/setup/auto-discord?guildId={Uri.EscapeDataString(guildId)}&repair={(repair ? "true" : "false")}";

                using var resp = await http.PostAsync(url, null);
                var text = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                    DiscordSetupFail($"Discord setup failed: {(int)resp.StatusCode}\n\n{text}");
                    return;
                }

                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;

                if (root.TryGetProperty("ok", out var okProp) &&
                    okProp.ValueKind == JsonValueKind.False)
                {
                    var error = root.TryGetProperty("error", out var errProp)
                        ? errProp.GetString()
                        : "Unknown error";

                    DiscordSetupFail("Discord setup failed: " + error);
                    return;
                }

                cfg.Enabled = true;
                cfg.GuildId = guildId;
                cfg.Discord.GuildId = guildId;

                if (root.TryGetProperty("guildName", out var guildNameProp))
                {
                    var guildName = guildNameProp.GetString();
                    if (!string.IsNullOrWhiteSpace(guildName))
                        cfg.VtcName = guildName;
                }

                if (root.TryGetProperty("channels", out var channels))
                {
                    cfg.Discord.DispatchChannelId = GetJsonString(channels, "dispatchChannelId");
                    cfg.Discord.BolChannelId = GetJsonString(channels, "bolChannelId");
                    cfg.Discord.LogsChannelId = GetJsonString(channels, "logsChannelId");
                    cfg.Discord.InspectionsChannelId = GetJsonString(channels, "inspectionsChannelId");
                    cfg.Discord.MaintenanceChannelId = GetJsonString(channels, "maintenanceChannelId");
                    cfg.Discord.LeaderboardChannelId = GetJsonString(channels, "leaderboardChannelId");
                    cfg.Discord.AnnouncementsChannelId = GetJsonString(channels, "announcementsChannelId");
                    cfg.Discord.SystemLogChannelId = GetJsonString(channels, "systemLogChannelId");

                    var loadboard = GetJsonString(channels, "loadboardChannelId");
                    if (!string.IsNullOrWhiteSpace(loadboard))
                        cfg.Discord.LoadboardChannelId = loadboard;
                }

                if (root.TryGetProperty("webhooks", out var webhooks))
                {
                    cfg.Discord.DispatchWebhookUrl = GetJsonString(webhooks, "dispatchWebhookUrl");
                    cfg.Discord.BolWebhookUrl = GetJsonString(webhooks, "bolWebhookUrl");
                    cfg.Discord.LogsWebhookUrl = GetJsonString(webhooks, "logsWebhookUrl");
                    cfg.Discord.InspectionsWebhookUrl = GetJsonString(webhooks, "inspectionsWebhookUrl");
                    cfg.Discord.MaintenanceWebhookUrl = GetJsonString(webhooks, "maintenanceWebhookUrl");
                    cfg.Discord.LeaderboardWebhookUrl = GetJsonString(webhooks, "leaderboardWebhookUrl");
                    cfg.Discord.AnnouncementsWebhookUrl = GetJsonString(webhooks, "announcementsWebhookUrl");
                    cfg.Discord.SystemWebhookUrl = GetJsonString(webhooks, "systemWebhookUrl");
                }

                VtcConfigService.Save(cfg);
                DiscordNotificationSettingsService.ApplyDiscordSetupPayload(root, cfg);

                AutoSetupStatusText.Text = repair
                    ? "Discord setup repaired and synced."
                    : "Discord setup complete and synced.";

                AutoSetupStatusText.Foreground = Brushes.LimeGreen;

                AppendLog(AutoSetupStatusText.Text);

                MessageBox.Show(
                    AutoSetupStatusText.Text,
                    "VTC Admin",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                DiscordSetupFail(ex.Message);
            }
        }

        private static string GetJsonString(JsonElement obj, string name)
        {
            return obj.TryGetProperty(name, out var value)
                ? value.GetString() ?? ""
                : "";
        }

        private void DiscordSetupFail(string message)
        {
            AutoSetupStatusText.Text = message;
            AutoSetupStatusText.Foreground = Brushes.Red;

            AppendLog(message);

            MessageBox.Show(
                message,
                "Discord Setup",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _rosterVm.RefreshAsync();
                AppendLog("Roster refreshed.");
            }
            catch (Exception ex)
            {
                AppendLog("Refresh failed: " + ex.Message);
            }
        }

        private void AdminRosterGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AdminRosterGrid.SelectedItem is VtcRosterViewModel.RosterDriverRow row)
            {
                SelectedDriverTextBox.Text = row.Driver ?? "";
                TruckNumberTextBox.Text = row.Truck == "-" ? "" : (row.Truck ?? "");

                SelectRole(row.Role);
            }
        }

        private void SelectRole(string? role)
        {
            var target = (role ?? "").Trim();

            for (int i = 0; i < RoleComboBox.Items.Count; i++)
            {
                if (RoleComboBox.Items[i] is ComboBoxItem item &&
                    string.Equals((item.Content?.ToString() ?? "").Trim(), target, StringComparison.OrdinalIgnoreCase))
                {
                    RoleComboBox.SelectedIndex = i;
                    return;
                }
            }

            RoleComboBox.SelectedIndex = 4;
        }

        private async void SyncDriverScore_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var ok = await new DriverScoreSyncService().SyncAsync();

                MessageBox.Show(
                    ok ? "Driver score synced to Discord." : "Driver score sync failed. Make sure the ELD is linked to Discord.",
                    "Driver Score",
                    MessageBoxButton.OK,
                    ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Driver Score", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (AdminRosterGrid.SelectedItem is not VtcRosterViewModel.RosterDriverRow row)
                {
                    AppendLog("Select a driver first.");
                    return;
                }

                var win = new DriverProfileView(row)
                {
                    Owner = this
                };

                win.ShowDialog();
                AppendLog($"Opened driver profile: {row.Driver}");
            }
            catch (Exception ex)
            {
                AppendLog("Open profile failed: " + ex.Message);
            }
        }

        private async void AssignTruck_Click(object sender, RoutedEventArgs e)
        {
            if (!_canManage)
            {
                AppendLog("Blocked: insufficient permissions.");
                return;
            }

            if (AdminRosterGrid.SelectedItem is not VtcRosterViewModel.RosterDriverRow row)
            {
                AppendLog("Select a driver before assigning a truck.");
                return;
            }

            var truck = (TruckNumberTextBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(truck))
            {
                AppendLog("Enter a truck number first.");
                return;
            }

            try
            {
                AppendLog($"Saving truck assignment for {row.Driver}: {truck}");

                var ok = await SaveRosterUpdateAsync(
                    row,
                    truckNumber: truck,
                    role: row.Role,
                    status: row.Status);

                if (!ok)
                    return;

                row.Truck = truck;
                AdminRosterGrid.Items.Refresh();

                DriverProfileMasterStore.LinkTruck(
                    row.DiscordUserId,
                    row.Driver,
                    row.Driver,
                    truck,
                    truck,
                    "",
                    "",
                    "VTC Admin Assignment",
                    current: true);

                AppendLog($"Truck saved for {row.Driver}: {truck}");
                MessageBox.Show(
                    $"Truck assignment saved.\n\nDriver: {row.Driver}\nTruck: {truck}",
                    "Assign Truck",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppendLog("Assign truck failed: " + ex.Message);
                MessageBox.Show(
                    "Assign truck failed: " + ex.Message,
                    "Assign Truck",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void ChangeRole_Click(object sender, RoutedEventArgs e)
        {
            if (!_canManage)
            {
                AppendLog("Blocked: insufficient permissions.");
                return;
            }

            if (AdminRosterGrid.SelectedItem is not VtcRosterViewModel.RosterDriverRow row)
            {
                AppendLog("Select a driver before changing role.");
                return;
            }

            var role = (RoleComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString()?.Trim() ?? "Driver";

            try
            {
                AppendLog($"Saving role for {row.Driver}: {role}");

                var ok = await SaveRosterUpdateAsync(
                    row,
                    truckNumber: row.Truck,
                    role: role,
                    status: row.Status);

                if (!ok)
                    return;

                row.Role = role;
                AdminRosterGrid.Items.Refresh();

                AppendLog($"Role saved for {row.Driver}: {role}");
                MessageBox.Show(
                    $"Role updated.\n\nDriver: {row.Driver}\nRole: {role}",
                    "Save Role",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppendLog("Save role failed: " + ex.Message);
                MessageBox.Show(
                    "Save role failed: " + ex.Message,
                    "Save Role",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async Task<bool> SaveRosterUpdateAsync(
            VtcRosterViewModel.RosterDriverRow row,
            string? truckNumber,
            string? role,
            string? status)
        {
            var cfg = VtcConfigService.Load(forceReload: true);
            var baseUrl = (cfg.BotApiBaseUrl ?? "").Trim().TrimEnd('/');
            var guildId = (cfg.Discord?.GuildId ?? cfg.GuildId ?? "").Trim();

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                AppendLog("BotApiBaseUrl missing in vtc.config.json.");
                MessageBox.Show("BotApiBaseUrl is missing in vtc.config.json.", "VTC Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(guildId))
            {
                AppendLog("No active VTC guild selected.");
                MessageBox.Show("No active VTC guild selected.", "VTC Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

            var payload = new
            {
                driverId = (row.DiscordUserId ?? "").Trim(),
                name = (row.Driver ?? "").Trim(),
                discordUserId = (row.DiscordUserId ?? "").Trim(),
                discordUsername = "",
                truckNumber = (truckNumber ?? "").Trim(),
                role = (role ?? "").Trim(),
                status = (status ?? "").Trim(),
                notes = ""
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var url = $"{baseUrl}/api/vtc/roster/update?guildId={Uri.EscapeDataString(guildId)}";
            var resp = await http.PostAsync(url, content);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                AppendLog($"Roster update failed: {(int)resp.StatusCode}");
                MessageBox.Show(
                    $"Roster update failed.\n\nStatus: {(int)resp.StatusCode}\n\n{body}",
                    "VTC Admin",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private void LoadManagers()
        {
            try
            {
                ManagersListBox.ItemsSource = null;
                ManagersListBox.ItemsSource = AdminAccessService.GetManagers().ToList();
                AppendLog("Managers list loaded.");
            }
            catch (Exception ex)
            {
                AppendLog("Load managers failed: " + ex.Message);
            }
        }

        private void AddManager_Click(object sender, RoutedEventArgs e)
        {
            if (!_canManage)
            {
                AppendLog("Blocked: insufficient permissions.");
                return;
            }

            try
            {
                var name = (ManagerNameTextBox.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    AppendLog("Enter a manager name first.");
                    return;
                }

                var list = AdminAccessService.GetManagers().ToList();
                if (!list.Contains(name, StringComparer.OrdinalIgnoreCase))
                    list.Add(name);

                AdminAccessService.SaveManagers(list);

                ManagerNameTextBox.Text = "";
                LoadManagers();

                AppendLog($"Manager added: {name}");
            }
            catch (Exception ex)
            {
                AppendLog("Add manager failed: " + ex.Message);
            }
        }

        private void RemoveManager_Click(object sender, RoutedEventArgs e)
        {
            if (!_canManage)
            {
                AppendLog("Blocked: insufficient permissions.");
                return;
            }

            try
            {
                var selected = ManagersListBox.SelectedItem?.ToString()?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(selected))
                {
                    AppendLog("Select a manager first.");
                    return;
                }

                var list = AdminAccessService.GetManagers()
                    .Where(x => !string.Equals(x, selected, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                AdminAccessService.SaveManagers(list);
                LoadManagers();

                AppendLog($"Manager removed: {selected}");
            }
            catch (Exception ex)
            {
                AppendLog("Remove manager failed: " + ex.Message);
            }
        }

        private void OpenDispatch_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                (Application.Current.MainWindow as MainWindow)?.NavigateTo("dispatch");
                AppendLog("Opened Dispatch.");
            }
            catch (Exception ex)
            {
                AppendLog("Open Dispatch failed: " + ex.Message);
            }
        }

        private void OpenFleet_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                (Application.Current.MainWindow as MainWindow)?.NavigateTo("fleet");
                AppendLog("Opened Fleet.");
            }
            catch (Exception ex)
            {
                AppendLog("Open Fleet failed: " + ex.Message);
            }
        }

        private void OpenMaintenance_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                (Application.Current.MainWindow as MainWindow)?.NavigateTo("maintenance");
                AppendLog("Opened Maintenance.");
            }
            catch (Exception ex)
            {
                AppendLog("Open Maintenance failed: " + ex.Message);
            }
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            AdminLogTextBox.Clear();
        }

        private void AppendLog(string text)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {text}";
            if (string.IsNullOrWhiteSpace(AdminLogTextBox.Text))
                AdminLogTextBox.Text = line;
            else
                AdminLogTextBox.AppendText(Environment.NewLine + line);

            AdminLogTextBox.ScrollToEnd();
        }

        private void OpenOperationsHub_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                FleetEconomyIntegration.OpenOperationsCommandCenterWindow(Window.GetWindow(this));
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Operations Command Center",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}