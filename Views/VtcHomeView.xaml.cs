using Microsoft.Win32;
using OverWatchELD.Models.Events;
using OverWatchELD.Services;
using OverWatchELD.Services.Convoy;
using OverWatchELD.Services.Economy;
using OverWatchELD.Services.Events;
using OverWatchELD.Services.Fleet;
using OverWatchELD.ViewModels;
using OverWatchELD.Views;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OverWatchELD.Views.Fleet;

namespace OverWatchELD.Views
{
    public partial class VtcHomeView : UserControl
    {
        private static readonly JsonSerializerOptions JsonReadOpts = new() { PropertyNameCaseInsensitive = true };
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        private readonly VtcFleetSnapshotService _fleetSnapshotService = new();
        private readonly ConvoyStore _convoyStore = new();
        private readonly VtcEventStore _eventStore = new();
        private readonly VtcRosterViewModel _rosterVm = new();

        private bool _loaded;
        private readonly DispatcherTimer _announcementsTimer = new() { Interval = TimeSpan.FromSeconds(10) };
        private DateTimeOffset? _lastAnnouncementsSeenUtc;

        private void FleetSnapshotList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                if (FleetSnapshotList?.SelectedItem is not FleetSnapshotItem item)
                    return;

                if (string.IsNullOrWhiteSpace(item.Truck) || item.Truck.Contains("No fleet trucks", StringComparison.OrdinalIgnoreCase))
                    return;

                var win = new FleetSnapshotTruckDetailsWindow(
                    item.TruckId,
                    item.Truck,
                    item.Driver)
                {
                    Owner = Window.GetWindow(this)
                };

                win.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to open truck details: " + ex.Message,
                    "Fleet Snapshot",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private sealed class GuildItem
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";

            public override string ToString()
            {
                if (string.IsNullOrWhiteSpace(Name)) return Id;
                return $"{Name} ({Id})";
            }
        }

        private sealed class VtcBrandingConfig
        {
            public string BannerImagePath { get; set; } = "";
            public string IconImagePath { get; set; } = "";
        }

        public sealed class VtcLeaderboardItem
        {
            public string Name { get; set; } = "";
            public string MilesDisplay { get; set; } = "";
            public Brush StatusBrush { get; set; } = Brushes.Gray;
        }

        public sealed class VtcActivityItem
        {
            public string Message { get; set; } = "";
            public string TimeDisplay { get; set; } = "";
            public Brush StatusBrush { get; set; } = Brushes.Gray;
        }

        private readonly VtcDriverActivityFeedService _driverActivityFeedService = new();
        private static void ApplyFleetCommandAssignmentsToRoster(IEnumerable<object> rows)
        {
            try
            {
                if (rows == null)
                    return;

                var store = new FleetCommandStore();

                var assignedTrucks = store.LoadAll()
                    .Where(t => !string.IsNullOrWhiteSpace(t.AssignedDriver))
                    .GroupBy(t => t.AssignedDriver.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        g => g.Key,
                        g => g.OrderByDescending(x => x.UpdatedUtc).First(),
                        StringComparer.OrdinalIgnoreCase);

                foreach (var row in rows)
                {
                    if (row == null)
                        continue;

                    var driverName = FirstNonEmpty(
    ReadObjectString(row, "Driver"),
    ReadObjectString(row, "DriverName"),
    ReadObjectString(row, "DisplayName"),
    ReadObjectString(row, "Username"),
    ReadObjectString(row, "DiscordUsername"),
    ReadObjectString(row, "Name"));

                    if (string.IsNullOrWhiteSpace(driverName))
                        continue;

                    if (!assignedTrucks.TryGetValue(driverName.Trim(), out var truck))
                        continue;

                    var truckText = FirstNonEmpty(
                        truck.TruckName,
                        truck.Model,
                        truck.TruckNumber,
                        truck.PlateNumber);

                    SetObjectString(row, "Truck", truckText);
                    SetObjectString(row, "TruckName", truckText);
                    SetObjectString(row, "TruckNumber", truck.TruckNumber);
                    SetObjectString(row, "PlateNumber", truck.PlateNumber);
                    SetObjectString(row, "Location", truck.Location);
                }
            }
            catch
            {
            }
        }

        private static string ReadObjectString(object? obj, string propertyName)
        {
            try
            {
                if (obj == null) return "";
                var prop = obj.GetType().GetProperty(propertyName);
                var value = prop?.GetValue(obj);
                return value?.ToString()?.Trim() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static void SetObjectString(object? obj, string propertyName, string? value)
        {
            try
            {
                if (obj == null) return;

                var prop = obj.GetType().GetProperty(propertyName);
                if (prop == null || !prop.CanWrite)
                    return;

                prop.SetValue(obj, value ?? "");
            }
            catch
            {
            }
        }
        public sealed class FleetSnapshotItem
        {
            public string TruckId { get; set; } = "";
            public string Truck { get; set; } = "";
            public string Driver { get; set; } = "";
            public bool IsCurrentTruck { get; set; }
            public string ActionText { get; set; } = "Use Truck";
            public string Location { get; set; } = "";
            public string Status { get; set; } = "";
            public string ServiceDue { get; set; } = "";
            public string InspectionStatus { get; set; } = "";
            public string Health { get; set; } = "";
            public Brush StatusBrush { get; set; } = Brushes.Gray;
            public Brush HealthBrush { get; set; } = Brushes.Gray;
        }

        public sealed class AnnouncementItem
        {
            public string Author { get; set; } = "";
            public string Message { get; set; } = "";
            public string TimeDisplay { get; set; } = "";
            public string SourceLabel { get; set; } = "Discord";
            public Brush StatusBrush { get; set; } = Brushes.Goldenrod;
        }

        public VtcHomeView()
        {
            InitializeComponent();
            Loaded += VtcHomeView_Loaded;
            Unloaded += VtcHomeView_Unloaded;

            _announcementsTimer.Tick += async (_, __) =>
            {
                await RefreshAnnouncementsAsync();
                await RefreshPhase1DashboardAsync();
            };
        }

        private void VtcHomeView_Loaded(object sender, RoutedEventArgs e)
        {
            if (_loaded) return;
            _loaded = true;

            try { VtcConfigService.EnsureCreated(); } catch { }

            // Fast local-only setup so the dashboard shows immediately.
            HydrateFromSavedPairing();
            LoadBranding();

            try { _announcementsTimer.Start(); } catch { }

            // Heavy VTC/bot/roster refresh happens AFTER the UI is visible.
            _ = Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await Task.Delay(750);
                    await RefreshAllAsync();
                }
                catch
                {
                    // Never let slow/offline Railway/Discord block app loading.
                }
            }), DispatcherPriority.Background);
        }

        private void VtcHomeView_Unloaded(object sender, RoutedEventArgs e)
        {
            try { _announcementsTimer.Stop(); } catch { }
        }

        private void OpenGarage_Click(object sender, RoutedEventArgs e)
        {
            var win = new VtcGarageWindow
            {
                Owner = Window.GetWindow(this)
            };

            win.Show();
        }

        private void HydrateFromSavedPairing()
        {
            try
            {
                var cfg = VtcConfigService.Load(forceReload: true) ?? new VtcConfig();
                cfg.Discord ??= new VtcConfig.DiscordConfig();

                var changed = false;

                try
                {
                    var pairing = VtcPairingStore.Load();
                    if (pairing != null)
                    {
                        var pairingGuildId = ReadStringProperty(pairing, "GuildId");
                        var pairingVtcName = ReadStringProperty(pairing, "VtcName");
                        var pairingDiscordUserId = ReadStringProperty(pairing, "DiscordUserId");
                        var pairingDiscordUsername = ReadStringProperty(pairing, "DiscordUsername");

                        if (!string.IsNullOrWhiteSpace(pairingGuildId) &&
                            string.IsNullOrWhiteSpace(cfg.Discord.GuildId))
                        {
                            cfg.Discord.GuildId = pairingGuildId.Trim();
                            changed = true;
                        }

                        if (!string.IsNullOrWhiteSpace(pairingVtcName) &&
                            string.IsNullOrWhiteSpace(cfg.VtcName))
                        {
                            cfg.VtcName = pairingVtcName.Trim();
                            changed = true;
                        }

                        if (!string.IsNullOrWhiteSpace(pairingDiscordUserId))
                        {
                            try
                            {
                                var existingIdentity = DiscordIdentityStore.Load();
                                if (existingIdentity == null ||
                                    string.IsNullOrWhiteSpace(ReadStringProperty(existingIdentity, "DiscordUserId")))
                                {
                                    DiscordIdentityStore.Save(new DiscordIdentity
                                    {
                                        GuildId = (cfg.Discord?.GuildId ?? "").Trim(),
                                        DiscordUserId = pairingDiscordUserId.Trim(),
                                        DiscordUsername = (pairingDiscordUsername ?? "").Trim()
                                    });
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }

                try
                {
                    var ident = DiscordIdentityStore.Load();
                    if (ident != null)
                    {
                        var identGuildId = ReadStringProperty(ident, "GuildId");
                        if (!string.IsNullOrWhiteSpace(identGuildId) &&
                            string.IsNullOrWhiteSpace(cfg.Discord.GuildId))
                        {
                            cfg.Discord.GuildId = identGuildId.Trim();
                            changed = true;
                        }
                    }
                }
                catch { }

                if (changed)
                {
                    try { VtcConfigService.Save(cfg); } catch { }
                }

                ApplyConnectionHeaderState(cfg);
                ApplySetupVisibility(cfg);
            }
            catch
            {
                try
                {
                    var cfg = VtcConfigService.Load(forceReload: true);
                    ApplyConnectionHeaderState(cfg);
                    ApplySetupVisibility(cfg);
                }
                catch { }
            }
        }

        private void ApplyConnectionHeaderState(VtcConfig cfg)
        {
            try
            {
                var safeVtcName = (cfg?.VtcName ?? "").Trim();
                var gid = (cfg?.Discord?.GuildId ?? "").Trim();

                if (VtcNameText != null)
                    VtcNameText.Text = string.IsNullOrWhiteSpace(safeVtcName) ? "VTC: Standalone" : $"VTC: {safeVtcName}";

                if (VtcHeaderTitle != null)
                    VtcHeaderTitle.Text = string.IsNullOrWhiteSpace(safeVtcName) ? "VTC Setup" : safeVtcName + " VTC";

                if (SelectedGuildText != null)
                    SelectedGuildText.Text = string.IsNullOrWhiteSpace(gid) ? "GuildId: (not paired yet)" : $"GuildId: {gid}";
            }
            catch { }
        }

        private void FleetAnalytics_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                FleetEconomyIntegration.OpenFleetAnalyticsDashboardWindow(
                    Window.GetWindow(this));
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Fleet Analytics",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void ApplySetupVisibility(VtcConfig? cfg)
        {
            try
            {
                var gid = (cfg?.Discord?.GuildId ?? "").Trim();
                var vtcName = (cfg?.VtcName ?? "").Trim();
                var isPaired = !string.IsNullOrWhiteSpace(gid);

                if (PairingPanel != null)
                    PairingPanel.Visibility = isPaired ? Visibility.Collapsed : Visibility.Visible;

                if (MainDashboardPanel != null)
                    MainDashboardPanel.Visibility = Visibility.Visible;

                if (PairStatusText != null && !isPaired)
                {
                    PairStatusText.Text = string.IsNullOrWhiteSpace(vtcName)
                        ? "Paste your !link code to connect this ELD to your VTC."
                        : $"Paste your !link code to connect to {vtcName}.";
                }

                ApplyDeveloperOnlyChangeVtcVisibility();
            }
            catch { }
        }

        private static string ReadStringProperty(object? obj, string propertyName)
        {
            try
            {
                if (obj == null) return "";
                var prop = obj.GetType().GetProperty(propertyName);
                if (prop == null) return "";
                var value = prop.GetValue(obj) as string;
                return (value ?? "").Trim();
            }
            catch
            {
                return "";
            }
        }

        private async Task RefreshAllAsync()
        {
            HydrateFromSavedPairing();
            await UpdateBotApiStatusAsync();
            await RefreshRosterAsync();
            await RefreshAnnouncementsAsync();
            await RefreshPhase1DashboardAsync();
            LoadBranding();
        }

        private async void RefreshAll_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAllAsync();
        }

        private void DriverScores_Click(object sender, RoutedEventArgs e)
        {
            FleetEconomyIntegration.OpenDriverSafetyPerformanceWindow(Window.GetWindow(this));
        }

        private void NavigateDispatch_Click(object sender, RoutedEventArgs e)
        {
            try { (Application.Current.MainWindow as MainWindow)?.NavigateTo("dispatch"); } catch { }
        }

        private void NavigateMaintenance_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var role = GetCurrentUserVtcRole();

                var win = new Window
                {
                    Title = "VTC Maintenance",
                    Width = 1450,
                    Height = 900,
                    Owner = Window.GetWindow(this),
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Background = Brushes.Black,
                    Content = new VtcMaintenanceView(role)
                };

                win.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to open VTC Maintenance: " + ex.Message,
                    "VTC Maintenance",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private string GetCurrentUserVtcRole()
        {
            try
            {
                var ident = DiscordIdentityStore.Load();
                var myDiscordId = (ident?.DiscordUserId ?? "").Trim();

                if (string.IsNullOrWhiteSpace(myDiscordId))
                    return "Driver";

                var me = _rosterVm.Drivers.FirstOrDefault(r =>
                    string.Equals((r.DiscordUserId ?? "").Trim(), myDiscordId, StringComparison.OrdinalIgnoreCase));

                return string.IsNullOrWhiteSpace(me?.Role) ? "Driver" : me.Role.Trim();
            }
            catch
            {
                return "Driver";
            }
        }

        private void NavigateFleet_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new VtcCompanyFleetWindow
                {
                    Owner = Window.GetWindow(this)
                };

                win.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to open company fleet: " + ex.Message,
                    "Company Fleet",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void FuelMaintenance_Click(object sender, RoutedEventArgs e)
        {
            FleetEconomyIntegration.OpenFuelMaintenanceAutomationWindow(Window.GetWindow(this));
        }

        private void OpenRoster_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var rosterView = new VtcRosterView();

                try
                {
                    rosterView.DataContext = _rosterVm;
                }
                catch
                {
                    rosterView.DataContext = DataContext;
                }

                var win = new Window
                {
                    Title = "VTC Roster",
                    Width = 1200,
                    Height = 800,
                    Owner = Window.GetWindow(this),
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Content = rosterView
                };

                win.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to open Roster: " + ex.Message,
                    "Roster",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OpenLiveMap_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new LiveMapWindow
                {
                    Owner = Window.GetWindow(this)
                };

                win.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to open Live Map: " + ex.Message,
                    "Live Map",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OpenConvoy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new ConvoyPageWindow
                {
                    Owner = Window.GetWindow(this)
                };

                if (win.DataContext == null)
                    win.DataContext = DataContext;

                win.ShowDialog();
                UpdateConvoyCard();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to open Convoy: " + ex.Message,
                    "Convoy",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async Task UpdateBotApiStatusAsync()
        {
            try
            {
                var cfg = VtcConfigService.Load(forceReload: true);

                ApplyConnectionHeaderState(cfg);
                ApplySetupVisibility(cfg);

                var baseUrl = (cfg.BotApiBaseUrl ?? "").Trim().TrimEnd('/');
                if (string.IsNullOrWhiteSpace(baseUrl))
                {
                    if (BotStatusText != null)
                        BotStatusText.Text = "Bot API: Missing BotApiBaseUrl in vtc.config.json";
                    return;
                }

                var healthUrl = baseUrl + "/health";
                var health = await Http.GetStringAsync(healthUrl);
                if (BotStatusText != null)
                    BotStatusText.Text = (health ?? "").Contains("ok", StringComparison.OrdinalIgnoreCase)
                        ? "Bot API: Online"
                        : "Bot API: Online";

                var gid = (cfg.Discord?.GuildId ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(gid))
                {
                    var vtcName = await TryGetVtcNameAsync(baseUrl, gid);
                    if (!string.IsNullOrWhiteSpace(vtcName) &&
                        !string.Equals(vtcName, cfg.VtcName ?? "", StringComparison.Ordinal))
                    {
                        cfg.VtcName = vtcName;
                        VtcConfigService.Save(cfg);
                        ApplyConnectionHeaderState(cfg);
                        ApplySetupVisibility(cfg);
                    }
                }
            }
            catch (Exception ex)
            {
                if (BotStatusText != null)
                    BotStatusText.Text = "Bot API: Offline (" + ex.Message + ")";
            }
        }

        private static async Task<string> TryGetVtcNameAsync(string baseUrl, string guildId)
        {
            try
            {
                var url = baseUrl.TrimEnd('/') + "/api/vtc/name?guildId=" + Uri.EscapeDataString(guildId);
                var raw = (await Http.GetStringAsync(url))?.Trim() ?? "";
                if (!raw.StartsWith("{")) return raw.Trim('"').Trim();

                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("vtcName", out var n) && n.ValueKind == JsonValueKind.String)
                    return (n.GetString() ?? "").Trim();
                if (doc.RootElement.TryGetProperty("name", out var n2) && n2.ValueKind == JsonValueKind.String)
                    return (n2.GetString() ?? "").Trim();
            }
            catch { }

            return "";
        }

        private void ApplyDeveloperOnlyChangeVtcVisibility()
        {
            try
            {
                var button = FindName("ChangeVtcButton") as Button;

                if (button == null)
                    return;

                button.Visibility =
                    IsDeveloperUser()
                        ? Visibility.Visible
                        : Visibility.Collapsed;
            }
            catch
            {
            }
        }

        private bool IsDeveloperUser()
        {
            try
            {
                var identity = DiscordIdentityStore.Load();

                var currentId =
                    (identity?.DiscordUserId ?? "")
                    .Trim();

                var developerId =
                    (Environment.GetEnvironmentVariable("OVERWATCH_DEV_DISCORD_ID") ?? "")
                    .Trim();

                if (string.IsNullOrWhiteSpace(currentId))
                    return false;

                if (string.IsNullOrWhiteSpace(developerId))
                    return false;

                return string.Equals(
                    currentId,
                    developerId,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private async void ChangeVtc_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cfg = VtcConfigService.Load(forceReload: true);
                var baseUrl = (cfg.BotApiBaseUrl ?? "").Trim().TrimEnd('/');
                if (string.IsNullOrWhiteSpace(baseUrl))
                    throw new InvalidOperationException("BotApiBaseUrl is missing in vtc.config.json.");

                var servers = await FetchServersAsync(baseUrl);
                if (servers.Count == 0)
                {
                    MessageBox.Show("No Discord servers were returned by the bot.", "Change VTC",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var currentGuildId = (cfg.Discord?.GuildId ?? "").Trim();
                var pick = ShowGuildPickerDialog(servers, currentGuildId);
                if (pick is null) return;

                var pickedId = (pick.Id ?? "").Trim();
                if (string.IsNullOrWhiteSpace(pickedId))
                    return;

                var confirm = MessageBox.Show(
                    $"Switch this ELD to:\n\n{pick}\n\nThis will update VTC data (roster, announcements, etc.) to that server.",
                    "Confirm VTC Switch",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirm != MessageBoxResult.Yes) return;

                if (cfg.Discord == null) cfg.Discord = new VtcConfig.DiscordConfig();
                cfg.Discord.GuildId = pickedId;
                cfg.VtcName = "";
                VtcConfigService.Save(cfg);

                try
                {
                    var ident = DiscordIdentityStore.Load();
                    if (ident != null)
                    {
                        ident.GuildId = pickedId;
                        DiscordIdentityStore.Save(ident);
                    }
                }
                catch { }

                try
                {
                    dynamic pairing = VtcPairingStore.Load();
                    if (pairing != null)
                    {
                        pairing.GuildId = pickedId;
                        pairing.VtcName = "";
                        VtcPairingStore.Save(pairing);
                    }
                }
                catch { }

                _lastAnnouncementsSeenUtc = null;
                SetAnnouncementUnread(false);

                await RefreshAllAsync();
                LoadBranding();

                MessageBox.Show("VTC switched successfully.", "Change VTC", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to change VTC: " + ex.Message, "Change VTC", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static async Task<List<GuildItem>> FetchServersAsync(string baseUrl)
        {
            var url = baseUrl.TrimEnd('/') + "/api/vtc/servers";
            var raw = (await Http.GetStringAsync(url))?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(raw)) return new List<GuildItem>();

            var items = new List<GuildItem>();

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            JsonElement arr;
            if (!(root.TryGetProperty("servers", out arr) ||
                  root.TryGetProperty("guilds", out arr) ||
                  root.TryGetProperty("items", out arr) ||
                  root.TryGetProperty("data", out arr)))
            {
                if (root.ValueKind == JsonValueKind.Array) arr = root;
                else arr = default;
            }

            if (arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in arr.EnumerateArray())
                {
                    var id = el.TryGetProperty("id", out var idEl) ? (idEl.GetString() ?? "") : "";
                    var name = el.TryGetProperty("name", out var nameEl) ? (nameEl.GetString() ?? "") : "";
                    id = (id ?? "").Trim();
                    name = (name ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    items.Add(new GuildItem { Id = id, Name = name });
                }
            }

            return items.OrderBy(x => x.Name).ThenBy(x => x.Id).ToList();
        }

        private GuildItem? ShowGuildPickerDialog(List<GuildItem> servers, string currentGuildId)
        {
            var win = new Window
            {
                Title = "Select Discord Server",
                Width = 520,
                Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                Background = Brushes.White
            };

            var grid = new Grid { Margin = new Thickness(12) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var top = new TextBlock
            {
                Text = "Choose the Discord server (VTC) for this ELD. This does NOT expose any personal Discord name.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(top, 0);

            var list = new ListBox { ItemsSource = servers, Margin = new Thickness(0, 0, 0, 10) };
            var current = servers.FirstOrDefault(s => s.Id == currentGuildId);
            if (current != null) list.SelectedItem = current;
            else if (servers.Count > 0) list.SelectedIndex = 0;
            Grid.SetRow(list, 1);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "Use Selected", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 10, 0), IsDefault = true };
            var cancel = new Button { Content = "Cancel", Padding = new Thickness(12, 6, 12, 6), IsCancel = true };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            Grid.SetRow(buttons, 2);

            grid.Children.Add(top);
            grid.Children.Add(list);
            grid.Children.Add(buttons);

            win.Content = grid;

            GuildItem? picked = null;
            ok.Click += (_, __) =>
            {
                picked = list.SelectedItem as GuildItem;
                win.DialogResult = picked != null;
                win.Close();
            };

            var res = win.ShowDialog();
            if (res == true) return picked;
            return null;
        }

        private void PairHelp_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "To pair:\n\n1) In Discord, use the bot command: !link 123456 (example)\n2) Copy the pairing code you receive\n3) Paste it here and press Pair\n\nAfter pairing, you can use 'Change VTC' to switch servers later.",
                "Pairing Help",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private async void Pair_Click(object sender, RoutedEventArgs e)
        {
            if (PairStatusText != null) PairStatusText.Text = "";

            var code = (PairCodeTextBox?.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(code))
            {
                if (PairStatusText != null) PairStatusText.Text = "Enter a pairing code.";
                ApplySetupVisibility(VtcConfigService.Load(forceReload: true));
                return;
            }

            try
            {
                var cfg = VtcConfigService.Load(forceReload: true);
                var baseUrl = (cfg.BotApiBaseUrl ?? "").Trim().TrimEnd('/');

                if (string.IsNullOrWhiteSpace(baseUrl))
                    throw new InvalidOperationException("BotApiBaseUrl is missing in vtc.config.json.");

                var url = baseUrl + "/api/vtc/pair/claim?code=" + Uri.EscapeDataString(code);
                var raw = (await Http.GetStringAsync(url))?.Trim() ?? "";

                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;

                var ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
                if (!ok)
                {
                    var err = root.TryGetProperty("error", out var er) ? (er.GetString() ?? "PairFailed") : "PairFailed";
                    var msg = root.TryGetProperty("message", out var me) ? (me.GetString() ?? "") : "";
                    if (PairStatusText != null)
                        PairStatusText.Text = $"Pairing failed: {err} {msg}".Trim();

                    ApplySetupVisibility(cfg);
                    return;
                }

                if (cfg.Discord == null)
                    cfg.Discord = new VtcConfig.DiscordConfig();

                var guildId = root.TryGetProperty("guildId", out var gidEl) ? (gidEl.GetString() ?? "").Trim() : "";
                var vtcName = root.TryGetProperty("vtcName", out var vn) ? (vn.GetString() ?? "").Trim() : "";
                var discordUserId = root.TryGetProperty("discordUserId", out var du) ? (du.GetString() ?? "").Trim() : "";
                var discordUsername = root.TryGetProperty("discordUsername", out var dn) ? (dn.GetString() ?? "").Trim() : "";

                if (string.IsNullOrWhiteSpace(guildId))
                {
                    if (PairStatusText != null)
                        PairStatusText.Text = "Pairing failed: bot returned no GuildId. This ELD is still standalone.";

                    cfg.Discord.GuildId = "";
                    cfg.VtcName = "";
                    cfg.Enabled = false;
                    VtcConfigService.Save(cfg);

                    ApplyConnectionHeaderState(cfg);
                    ApplySetupVisibility(cfg);
                    return;
                }

                cfg.Discord.GuildId = guildId;

                if (string.IsNullOrWhiteSpace(vtcName))
                {
                    try
                    {
                        vtcName = await TryGetVtcNameAsync(baseUrl, guildId);
                    }
                    catch
                    {
                        vtcName = "";
                    }
                }

                if (string.IsNullOrWhiteSpace(vtcName))
                    vtcName = "Connected VTC";

                cfg.VtcName = vtcName;
                cfg.Enabled = true;
                cfg.PairCode = "";

                try
                {
                    DiscordIdentityStore.Save(new DiscordIdentity
                    {
                        GuildId = guildId,
                        DiscordUserId = discordUserId,
                        DiscordUsername = discordUsername
                    });
                }
                catch { }

                try
                {
                    VtcPairingStore.Save(new VtcPairingStore.Pairing
                    {
                        GuildId = guildId,
                        VtcName = vtcName,
                        DiscordUserId = discordUserId,
                        DiscordUsername = discordUsername,
                        PairedUtc = DateTimeOffset.UtcNow
                    });
                }
                catch { }

                VtcConfigService.Save(cfg);

                HydrateFromSavedPairing();
                ApplyConnectionHeaderState(cfg);
                ApplySetupVisibility(cfg);

                if (PairStatusText != null)
                    PairStatusText.Text = $"✅ Paired to {vtcName}.";

                _lastAnnouncementsSeenUtc = null;
                SetAnnouncementUnread(false);

                await RefreshAllAsync();
                LoadBranding();

                MessageBox.Show(
                    $"Paired successfully to: {vtcName}",
                    "VTC Pairing",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                if (PairStatusText != null)
                    PairStatusText.Text = "Pairing error: " + ex.Message;

                try
                {
                    ApplySetupVisibility(VtcConfigService.Load(forceReload: true));
                }
                catch { }
            }
        }

        private async Task RefreshRosterAsync()
        {
            try
            {
                await _rosterVm.RefreshAsync();

                // Overlay Fleet Command truck assignments onto roster rows.
                ApplyFleetCommandAssignmentsToRoster(_rosterVm.Drivers.Cast<object>());
            }
            catch { }
        }

        private async Task RefreshPhase1DashboardAsync()
        {
            try
            {
                var rows = _rosterVm.Drivers.ToList();

                // Make Fleet Command assignments appear in VTC dashboard, roster, and snapshot.
                ApplyFleetCommandAssignmentsToRoster(rows.Cast<object>());

                UpdateSummaryCards(rows);
                UpdateTopDrivers(rows);
                UpdateActivityFeed(rows);
                UpdateFleetSnapshot(rows);

                try
                {
                    VtcTelemetryMaintenanceSyncService.SyncFromRows(rows);
                }
                catch
                {
                }

                UpdateConvoyCard();
                UpdateConvoyCard();
                UpdateUpcomingEventCard();
                UpdateAdminButtonVisibility(rows);
            }
            catch
            {
                SetDashboardEmpty();
            }

            await Task.CompletedTask;
        }

        private void UpdateSummaryCards(List<VtcRosterViewModel.RosterDriverRow> rows)
        {
            var totalDrivers = rows.Count;
            var onlineDrivers = rows.Count(r => StatusLooksOnline(r.Status));

            var fleetTrucks = GetFleetCommandTruckCount(rows);

            // Real VTC mileage from delivered Dispatch loads / Economy transactions.
            // This replaces the old placeholder formula:
            // weeklyMiles = onlineDrivers * 250; monthlyMiles = weeklyMiles * 4.
            var mileage = VtcHomeRealMetricsService.BuildMileageSummary();
            var weeklyMiles = mileage.WeeklyMiles;
            var monthlyMiles = mileage.MonthlyMiles;

            SafeSetText(DriversCountText, totalDrivers.ToString("N0", CultureInfo.InvariantCulture));
            SafeSetText(OnlineDriversText, onlineDrivers.ToString("N0", CultureInfo.InvariantCulture));
            SafeSetText(FleetCountText, fleetTrucks.ToString("N0", CultureInfo.InvariantCulture));
            SafeSetText(WeeklyMilesText, weeklyMiles.ToString("N0", CultureInfo.InvariantCulture));
            SafeSetText(MonthlyMilesText, monthlyMiles.ToString("N0", CultureInfo.InvariantCulture));
        }


        private static int GetFleetCommandTruckCount(List<VtcRosterViewModel.RosterDriverRow> rows)
        {
            try
            {
                var count = new FleetCommandStore()
                    .LoadAll()
                    .Count(t => !string.IsNullOrWhiteSpace(FirstNonEmpty(
                        t.TruckNumber,
                        t.TruckName,
                        t.Model,
                        t.PlateNumber)));

                if (count > 0)
                    return count;
            }
            catch
            {
            }

            return rows
                .Select(r => (r.Truck ?? "").Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t) && t != "-")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
        }

        private void UpdateTopDrivers(List<VtcRosterViewModel.RosterDriverRow> rows)
        {
            var ranked = rows
                .OrderByDescending(r => StatusLooksOnline(r.Status))
                .ThenBy(r => r.Driver)
                .Take(10)
                .Select((r, index) => new VtcLeaderboardItem
                {
                    Name = $"{index + 1}. {SafeDriver(r.Driver)}",
                    MilesDisplay = StatusLooksOnline(r.Status) ? "Online" : "Offline",
                    StatusBrush = GetStatusBrush(r.Status)
                })
                .ToList();

            if (ranked.Count == 0)
            {
                ranked.Add(new VtcLeaderboardItem
                {
                    Name = "No leaderboard data yet.",
                    MilesDisplay = "",
                    StatusBrush = Brushes.Gray
                });
            }

            SafeBindListBox(TopDriversList, ranked);
        }

        private void UpdateActivityFeed(List<VtcRosterViewModel.RosterDriverRow> rows)
        {
            var items = new List<VtcActivityItem>();

            try
            {
                var historyItems = _driverActivityFeedService.BuildActivityFeed();

                items.AddRange(historyItems.Select(x => new VtcActivityItem
                {
                    Message = x.Message,
                    TimeDisplay = x.TimeDisplay,
                    StatusBrush = x.Kind.Equals("Progress", StringComparison.OrdinalIgnoreCase)
                        ? Brushes.LimeGreen
                        : Brushes.DodgerBlue
                }));
            }
            catch
            {
            }

            if (items.Count == 0)
            {
                items = rows
                    .OrderByDescending(r => StatusLooksOnline(r.Status))
                    .ThenBy(r => r.Driver)
                    .Take(12)
                    .Select(r => new VtcActivityItem
                    {
                        Message = BuildActivityMessage(r),
                        TimeDisplay = string.IsNullOrWhiteSpace(r.LastSeen) || r.LastSeen == "-"
                            ? "Roster snapshot"
                            : $"Last seen {r.LastSeen}",
                        StatusBrush = GetStatusBrush(r.Status)
                    })
                    .ToList();
            }

            if (items.Count == 0)
            {
                items.Add(new VtcActivityItem
                {
                    Message = "No recent VTC driver activity yet.",
                    TimeDisplay = "",
                    StatusBrush = Brushes.Gray
                });
            }

            SafeBindListBox(ActivityFeedList, items);
        }

        private void UpdateFleetSnapshot(List<VtcRosterViewModel.RosterDriverRow> rows)
        {
            var serviceItems = _fleetSnapshotService.BuildSnapshot(rows);

            var items = serviceItems
                .Select(x => new FleetSnapshotItem
                {
                    TruckId = x.TruckId,
                    Truck = x.Truck,
                    Driver = x.Driver,
                    IsCurrentTruck = x.IsCurrentTruck,
                    ActionText = x.ActionText,
                    Location = x.Location,
                    Status = x.Status,
                    ServiceDue = x.ServiceDue,
                    InspectionStatus = x.InspectionStatus,
                    Health = x.Health,
                    StatusBrush = x.StatusBrush,
                    HealthBrush = x.HealthBrush
                })
                .ToList();

            if (items.Count == 0)
            {
                items.Add(new FleetSnapshotItem
                {
                    TruckId = "",
                    Truck = "No fleet trucks yet.",
                    Driver = "",
                    IsCurrentTruck = false,
                    ActionText = "",
                    Location = "",
                    Status = "",
                    ServiceDue = "",
                    InspectionStatus = "",
                    Health = "",
                    StatusBrush = Brushes.Gray,
                    HealthBrush = Brushes.Gray
                });
            }

            SafeBindListBox(FleetSnapshotList, items);
        }


        private async void UseFleetTruck_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not Button button)
                    return;

                var truckId = (button.Tag?.ToString() ?? "").Trim();
                if (string.IsNullOrWhiteSpace(truckId))
                    return;

                var store = new FleetCommandStore();
                var activeStore = new ActiveTruckSelectionStore();
                var trucks = store.LoadAll();
                var selected = trucks.FirstOrDefault(t =>
                    string.Equals(t.Id, truckId, StringComparison.OrdinalIgnoreCase));

                if (selected == null)
                {
                    MessageBox.Show("That truck could not be found in the local fleet store.", "Fleet Truck", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var driverName = GetCurrentDriverNameForFleet();
                var discordId = GetCurrentDiscordUserIdForFleet();

                if (string.IsNullOrWhiteSpace(driverName))
                    driverName = FirstNonEmpty(selected.AssignedDriver, EldDriverIdentityResolver.DriverName());

                activeStore.SetActiveTruck(driverName, discordId, selected.Id);

                foreach (var truck in trucks)
                {
                    var sameDriver =
                        (!string.IsNullOrWhiteSpace(discordId) && string.Equals(truck.DriverDiscordId, discordId, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrWhiteSpace(driverName) && string.Equals(truck.AssignedDriver, driverName, StringComparison.OrdinalIgnoreCase));

                    if (string.Equals(truck.Id, selected.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        truck.AssignedDriver = FirstNonEmpty(truck.AssignedDriver, driverName);
                        truck.DriverDiscordId = FirstNonEmpty(truck.DriverDiscordId, discordId);
                        truck.Status = "Active";
                        truck.IsActive = true;
                        truck.IsParked = false;
                        truck.UpdatedUtc = DateTimeOffset.UtcNow;
                        store.Save(truck);
                    }
                    else if (sameDriver)
                    {
                        truck.Status = "Inactive";
                        truck.IsParked = true;
                        truck.ParkedUtc = DateTime.UtcNow;
                        truck.UpdatedUtc = DateTimeOffset.UtcNow;
                        store.Save(truck);
                    }
                }

                await RefreshRosterAsync();
                await RefreshPhase1DashboardAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not set active truck:\n" + ex.Message, "Fleet Truck", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static string GetCurrentDriverNameForFleet()
        {
            try
            {
                var app = Application.Current as OverWatchELD.App;
                var fromSession = FirstNonEmpty(
                    app?.Session?.DriverName);

                if (!string.IsNullOrWhiteSpace(fromSession) &&
                    !string.Equals(fromSession, "Driver", StringComparison.OrdinalIgnoreCase))
                    return fromSession;
            }
            catch { }

            try
            {
                var ident = DiscordIdentityStore.Load();
                if (!string.IsNullOrWhiteSpace(ident?.DiscordUsername))
                    return ident.DiscordUsername.Trim();
            }
            catch { }

            return EldDriverIdentityResolver.DriverName();
        }

        private static string GetCurrentDiscordUserIdForFleet()
        {
            try
            {
                var ident = DiscordIdentityStore.Load();
                if (!string.IsNullOrWhiteSpace(ident?.DiscordUserId))
                    return ident.DiscordUserId.Trim();
            }
            catch { }

            return "";
        }

        private void UpdateConvoyCard()
        {
            try
            {
                _ = _convoyStore.GetLatest();
            }
            catch
            {
            }
        }

        private void UpdateUpcomingEventCard()
        {
            try
            {
                var upcoming = _eventStore.LoadAll()
                    .Where(x => x.EventDate.Date >= DateTime.Today)
                    .OrderBy(x => x.EventDate)
                    .ThenBy(x => x.TimeDisplay)
                    .FirstOrDefault();

                if (upcoming == null)
                {
                    if (UpcomingEventTitleText != null) UpcomingEventTitleText.Text = "No upcoming events";
                    if (UpcomingEventTypeText != null) UpcomingEventTypeText.Text = "Event";
                    if (UpcomingEventDateTimeText != null) UpcomingEventDateTimeText.Text = "--";
                    if (UpcomingEventMetaText != null) UpcomingEventMetaText.Text = "Location: --   •   Host: --";
                    if (UpcomingEventAttendeeText != null) UpcomingEventAttendeeText.Text = "Attending: 0";
                    if (UpcomingEventNotesText != null) UpcomingEventNotesText.Text = "No event notes.";
                    return;
                }

                if (UpcomingEventTitleText != null)
                    UpcomingEventTitleText.Text = string.IsNullOrWhiteSpace(upcoming.Title) ? "Untitled Event" : upcoming.Title;

                if (UpcomingEventTypeText != null)
                    UpcomingEventTypeText.Text = string.IsNullOrWhiteSpace(upcoming.EventType) ? "Event" : upcoming.EventType;

                if (UpcomingEventDateTimeText != null)
                    UpcomingEventDateTimeText.Text = $"{upcoming.EventDate:MMMM d, yyyy} • {SafeEventValue(upcoming.TimeDisplay)}";

                if (UpcomingEventMetaText != null)
                    UpcomingEventMetaText.Text = $"Location: {SafeEventValue(upcoming.Location)}   •   Host: {SafeEventValue(upcoming.Host)}";

                if (UpcomingEventAttendeeText != null)
                    UpcomingEventAttendeeText.Text = $"Attending: {upcoming.AttendeeCount}";

                if (UpcomingEventNotesText != null)
                    UpcomingEventNotesText.Text = string.IsNullOrWhiteSpace(upcoming.Notes) ? "No event notes." : upcoming.Notes;
            }
            catch
            {
                if (UpcomingEventTitleText != null) UpcomingEventTitleText.Text = "No upcoming events";
                if (UpcomingEventTypeText != null) UpcomingEventTypeText.Text = "Event";
                if (UpcomingEventDateTimeText != null) UpcomingEventDateTimeText.Text = "--";
                if (UpcomingEventMetaText != null) UpcomingEventMetaText.Text = "Location: --   •   Host: --";
                if (UpcomingEventAttendeeText != null) UpcomingEventAttendeeText.Text = "Attending: 0";
                if (UpcomingEventNotesText != null) UpcomingEventNotesText.Text = "No event notes.";
            }
        }

        private void UpdateAdminButtonVisibility(List<VtcRosterViewModel.RosterDriverRow> rows)
        {
            try
            {
                if (AdminPanelButton == null)
                    return;

                string myDiscordId = "";

                try
                {
                    var ident = DiscordIdentityStore.Load();
                    myDiscordId = (ident?.DiscordUserId ?? "").Trim();
                }
                catch
                {
                    myDiscordId = "";
                }

                if (string.IsNullOrWhiteSpace(myDiscordId))
                {
                    AdminPanelButton.Visibility = Visibility.Collapsed;
                    AdminPanelButton.Tag = null;
                    UpdateBrandingButtonsVisibility(false);
                    return;
                }

                var me = rows.FirstOrDefault(r =>
                    string.Equals((r.DiscordUserId ?? "").Trim(), myDiscordId, StringComparison.OrdinalIgnoreCase));

                if (me == null)
                {
                    AdminPanelButton.Visibility = Visibility.Collapsed;
                    AdminPanelButton.Tag = null;
                    UpdateBrandingButtonsVisibility(false);
                    return;
                }

                var role = (me.Role ?? "").Trim();

                var isAdmin =
                    role.Equals("Owner", StringComparison.OrdinalIgnoreCase) ||
                    role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                    role.Equals("Manager", StringComparison.OrdinalIgnoreCase);

                AdminPanelButton.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
                AdminPanelButton.Tag = string.IsNullOrWhiteSpace(role) ? "Admin Access" : role;
                UpdateBrandingButtonsVisibility(isAdmin);
            }
            catch
            {
                if (AdminPanelButton != null)
                {
                    AdminPanelButton.Visibility = Visibility.Collapsed;
                    AdminPanelButton.Tag = null;
                }

                UpdateBrandingButtonsVisibility(false);
            }
        }

        private void OpenAdminPanel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var role = (AdminPanelButton?.Tag as string ?? "Admin Access").Trim();
                var win = new VtcAdminWindow(_rosterVm, role)
                {
                    Owner = Window.GetWindow(this)
                };

                win.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to open admin panel: " + ex.Message,
                    "VTC Admin",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OpenConvoyPage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new ConvoyPageWindow
                {
                    Owner = Window.GetWindow(this)
                };

                win.ShowDialog();
                UpdateConvoyCard();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to open Convoy Page: " + ex.Message,
                    "Convoy",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void CreateConvoy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new CreateConvoyWindow
                {
                    Owner = Window.GetWindow(this)
                };

                win.ShowDialog();
                UpdateConvoyCard();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to open Create Convoy window: " + ex.Message,
                    "Create Convoy",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ViewConvoyAttendees_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new ConvoyAttendeesWindow
                {
                    Owner = Window.GetWindow(this)
                };

                win.ShowDialog();
                UpdateConvoyCard();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to open Convoy Attendees window: " + ex.Message,
                    "Convoy Attendees",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OpenEventsPage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new EventPageWindow
                {
                    Owner = Window.GetWindow(this)
                };

                win.ShowDialog();
                UpdateUpcomingEventCard();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to open Events Page: " + ex.Message,
                    "Events",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void CreateEventQuick_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new CreateEventWindow
                {
                    Owner = Window.GetWindow(this)
                };

                win.ShowDialog();
                UpdateUpcomingEventCard();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to open Create Event: " + ex.Message,
                    "Events",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void PostUpcomingEventQuick_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selected = _eventStore.LoadAll()
                    .Where(x => x.EventDate.Date >= DateTime.Today)
                    .OrderBy(x => x.EventDate)
                    .ThenBy(x => x.TimeDisplay)
                    .FirstOrDefault();

                if (selected == null)
                {
                    MessageBox.Show(
                        "There is no upcoming event to post.",
                        "Post Event",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var cfg = VtcConfigService.Load(forceReload: true);
                var baseUrl = (cfg.BotApiBaseUrl ?? "").Trim().TrimEnd('/');
                var guildId = (cfg.Discord?.GuildId ?? "").Trim();

                if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(guildId))
                {
                    MessageBox.Show(
                        "BotApiBaseUrl or GuildId is missing from VTC config.",
                        "Post Event",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var text =
                    $"📅 **{selected.Title}**\n" +
                    $"Type: {SafeEventValue(selected.EventType)}\n" +
                    $"Date: {selected.EventDate:MMMM d, yyyy}\n" +
                    $"Time: {SafeEventValue(selected.TimeDisplay)}\n" +
                    $"Location: {SafeEventValue(selected.Location)}\n" +
                    $"Host: {SafeEventValue(selected.Host)}\n" +
                    $"Attending: {selected.AttendeeCount}\n" +
                    $"{(string.IsNullOrWhiteSpace(selected.Notes) ? "" : "\nNotes: " + selected.Notes)}";

                var payload = new
                {
                    GuildId = guildId,
                    Text = text,
                    Author = "OverWatch ELD Events"
                };

                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                var json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");

                var resp = await http.PostAsync($"{baseUrl}/api/vtc/announcements/post", content);
                var body = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                    MessageBox.Show(
                        $"Post failed.\n\nStatus: {(int)resp.StatusCode}\n\n{body}",
                        "Post Event",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                MessageBox.Show(
                    "✅ Upcoming event posted to Discord announcements.",
                    "Post Event",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to post event: " + ex.Message,
                    "Post Event",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ChangeBanner_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var path = PickImageFile("Select Banner Image");
                if (string.IsNullOrWhiteSpace(path))
                    return;

                var branding = LoadBrandingConfig();
                var target = SaveBrandingImage(path, "banner");
                branding.BannerImagePath = target;
                SaveBrandingConfig(branding);
                LoadBranding();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to change banner: " + ex.Message,
                    "VTC Branding",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void RemoveBanner_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var branding = LoadBrandingConfig();
                TryDeleteFile(branding.BannerImagePath);
                branding.BannerImagePath = "";
                SaveBrandingConfig(branding);
                LoadBranding();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to remove banner: " + ex.Message,
                    "VTC Branding",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ChangeIcon_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var path = PickImageFile("Select VTC Icon");
                if (string.IsNullOrWhiteSpace(path))
                    return;

                var branding = LoadBrandingConfig();
                var target = SaveBrandingImage(path, "icon");
                branding.IconImagePath = target;
                SaveBrandingConfig(branding);
                LoadBranding();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to change icon: " + ex.Message,
                    "VTC Branding",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void RemoveIcon_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var branding = LoadBrandingConfig();
                TryDeleteFile(branding.IconImagePath);
                branding.IconImagePath = "";
                SaveBrandingConfig(branding);
                LoadBranding();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to remove icon: " + ex.Message,
                    "VTC Branding",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void LoadBranding()
        {
            try
            {
                var branding = LoadBrandingConfig();

                ApplyImage(BannerImage, BannerFallback, branding.BannerImagePath);
                ApplyImage(VtcIconImage, IconFallback, branding.IconImagePath);
            }
            catch
            {
                ApplyImage(BannerImage, BannerFallback, "");
                ApplyImage(VtcIconImage, IconFallback, "");
            }
        }

        private void UpdateBrandingButtonsVisibility(bool canEdit)
        {
            if (ChangeBannerButton != null) ChangeBannerButton.Visibility = Visibility.Collapsed;
            if (RemoveBannerButton != null) RemoveBannerButton.Visibility = Visibility.Collapsed;
            if (ChangeIconButton != null) ChangeIconButton.Visibility = Visibility.Collapsed;
            if (RemoveIconButton != null) RemoveIconButton.Visibility = Visibility.Collapsed;
        }

        private string GetBrandingRootDirectory()
        {
            var cfg = VtcConfigService.Load(forceReload: true);
            var guildId = (cfg.Discord?.GuildId ?? cfg.GuildId ?? "").Trim();

            if (string.IsNullOrWhiteSpace(guildId))
                guildId = "default";

            var dir = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Data",
                "VtcBranding",
                guildId);

            Directory.CreateDirectory(dir);
            return dir;
        }

        private string GetBrandingConfigPath()
        {
            return Path.Combine(GetBrandingRootDirectory(), "branding.json");
        }

        private VtcBrandingConfig LoadBrandingConfig()
        {
            try
            {
                var path = GetBrandingConfigPath();
                if (!File.Exists(path))
                    return new VtcBrandingConfig();

                var json = File.ReadAllText(path);
                var data = JsonSerializer.Deserialize<VtcBrandingConfig>(json, JsonReadOpts);
                return data ?? new VtcBrandingConfig();
            }
            catch
            {
                return new VtcBrandingConfig();
            }
        }

        private void SaveBrandingConfig(VtcBrandingConfig config)
        {
            var path = GetBrandingConfigPath();
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }

        private string SaveBrandingImage(string sourcePath, string baseName)
        {
            var ext = Path.GetExtension(sourcePath);
            if (string.IsNullOrWhiteSpace(ext))
                ext = ".png";

            var target = Path.Combine(GetBrandingRootDirectory(), baseName + ext.ToLowerInvariant());
            File.Copy(sourcePath, target, true);
            return target;
        }

        private static string PickImageFile(string title)
        {
            var dlg = new OpenFileDialog
            {
                Title = title,
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp",
                Multiselect = false
            };

            return dlg.ShowDialog() == true ? dlg.FileName : "";
        }

        private static void TryDeleteFile(string? path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        private static void ApplyImage(Image? image, Border? fallback, string? path)
        {
            try
            {
                if (image == null || fallback == null)
                    return;

                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    image.Source = null;
                    image.Visibility = Visibility.Collapsed;
                    fallback.Visibility = Visibility.Visible;
                    return;
                }

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();

                image.Source = bmp;
                image.Visibility = Visibility.Visible;
                fallback.Visibility = Visibility.Collapsed;
            }
            catch
            {
                if (image != null)
                {
                    image.Source = null;
                    image.Visibility = Visibility.Collapsed;
                }

                if (fallback != null)
                    fallback.Visibility = Visibility.Visible;
            }
        }

        private void SetDashboardEmpty()
        {
            SafeSetText(DriversCountText, "0");
            SafeSetText(OnlineDriversText, "0");
            SafeSetText(FleetCountText, "0");
            SafeSetText(WeeklyMilesText, "0");
            SafeSetText(MonthlyMilesText, "0");

            SafeBindListBox(TopDriversList, new[]
            {
                new VtcLeaderboardItem
                {
                    Name = "No leaderboard data yet.",
                    MilesDisplay = "",
                    StatusBrush = Brushes.Gray
                }
            });

            SafeBindListBox(ActivityFeedList, new[]
            {
                new VtcActivityItem
                {
                    Message = "No recent activity yet.",
                    TimeDisplay = "",
                    StatusBrush = Brushes.Gray
                }
            });

            SafeBindListBox(FleetSnapshotList, new[]
            {
                new FleetSnapshotItem
                {
                    TruckId = "",
                    Truck = "No fleet trucks yet.",
                    Driver = "",
                    IsCurrentTruck = false,
                    ActionText = "",
                    Location = "",
                    Status = "",
                    ServiceDue = "",
                    InspectionStatus = "",
                    Health = "",
                    StatusBrush = Brushes.Gray,
                    HealthBrush = Brushes.Gray
                }
            });

            if (UpcomingEventTitleText != null) UpcomingEventTitleText.Text = "No upcoming events";
            if (UpcomingEventTypeText != null) UpcomingEventTypeText.Text = "Event";
            if (UpcomingEventDateTimeText != null) UpcomingEventDateTimeText.Text = "--";
            if (UpcomingEventMetaText != null) UpcomingEventMetaText.Text = "Location: --   •   Host: --";
            if (UpcomingEventAttendeeText != null) UpcomingEventAttendeeText.Text = "Attending: 0";
            if (UpcomingEventNotesText != null) UpcomingEventNotesText.Text = "No event notes.";
        }

        private static string BuildActivityMessage(VtcRosterViewModel.RosterDriverRow row)
        {
            var name = SafeDriver(row.Driver);
            var status = string.IsNullOrWhiteSpace(row.Status) ? "-" : row.Status.Trim();
            var role = string.IsNullOrWhiteSpace(row.Role) ? "-" : row.Role.Trim();
            var truck = string.IsNullOrWhiteSpace(row.Truck) ? "-" : row.Truck.Trim();
            var location = string.IsNullOrWhiteSpace(row.Location) ? "-" : row.Location.Trim();

            var parts = new List<string>
            {
                $"{name} — {status}"
            };

            if (role != "-")
                parts.Add($"Role: {role}");

            if (truck != "-")
                parts.Add($"Truck: {truck}");

            if (location != "-")
                parts.Add(location);

            return string.Join(" • ", parts);
        }

        private static void SafeBindListBox(ListBox? listBox, IEnumerable items)
        {
            if (listBox == null) return;

            try
            {
                listBox.ItemsSource = null;
                listBox.Items.Clear();
                listBox.ItemsSource = items;
            }
            catch { }
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

        private static string SafeDriver(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "Unknown Driver" : value.Trim();
        }

        private static void SafeSetText(TextBlock? textBlock, string value)
        {
            try
            {
                if (textBlock != null)
                    textBlock.Text = value;
            }
            catch { }
        }

        private static string SafeEventValue(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "--" : value.Trim();
        }

        private static bool StatusLooksOnline(string? status)
        {
            if (string.IsNullOrWhiteSpace(status)) return false;

            var s = status.Trim();

            return s.Equals("online", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("active", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("driving", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("on duty", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("onduty", StringComparison.OrdinalIgnoreCase);
        }

        private static Brush GetStatusBrush(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return Brushes.Gray;

            var s = status.Trim();

            if (s.Equals("online", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("active", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("driving", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("on duty", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("onduty", StringComparison.OrdinalIgnoreCase))
                return Brushes.LimeGreen;

            if (s.Equals("idle", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("parked", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("break", StringComparison.OrdinalIgnoreCase))
                return Brushes.Goldenrod;

            return Brushes.IndianRed;
        }

        private async Task RefreshAnnouncementsAsync()
        {
            try
            {
                var cfg = VtcConfigService.Load();
                var baseUrl = (cfg.BotApiBaseUrl ?? "").Trim().TrimEnd('/');
                var gid = (cfg.Discord?.GuildId ?? "").Trim();

                if (AnnouncementsList == null) return;

                if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(gid))
                {
                    AnnouncementsList.ItemsSource = new[]
                    {
                        new AnnouncementItem
                        {
                            Author = "Not Connected",
                            Message = "Pair or select a Discord server to view announcements.",
                            TimeDisplay = "",
                            SourceLabel = "Setup Required",
                            StatusBrush = Brushes.Gray
                        }
                    };
                    SetAnnouncementUnread(false);
                    ApplySetupVisibility(cfg);
                    return;
                }

                var url = baseUrl + "/api/vtc/announcements?guildId=" + Uri.EscapeDataString(gid);
                var raw = (await Http.GetStringAsync(url))?.Trim() ?? "";

                var items = new List<AnnouncementItem>();
                DateTimeOffset? newestUtc = null;

                using (var doc = JsonDocument.Parse(raw))
                {
                    var root = doc.RootElement;

                    JsonElement arr;
                    if (!(root.TryGetProperty("items", out arr) ||
                          root.TryGetProperty("announcements", out arr) ||
                          root.TryGetProperty("data", out arr)))
                    {
                        if (root.ValueKind == JsonValueKind.Array) arr = root;
                        else arr = default;
                    }

                    if (arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in arr.EnumerateArray().Take(25))
                        {
                            var txt = el.TryGetProperty("text", out var t) ? (t.GetString() ?? "") : "";
                            if (string.IsNullOrWhiteSpace(txt))
                                txt = el.TryGetProperty("message", out var t2) ? (t2.GetString() ?? "") : "";

                            var author = el.TryGetProperty("author", out var a) ? (a.GetString() ?? "") : "";
                            var created = el.TryGetProperty("createdUtc", out var c) ? (c.GetString() ?? "") : "";

                            DateTimeOffset? ts = null;
                            if (DateTimeOffset.TryParse(created, out var parsed))
                            {
                                ts = parsed;
                                newestUtc = newestUtc == null ? parsed : (parsed > newestUtc ? parsed : newestUtc);
                            }

                            items.Add(new AnnouncementItem
                            {
                                Author = string.IsNullOrWhiteSpace(author) ? "Discord Update" : author.Trim(),
                                Message = string.IsNullOrWhiteSpace(txt) ? "No message content." : txt.Trim(),
                                TimeDisplay = ts?.LocalDateTime.ToString("g") ?? "",
                                SourceLabel = "Discord Announcement",
                                StatusBrush = Brushes.Goldenrod
                            });
                        }
                    }
                }

                if (items.Count == 0)
                {
                    items.Add(new AnnouncementItem
                    {
                        Author = "Announcements",
                        Message = "No announcements yet.",
                        TimeDisplay = "",
                        SourceLabel = "Discord",
                        StatusBrush = Brushes.Gray
                    });
                }

                AnnouncementsList.ItemsSource = items;

                var lastSeen = ParseUtc(VtcConfigService.Load().AnnouncementsLastSeenUtc);
                var compare = _lastAnnouncementsSeenUtc ?? lastSeen;

                var hasUnread = newestUtc != null && (compare == null || newestUtc > compare);
                SetAnnouncementUnread(hasUnread);
                ApplySetupVisibility(cfg);
            }
            catch
            {
                if (AnnouncementsList != null)
                {
                    AnnouncementsList.ItemsSource = new[]
                    {
                        new AnnouncementItem
                        {
                            Author = "Announcements",
                            Message = "Announcements unavailable.",
                            TimeDisplay = "",
                            SourceLabel = "Discord",
                            StatusBrush = Brushes.Gray
                        }
                    };
                }

                SetAnnouncementUnread(false);
            }
        }

        private static DateTimeOffset? ParseUtc(string? iso)
        {
            if (string.IsNullOrWhiteSpace(iso)) return null;
            if (DateTimeOffset.TryParse(iso, out var dt)) return dt;
            return null;
        }

        private void SetAnnouncementUnread(bool unread)
        {
            try
            {
                if (AnnouncementsDot != null)
                    AnnouncementsDot.Visibility = unread ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { }
        }

        private async void RefreshAnnouncements_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAnnouncementsAsync();
            await RefreshPhase1DashboardAsync();
        }

        private void AnnouncementsMarkRead_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cfg = VtcConfigService.Load(forceReload: true);
                cfg.AnnouncementsLastSeenUtc = DateTimeOffset.UtcNow.ToString("O");
                VtcConfigService.Save(cfg);
                _lastAnnouncementsSeenUtc = DateTimeOffset.UtcNow;
                SetAnnouncementUnread(false);
            }
            catch { }
        }

        private void OpenAchievements_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new OverWatchELD.Views.Achievements.AchievementBoardWindow
                {
                    Owner = Window.GetWindow(this)
                };

                win.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Achievement Board failed to open: " + ex.Message,
                    "Achievements",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }
}