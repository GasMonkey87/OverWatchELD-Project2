using OverWatchELD.Services;
using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace OverWatchELD.Views
{
    public partial class LoginWindow : Window
    {
        private const string BotUrl = "https://overwatcheld.up.railway.app";

        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        public LoginWindow()
        {
            InitializeComponent();

            DriverNameBox.Text = GetSavedDriverName();
            SaveBotUrl();
            LoadVersion();
            LoadLinkedState();

            _ = RefreshLinkedRoleFromBotAsync();
            _ = CheckTelemetry();
            _ = CheckBotStatus();
        }

        private void LoadVersion()
        {
            try
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                VersionText.Text = $"Version {v}";
            }
            catch
            {
                VersionText.Text = "Version";
            }
        }

        private void LoadLinkedState()
        {
            try
            {
                var cfg = VtcConfigService.Load();
                var guildId = cfg.Discord?.GuildId ?? cfg.GuildId ?? "";

                if (cfg.Enabled &&
                    !string.IsNullOrWhiteSpace(cfg.VtcName) &&
                    !string.IsNullOrWhiteSpace(guildId))
                {
                    LinkSection.Visibility = Visibility.Collapsed;
                    LinkedSection.Visibility = Visibility.Visible;

                    LinkedNameText.Text = $"Linked VTC: {cfg.VtcName}";
                    LinkedRoleText.Text = "Role: Checking VTC permissions...";
                }
                else
                {
                    ShowUnlinkedUi();
                }
            }
            catch
            {
                ShowUnlinkedUi();
            }
        }

        private async Task RefreshLinkedRoleFromBotAsync()
        {
            try
            {
                var cfg = VtcConfigService.Load();
                var guildId = cfg.Discord?.GuildId ?? cfg.GuildId ?? "";

                if (!cfg.Enabled ||
                    string.IsNullOrWhiteSpace(cfg.VtcName) ||
                    string.IsNullOrWhiteSpace(guildId))
                {
                    return;
                }

                LinkedRoleText.Text = "Role: Checking VTC permissions...";

                var identity = new DiscordIdentityService().LoadOrDefault();
                var discordUserId = identity?.DiscordUserId ?? "";

                if (string.IsNullOrWhiteSpace(discordUserId))
                {
                    LinkedRoleText.Text = "Role: Linked, not verified";
                    return;
                }

                var url =
                    $"{BotUrl}/api/vtc/member/role" +
                    $"?guildId={Uri.EscapeDataString(guildId)}" +
                    $"&discordUserId={Uri.EscapeDataString(discordUserId)}";

                using var resp = await _http.GetAsync(url);

                if (!resp.IsSuccessStatusCode)
                {
                    LinkedRoleText.Text = "Role: Linked, not verified";
                    SetSessionRole("Linked, not verified");
                    return;
                }

                var json = await resp.Content.ReadAsStringAsync();

                var result = JsonSerializer.Deserialize<RoleLookupResult>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result == null || !result.Ok)
                {
                    LinkedRoleText.Text = "Role: Linked, not verified";
                    SetSessionRole("Linked, not verified");
                    return;
                }

                var role = NormalizeRole(result.LinkedUserRole ?? result.Role ?? "Driver");

                LinkedRoleText.Text = $"Role: {role}";

                SaveDiscordIdentity(new PairResult
                {
                    Ok = true,
                    GuildId = guildId,
                    VtcName = cfg.VtcName ?? "",
                    DiscordUserId = discordUserId,
                    DiscordUsername = identity?.DiscordUsername ?? "",
                    LinkedUserRole = role,
                    Role = role
                }, role);

                SetSessionRole(role);
            }
            catch
            {
                LinkedRoleText.Text = "Role: Linked, not verified";
                SetSessionRole("Linked, not verified");
            }
        }

        private void ShowUnlinkedUi()
        {
            LinkSection.Visibility = Visibility.Visible;
            LinkedSection.Visibility = Visibility.Collapsed;
            LinkedNameText.Text = "Linked: -";
            LinkedRoleText.Text = "Role: Not linked";
        }

        private void Unlink_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MessageBox.Show(
                    "Unlink this ELD from the current VTC Discord?\n\nThis will fully reset the app.",
                    "Unlink Discord",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;

                VtcHardResetService.HardUnlink();

                MessageBox.Show("ELD has been fully unlinked.\n\nRestarting...",
                    "OverWatch ELD",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                System.Diagnostics.Process.Start(Environment.ProcessPath!);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unlink failed: " + ex.Message);
            }
        }

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            await TryLogin();
        }

        private async Task TryLogin()
        {
            try
            {
                LoginButton.IsEnabled = false;
                LoginButton.Content = "Connecting...";

                var code = (LinkCodeBox.Text ?? "").Trim();

                if (!FirstRunSetupService.IsComplete())
                {
                    var setup = new FirstTimeSetupWindow();
                    Application.Current.MainWindow = setup;
                    setup.Show();
                    Close();
                    return;
                }

                if (string.IsNullOrWhiteSpace(code))
                {
                    var mode = FirstRunSetupService.GetMode();

                    if (mode.Equals("vtc", StringComparison.OrdinalIgnoreCase))
                    {
                        var cfgExisting = VtcConfigService.Load();
                        var guildId = cfgExisting.Discord?.GuildId ?? cfgExisting.GuildId ?? "";

                        if (cfgExisting.Enabled &&
                            !string.IsNullOrWhiteSpace(cfgExisting.VtcName) &&
                            !string.IsNullOrWhiteSpace(guildId))
                        {
                            SetVtcSessionFromConfig(cfgExisting);
                            OpenDashboardAndClose();
                            return;
                        }

                        MessageBox.Show("Enter your Discord !link code first.");
                        return;
                    }

                    VtcHardResetService.SetStandaloneMode();
                    SetStandaloneSession();

                    OpenDashboardAndClose();
                    return;
                }

                var resp = await _http.GetAsync(
                    $"{BotUrl}/api/vtc/pair/claim?code={Uri.EscapeDataString(code)}");

                if (!resp.IsSuccessStatusCode)
                {
                    MessageBox.Show("Invalid link code.");
                    return;
                }

                var json = await resp.Content.ReadAsStringAsync();

                var result = JsonSerializer.Deserialize<PairResult>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result == null || !result.Ok)
                {
                    MessageBox.Show("Pairing failed.");
                    return;
                }

                var role = NormalizeRole(result.LinkedUserRole ?? result.Role ?? "Driver");

                var cfgLinked = VtcConfigService.Load(true);

                cfgLinked.Enabled = true;
                cfgLinked.PairCode = code;
                cfgLinked.VtcName = result.VtcName ?? "";
                cfgLinked.GuildId = result.GuildId ?? "";
                cfgLinked.BotApiBaseUrl = BotUrl;

                if (cfgLinked.Discord != null)
                    cfgLinked.Discord.GuildId = result.GuildId ?? "";

                VtcConfigService.Save(cfgLinked);

                SaveDiscordIdentity(result, role);
                SetVtcSession(result, role);

                FirstRunSetupService.MarkVtc();

                MessageBox.Show($"Connected to {result.VtcName}");

                OpenDashboardAndClose();
            }
            finally
            {
                LoginButton.IsEnabled = true;
                LoginButton.Content = "Login";
            }
        }

        private static void SetStandaloneSession()
        {
            if (Application.Current is App app)
            {
                app.EnsureSession();
                app.Session.VtcProvider = "Standalone";
                app.Session.VtcName = "";
                app.Session.GuildId = "";
                app.Session.DriverRole = "Driver";
                app.Session.LinkedUserRole = "Driver";
            }
        }

        private static void SetVtcSessionFromConfig(dynamic cfg)
        {
            if (Application.Current is App app)
            {
                app.EnsureSession();
                app.Session.VtcProvider = "Discord";
                app.Session.VtcName = cfg.VtcName ?? "";
                app.Session.GuildId = cfg.GuildId ?? "";

                var role = TryGetSavedIdentityRole();
                app.Session.DriverRole = role;
                app.Session.LinkedUserRole = role;
            }
        }

        private static void SetVtcSession(PairResult result, string role)
        {
            if (Application.Current is App app)
            {
                app.EnsureSession();
                app.Session.VtcProvider = "Discord";
                app.Session.VtcName = result.VtcName ?? "";
                app.Session.GuildId = result.GuildId ?? "";
                app.Session.DriverRole = role;
                app.Session.LinkedUserRole = role;
            }
        }

        private static void SetSessionRole(string role)
        {
            if (Application.Current is App app)
            {
                app.EnsureSession();
                app.Session.DriverRole = role;
                app.Session.LinkedUserRole = role;
            }
        }

        private static void SaveDiscordIdentity(PairResult result, string role)
        {
            new DiscordIdentityService().SaveOrUpdate(
                new DiscordIdentityService.DiscordIdentity
                {
                    DiscordUserId = result.DiscordUserId ?? "",
                    DiscordUsername = result.DiscordUsername ?? "",
                    GuildId = result.GuildId ?? "",
                    VtcName = result.VtcName ?? "",
                    Role = role
                });
        }

        private static string NormalizeRole(string? role)
        {
            if (string.IsNullOrWhiteSpace(role)) return "Driver";

            role = role.ToLowerInvariant();

            if (role.Contains("owner")) return "Owner";
            if (role.Contains("admin")) return "Admin";
            if (role.Contains("manager")) return "Manager";
            if (role.Contains("moderator")) return "Manager";

            return "Driver";
        }

        private static string TryGetSavedIdentityRole()
        {
            try
            {
                var identity = new DiscordIdentityService().LoadOrDefault();
                var role = NormalizeRole(identity?.Role);

                return string.IsNullOrWhiteSpace(role) ? "Linked, not verified" : role;
            }
            catch
            {
                return "Linked, not verified";
            }
        }

        private static string GetSavedDriverName()
        {
            try { return new DiscordIdentityService().LoadOrDefault()?.DiscordUsername ?? ""; }
            catch { return ""; }
        }

        private static void SaveBotUrl()
        {
            try
            {
                var cfg = VtcConfigService.Load();
                cfg.BotApiBaseUrl = BotUrl;
                VtcConfigService.Save(cfg);
            }
            catch { }
        }

        private void OpenDashboardAndClose()
        {
            var main = new MainWindow();
            Application.Current.MainWindow = main;
            main.Show();
            if (Application.Current is App app)
                app.StartDashboardBackgroundServices();
            Close();
        }

        private async Task CheckBotStatus()
        {
            try
            {
                var resp = await _http.GetAsync($"{BotUrl}/api/status");
                StatusText.Text = resp.IsSuccessStatusCode ? "Connected" : "Warning";
            }
            catch
            {
                StatusText.Text = "Offline";
            }
        }

        private async Task CheckTelemetry()
        {
            try
            {
                var resp = await _http.GetAsync("http://127.0.0.1:25555/api/ats/telemetry");
                if (resp.IsSuccessStatusCode)
                    StatusText.Text = "Telemetry Connected";
            }
            catch { }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private class PairResult
        {
            public bool Ok { get; set; }
            public string GuildId { get; set; } = "";
            public string VtcName { get; set; } = "";
            public string DiscordUserId { get; set; } = "";
            public string DiscordUsername { get; set; } = "";
            public string? LinkedUserRole { get; set; }
            public string? Role { get; set; }
        }

        private class RoleLookupResult
        {
            public bool Ok { get; set; }
            public string? Role { get; set; }
            public string? LinkedUserRole { get; set; }
        }
    }
}