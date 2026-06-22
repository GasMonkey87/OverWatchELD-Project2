using Microsoft.Win32;
using OverWatchELD.Models;
using OverWatchELD.Services;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace OverWatchELD.Views
{
    public partial class SettingsWindow : Window
    {
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        private static readonly JsonSerializerOptions _json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private sealed class LocalSettings
        {
            public string BotApiBaseUrl { get; set; } = "";

            public string DispatchChannelId { get; set; } = "";
            public string LogsChannelId { get; set; } = "";
            public string InspectionsChannelId { get; set; } = "";
            public string AnnouncementsChannelId { get; set; } = "";

            public string LoadboardChannelId { get; set; } = "";
            public bool UseLoadThreads { get; set; } = true;
            public bool AutoArchiveCompletedLoads { get; set; } = true;

            public bool DriverRandomMalfunctionsEnabled { get; set; } = true;
            public bool AdminEnableRandomMalfunctions { get; set; }
            public bool AdminAllowDriverMalfunctionOverride { get; set; }
            public double RandomMalfunctionChancePercent { get; set; } = 3;
            public int RandomMalfunctionIntervalMinutes { get; set; } = 15;
            public int RandomMalfunctionCooldownMinutes { get; set; } = 30;
            public bool RandomMalfunctionsOnlyWhileDriving { get; set; } = true;
            public double RandomMalfunctionMinSpeedMph { get; set; } = 5;
        }

        private sealed class LoadboardSettingsDto
        {
            public string LoadboardChannelId { get; set; } = "";
            public bool UseLoadThreads { get; set; } = true;
            public bool AutoArchiveCompletedLoads { get; set; } = true;

            public bool DriverRandomMalfunctionsEnabled { get; set; } = true;
            public bool AdminEnableRandomMalfunctions { get; set; }
            public bool AdminAllowDriverMalfunctionOverride { get; set; }
            public double RandomMalfunctionChancePercent { get; set; } = 3;
            public int RandomMalfunctionIntervalMinutes { get; set; } = 15;
            public int RandomMalfunctionCooldownMinutes { get; set; } = 30;
            public bool RandomMalfunctionsOnlyWhileDriving { get; set; } = true;
            public double RandomMalfunctionMinSpeedMph { get; set; } = 5;
        }

        private sealed class VtcBrandingConfig
        {
            public string BannerImagePath { get; set; } = "";
            public string IconImagePath { get; set; } = "";
        }

        private static string LocalSettingsPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "settings_window.discord.json");

        private LocalSettings _settings = new LocalSettings();

        public SettingsWindow()
        {
            InitializeComponent();
            Loaded += async (_, __) => await LoadAllAsync();
        }

        private async Task LoadAllAsync()
        {
            _settings = LoadLocalSettings();
            LoadConfigBackedValues();
            LoadUiFromSettings();
            LoadLinkedDiscordVtcUi();
            LoadRandomMalfunctionUi();
            LoadBrandingPreview();
            await Task.CompletedTask;
        }

        private void LoadConfigBackedValues()
        {
            try
            {
                var cfg = VtcConfigService.LoadOrCreate();
                var discord = cfg.Discord;

                _settings.BotApiBaseUrl = cfg.BotApiBaseUrl ?? "";

                _settings.DispatchChannelId = GetStringProperty(discord, "DispatchChannelId");
                _settings.LogsChannelId = GetStringProperty(discord, "LogsChannelId");
                _settings.InspectionsChannelId = GetStringProperty(discord, "InspectionsChannelId");
                _settings.AnnouncementsChannelId = GetStringProperty(discord, "AnnouncementsChannelId");

                _settings.LoadboardChannelId = GetStringProperty(discord, "LoadboardChannelId");
                _settings.UseLoadThreads = GetBoolProperty(discord, "UseLoadThreads", true);
                _settings.AutoArchiveCompletedLoads = GetBoolProperty(discord, "AutoArchiveCompletedLoads", true);
            }
            catch
            {
            }
        }

        private void LoadUiFromSettings()
        {
            BotApiBaseUrlTextBox.Text = string.IsNullOrWhiteSpace(_settings.BotApiBaseUrl)
                ? "Not configured"
                : _settings.BotApiBaseUrl;
            BotApiBaseUrlTextBox.IsReadOnly = true;
            BotApiBaseUrlTextBox.IsTabStop = false;

            DispatchChannelTextBox.Text = _settings.DispatchChannelId;
            LogsChannelTextBox.Text = _settings.LogsChannelId;
            InspectionsChannelTextBox.Text = _settings.InspectionsChannelId;
            AnnouncementsChannelTextBox.Text = _settings.AnnouncementsChannelId;

            if (FindName("LoadboardChannelTextBox") is TextBox loadboard)
                loadboard.Text = _settings.LoadboardChannelId;

            if (FindName("UseLoadThreadsCheckBox") is CheckBox threads)
                threads.IsChecked = _settings.UseLoadThreads;

            if (FindName("AutoArchiveCompletedLoadsCheckBox") is CheckBox archive)
                archive.IsChecked = _settings.AutoArchiveCompletedLoads;
        }

        private void LoadLinkedDiscordVtcUi()
        {
            try
            {
                var cfg = VtcConfigService.LoadOrCreate();
                var identity = new DiscordIdentityService().LoadOrDefault();

                var guildId = FirstNonBlank(
                    GetStringProperty(identity, "GuildId"),
                    cfg.Discord?.GuildId,
                    cfg.GuildId);

                var vtcName = FirstNonBlank(
                    GetStringProperty(identity, "VtcName"),
                    cfg.VtcName,
                    cfg.VtcShort,
                    guildId);

                var linkedUser = FirstNonBlank(
                    GetStringProperty(identity, "DiscordUsername"),
                    cfg.Linking?.DiscordUsername,
                    GetStringProperty(identity, "DiscordUserId"),
                    cfg.Linking?.DiscordUserId,
                    (Application.Current as App)?.Session?.DriverName,
                    Environment.UserName);

                var connected = !string.IsNullOrWhiteSpace(guildId) &&
                                (!string.IsNullOrWhiteSpace(linkedUser) ||
                                 !string.IsNullOrWhiteSpace(GetStringProperty(identity, "DiscordUserId")) ||
                                 !string.IsNullOrWhiteSpace(cfg.Linking?.DiscordUserId));

                ConnectedStatusText.Text = connected ? "Connected" : "Disconnected";
                ConnectedStatusText.Foreground = connected
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94))
                    : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 113, 113));

                ConnectedVtcText.Text = connected
                    ? FirstNonBlank(vtcName, "Linked VTC")
                    : "Not connected";

                LinkedUserText.Text = connected
                    ? FirstNonBlank(linkedUser, "Linked user")
                    : "Not linked";
            }
            catch
            {
                ConnectedStatusText.Text = "Disconnected";
                ConnectedStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 113, 113));
                ConnectedVtcText.Text = "Not connected";
                LinkedUserText.Text = "Not linked";
            }
        }



        private void RelinkDiscord_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cfg = VtcConfigService.LoadOrCreate();
                var driverName = FirstNonBlank(
                    (Application.Current as App)?.Session?.DriverName,
                    EldDriverIdentityResolver.DriverName(),
                    Environment.UserName,
                    "Driver");

                var codeLength = cfg.Linking?.CodeLength ?? 6;
                var expiresMinutes = cfg.Linking?.ExpiresMinutes ?? 10;
                var code = VtcLinkService.GenerateCode(driverName, codeLength, expiresMinutes);

                try
                {
                    VtcLinkService.ClearLink();
                }
                catch { }

                try
                {
                    var existing = DiscordIdentityStore.Load();
                    DiscordIdentityStore.Save(new DiscordIdentity
                    {
                        GuildId = FirstNonBlank(cfg.Discord?.GuildId, cfg.GuildId, existing.GuildId),
                        DiscordUserId = "",
                        DiscordUsername = "",
                        LastPairCode = code,
                        VtcName = FirstNonBlank(cfg.VtcName, cfg.VtcShort, existing.VtcName)
                    });
                }
                catch { }

                var message = $"Relink code: {code}\nUse this in Discord with the bot link command. This code expires in {Math.Max(1, expiresMinutes)} minutes.";

                if (FindName("RelinkCodeText") is TextBlock relinkText)
                    relinkText.Text = message;

                try
                {
                    Clipboard.SetText(code);
                    message += "\n\nThe code was copied to your clipboard.";
                }
                catch { }

                LoadLinkedDiscordVtcUi();

                MessageBox.Show(message, "Relink Discord", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Discord relink failed.\n\n" + ex.Message, "Relink Discord", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void LoadRandomMalfunctionUi()
        {
            try
            {
                var policy = RandomMalfunctionSettingsStore.LoadPolicy();
                var driver = RandomMalfunctionSettingsStore.LoadDriverPreference();

                _settings.AdminEnableRandomMalfunctions = policy.EnableRandomMalfunctionsForVtc;
                _settings.AdminAllowDriverMalfunctionOverride = policy.AllowDriverOverride;
                _settings.RandomMalfunctionChancePercent = policy.ChancePercent;
                _settings.RandomMalfunctionIntervalMinutes = policy.CheckIntervalMinutes;
                _settings.RandomMalfunctionCooldownMinutes = policy.CooldownMinutes;
                _settings.RandomMalfunctionsOnlyWhileDriving = policy.OnlyWhileDriving;
                _settings.RandomMalfunctionMinSpeedMph = policy.MinSpeedMph;
                _settings.DriverRandomMalfunctionsEnabled = driver.DriverEnabled;

                if (FindName("AdminEnableRandomMalfunctionsCheckBox") is CheckBox adminEnabled)
                    adminEnabled.IsChecked = policy.EnableRandomMalfunctionsForVtc;

                if (FindName("AdminAllowDriverMalfunctionOverrideCheckBox") is CheckBox allowOverride)
                    allowOverride.IsChecked = policy.AllowDriverOverride;

                if (FindName("EnableRandomMalfunctionsCheckBox") is CheckBox driverToggle)
                    driverToggle.IsChecked = driver.DriverEnabled;

                if (FindName("RandomMalfunctionChanceTextBox") is TextBox chance)
                    chance.Text = policy.ChancePercent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

                if (FindName("RandomMalfunctionIntervalTextBox") is TextBox interval)
                    interval.Text = policy.CheckIntervalMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture);

                if (FindName("RandomMalfunctionCooldownTextBox") is TextBox cooldown)
                    cooldown.Text = policy.CooldownMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture);

                if (FindName("RandomMalfunctionsOnlyWhileDrivingCheckBox") is CheckBox drivingOnly)
                    drivingOnly.IsChecked = policy.OnlyWhileDriving;

                if (FindName("RandomMalfunctionMinSpeedTextBox") is TextBox minSpeed)
                    minSpeed.Text = policy.MinSpeedMph.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

                ApplyRandomMalfunctionPolicyUi(policy);
            }
            catch
            {
            }
        }

        private void ApplyRandomMalfunctionPolicyUi(RandomMalfunctionVtcPolicy policy)
        {
            try
            {
                var canManage = CurrentUserCanManageRandomMalfunctions();

                if (FindName("AdminEnableRandomMalfunctionsCheckBox") is CheckBox adminEnabled)
                    adminEnabled.IsEnabled = canManage;

                if (FindName("AdminAllowDriverMalfunctionOverrideCheckBox") is CheckBox allowOverride)
                    allowOverride.IsEnabled = canManage;

                if (FindName("RandomMalfunctionChanceTextBox") is TextBox chance)
                    chance.IsEnabled = canManage;

                if (FindName("RandomMalfunctionIntervalTextBox") is TextBox interval)
                    interval.IsEnabled = canManage;

                if (FindName("RandomMalfunctionCooldownTextBox") is TextBox cooldown)
                    cooldown.IsEnabled = canManage;

                if (FindName("RandomMalfunctionsOnlyWhileDrivingCheckBox") is CheckBox drivingOnly)
                    drivingOnly.IsEnabled = canManage;

                if (FindName("RandomMalfunctionMinSpeedTextBox") is TextBox minSpeed)
                    minSpeed.IsEnabled = canManage;

                var driverCanChoose = policy.EnableRandomMalfunctionsForVtc && policy.AllowDriverOverride;

                if (FindName("EnableRandomMalfunctionsCheckBox") is CheckBox driverToggle)
                {
                    driverToggle.Visibility = policy.EnableRandomMalfunctionsForVtc ? Visibility.Visible : Visibility.Collapsed;
                    driverToggle.IsEnabled = driverCanChoose;
                    if (!driverCanChoose && policy.EnableRandomMalfunctionsForVtc)
                        driverToggle.IsChecked = true;
                }

                if (FindName("DriverRandomMalfunctionHelpText") is TextBlock help)
                {
                    if (!policy.EnableRandomMalfunctionsForVtc)
                        help.Text = "Random malfunctions are disabled by VTC management.";
                    else if (!policy.AllowDriverOverride)
                        help.Text = "Random malfunctions are forced on by VTC management.";
                    else
                        help.Text = "VTC management allows drivers to turn random malfunctions on/off.";
                }

                if (FindName("RandomMalfunctionPolicyStatusText") is TextBlock status)
                {
                    var who = canManage ? "You can edit this VTC policy." : "Only VTC admins/owners can edit this policy.";
                    var mode = !policy.EnableRandomMalfunctionsForVtc
                        ? "Disabled for this VTC."
                        : policy.AllowDriverOverride
                            ? "Enabled; drivers may choose."
                            : "Enabled; forced on for drivers.";

                    status.Text = $"{mode} {who}";
                }
            }
            catch
            {
            }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            var s = ReadUi();

            SaveLocalSettings(s);
            PersistToMainConfig(s);
            SaveRandomMalfunctionSettings(s);
            await PushLoadboardSettingsToBotAsync(s);

            SaveStatus.Text = "Saved";
        }

        private LocalSettings ReadUi()
        {
            return new LocalSettings
            {
                // Bot API Base URL is intentionally not editable from Settings.
                BotApiBaseUrl = _settings.BotApiBaseUrl,

                DispatchChannelId = DispatchChannelTextBox.Text.Trim(),
                LogsChannelId = LogsChannelTextBox.Text.Trim(),
                InspectionsChannelId = InspectionsChannelTextBox.Text.Trim(),
                AnnouncementsChannelId = AnnouncementsChannelTextBox.Text.Trim(),

                LoadboardChannelId = (FindName("LoadboardChannelTextBox") as TextBox)?.Text.Trim() ?? "",
                UseLoadThreads = (FindName("UseLoadThreadsCheckBox") as CheckBox)?.IsChecked == true,
                AutoArchiveCompletedLoads = (FindName("AutoArchiveCompletedLoadsCheckBox") as CheckBox)?.IsChecked == true,

                DriverRandomMalfunctionsEnabled = (FindName("EnableRandomMalfunctionsCheckBox") as CheckBox)?.IsChecked == true,
                AdminEnableRandomMalfunctions = (FindName("AdminEnableRandomMalfunctionsCheckBox") as CheckBox)?.IsChecked == true,
                AdminAllowDriverMalfunctionOverride = (FindName("AdminAllowDriverMalfunctionOverrideCheckBox") as CheckBox)?.IsChecked == true,
                RandomMalfunctionChancePercent = ReadDoubleBox("RandomMalfunctionChanceTextBox", 3, 0, 100),
                RandomMalfunctionIntervalMinutes = ReadIntBox("RandomMalfunctionIntervalTextBox", 15, 1, 1440),
                RandomMalfunctionCooldownMinutes = ReadIntBox("RandomMalfunctionCooldownTextBox", 30, 1, 1440),
                RandomMalfunctionsOnlyWhileDriving = (FindName("RandomMalfunctionsOnlyWhileDrivingCheckBox") as CheckBox)?.IsChecked != false,
                RandomMalfunctionMinSpeedMph = ReadDoubleBox("RandomMalfunctionMinSpeedTextBox", 5, 0, 100)
            };
        }

        private void PersistToMainConfig(LocalSettings s)
        {
            try
            {
                var cfg = VtcConfigService.LoadOrCreate();

                // Bot API Base URL is managed by app/VTC config and is not changed by this Settings window.
                cfg.Discord ??= new VtcConfig.DiscordConfig();

                cfg.Discord.DispatchChannelId = s.DispatchChannelId;
                cfg.Discord.LogsChannelId = s.LogsChannelId;
                cfg.Discord.InspectionsChannelId = s.InspectionsChannelId;
                cfg.Discord.AnnouncementsChannelId = s.AnnouncementsChannelId;

                cfg.Discord.LoadboardChannelId = s.LoadboardChannelId;
                cfg.Discord.UseLoadThreads = s.UseLoadThreads;
                cfg.Discord.AutoArchiveCompletedLoads = s.AutoArchiveCompletedLoads;

                VtcConfigService.Save(cfg);
            }
            catch
            {
            }
        }

        private void SaveRandomMalfunctionSettings(LocalSettings s)
        {
            try
            {
                var existingPolicy = RandomMalfunctionSettingsStore.LoadPolicy();
                var canManage = CurrentUserCanManageRandomMalfunctions();

                if (canManage)
                {
                    existingPolicy.EnableRandomMalfunctionsForVtc = s.AdminEnableRandomMalfunctions;
                    existingPolicy.AllowDriverOverride = s.AdminAllowDriverMalfunctionOverride;
                    existingPolicy.ChancePercent = s.RandomMalfunctionChancePercent;
                    existingPolicy.CheckIntervalMinutes = s.RandomMalfunctionIntervalMinutes;
                    existingPolicy.CooldownMinutes = s.RandomMalfunctionCooldownMinutes;
                    existingPolicy.OnlyWhileDriving = s.RandomMalfunctionsOnlyWhileDriving;
                    existingPolicy.MinSpeedMph = s.RandomMalfunctionMinSpeedMph;
                    existingPolicy.UpdatedUtc = DateTimeOffset.UtcNow;
                    existingPolicy.UpdatedBy = GetCurrentDiscordDisplay();
                    RandomMalfunctionSettingsStore.SavePolicy(existingPolicy);
                }

                if (existingPolicy.EnableRandomMalfunctionsForVtc && existingPolicy.AllowDriverOverride)
                {
                    RandomMalfunctionSettingsStore.SaveDriverPreference(new RandomMalfunctionDriverPreference
                    {
                        DriverKey = GetCurrentDriverKey(),
                        DriverEnabled = s.DriverRandomMalfunctionsEnabled,
                        UpdatedUtc = DateTimeOffset.UtcNow
                    });
                }
                else if (existingPolicy.EnableRandomMalfunctionsForVtc && !existingPolicy.AllowDriverOverride)
                {
                    RandomMalfunctionSettingsStore.SaveDriverPreference(new RandomMalfunctionDriverPreference
                    {
                        DriverKey = GetCurrentDriverKey(),
                        DriverEnabled = true,
                        UpdatedUtc = DateTimeOffset.UtcNow
                    });
                }

                LoadRandomMalfunctionUi();
            }
            catch
            {
            }
        }

        private double ReadDoubleBox(string name, double fallback, double min, double max)
        {
            try
            {
                if (FindName(name) is TextBox box)
                {
                    var text = (box.Text ?? "").Trim();
                    if (double.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var value))
                        return Math.Clamp(value, min, max);
                }
            }
            catch
            {
            }

            return fallback;
        }

        private int ReadIntBox(string name, int fallback, int min, int max)
        {
            try
            {
                if (FindName(name) is TextBox box)
                {
                    var text = (box.Text ?? "").Trim();
                    if (int.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var value))
                        return Math.Clamp(value, min, max);
                }
            }
            catch
            {
            }

            return fallback;
        }

        private static string GetCurrentDriverKey()
        {
            try
            {
                var identity = new DiscordIdentityService().LoadOrDefault();

                var id = GetStringProperty(identity, "DiscordUserId");
                if (!string.IsNullOrWhiteSpace(id))
                    return "discord:" + id.Trim();

                var username = GetStringProperty(identity, "DiscordUsername");
                if (!string.IsNullOrWhiteSpace(username))
                    return "name:" + username.Trim().ToLowerInvariant();
            }
            catch
            {
            }

            try
            {
                var app = Application.Current as App;
                var name = (app?.Session?.DriverName ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(name))
                    return "name:" + name.ToLowerInvariant();
            }
            catch
            {
            }

            return Environment.UserName ?? "local-driver";
        }

        private static string GetCurrentDiscordDisplay()
        {
            try
            {
                var identity = new DiscordIdentityService().LoadOrDefault();
                return FirstNonBlank(
                    GetStringProperty(identity, "DiscordUsername"),
                    GetStringProperty(identity, "DiscordUserId"),
                    Environment.UserName);
            }
            catch
            {
                return Environment.UserName;
            }
        }

        private static bool CurrentUserCanManageRandomMalfunctions()
        {
            try
            {
                var identity = new DiscordIdentityService().LoadOrDefault();

                var roleText = FirstNonBlank(
                    GetStringProperty(identity, "Role"),
                    GetStringProperty(identity, "VtcRole"),
                    GetStringProperty(identity, "PermissionRole"),
                    GetStringProperty(identity, "Roles"),
                    GetStringProperty(identity, "RoleNames"),
                    GetStringProperty(identity, "DiscordRoles"));

                if (RoleCanManage(roleText))
                    return true;
            }
            catch
            {
            }

            // Local fallback: keep Settings usable for the VTC owner/admin on their own build.
            // If a future role store is present, the role check above will enforce it.
            return true;
        }

        private static bool RoleCanManage(string? role)
        {
            if (string.IsNullOrWhiteSpace(role))
                return false;

            var r = role.Trim();

            return r.Equals("Owner", StringComparison.OrdinalIgnoreCase) ||
                   r.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                   r.Equals("Administrator", StringComparison.OrdinalIgnoreCase) ||
                   r.Equals("Manager", StringComparison.OrdinalIgnoreCase) ||
                   r.Contains("owner", StringComparison.OrdinalIgnoreCase) ||
                   r.Contains("admin", StringComparison.OrdinalIgnoreCase) ||
                   r.Contains("manager", StringComparison.OrdinalIgnoreCase) ||
                   r.Contains("management", StringComparison.OrdinalIgnoreCase);
        }

        private static string FirstNonBlank(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }

        private async Task PushLoadboardSettingsToBotAsync(LocalSettings s)
        {
            try
            {
                var baseUrl = (s.BotApiBaseUrl ?? "").Trim().TrimEnd('/');
                if (string.IsNullOrWhiteSpace(baseUrl))
                    return;

                if (string.IsNullOrWhiteSpace(s.LoadboardChannelId))
                    return;

                var dto = new LoadboardSettingsDto
                {
                    LoadboardChannelId = s.LoadboardChannelId,
                    UseLoadThreads = s.UseLoadThreads,
                    AutoArchiveCompletedLoads = s.AutoArchiveCompletedLoads
                };

                var resp = await _http.PostAsJsonAsync($"{baseUrl}/api/vtc/loadboard/settings", dto);
                resp.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                try
                {
                    SaveStatus.Text = "Saved locally, bot sync failed";
                    System.Diagnostics.Debug.WriteLine("PushLoadboardSettingsToBotAsync failed: " + ex.Message);
                }
                catch
                {
                }
            }
        }

        private static LocalSettings LoadLocalSettings()
        {
            try
            {
                if (!File.Exists(LocalSettingsPath))
                    return new LocalSettings();

                return JsonSerializer.Deserialize<LocalSettings>(
                    File.ReadAllText(LocalSettingsPath),
                    _json) ?? new LocalSettings();
            }
            catch
            {
                return new LocalSettings();
            }
        }

        private static void SaveLocalSettings(LocalSettings s)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LocalSettingsPath)!);
                File.WriteAllText(LocalSettingsPath, JsonSerializer.Serialize(s, _json));
            }
            catch
            {
            }
        }

        private static string GetStringProperty(object? obj, string name)
        {
            try
            {
                var p = obj?.GetType().GetProperty(name);
                return p?.GetValue(obj)?.ToString() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static bool GetBoolProperty(object? obj, string name, bool fallback)
        {
            try
            {
                var p = obj?.GetType().GetProperty(name);
                if (p == null) return fallback;

                var v = p.GetValue(obj);
                if (v is bool b) return b;

                if (bool.TryParse(v?.ToString(), out var parsed))
                    return parsed;

                return fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private void UploadBanner_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var path = PickImageFile();
                if (string.IsNullOrWhiteSpace(path))
                    return;

                var saved = CopyBrandingImage(path, "banner");

                var branding = LoadBrandingConfig();
                branding.BannerImagePath = saved;
                SaveBrandingConfig(branding);

                LoadBrandingPreview();
                RefreshMainWindowBranding();

                SaveStatus.Text = "Banner saved. Recommended size: 1600 x 400.";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Banner upload failed:\n\n" + ex.Message,
                    "VTC Branding",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void RemoveBanner_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var branding = LoadBrandingConfig();
                branding.BannerImagePath = "";
                SaveBrandingConfig(branding);

                LoadBrandingPreview();
                RefreshMainWindowBranding();

                SaveStatus.Text = "Banner removed.";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Remove banner failed:\n\n" + ex.Message,
                    "VTC Branding",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void UploadIcon_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var path = PickImageFile();
                if (string.IsNullOrWhiteSpace(path))
                    return;

                var saved = CopyBrandingImage(path, "icon");

                var branding = LoadBrandingConfig();
                branding.IconImagePath = saved;
                SaveBrandingConfig(branding);

                LoadBrandingPreview();
                RefreshMainWindowBranding();

                SaveStatus.Text = "Icon saved. Recommended size: 512 x 512.";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Icon upload failed:\n\n" + ex.Message,
                    "VTC Branding",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void RemoveIcon_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var branding = LoadBrandingConfig();
                branding.IconImagePath = "";
                SaveBrandingConfig(branding);

                LoadBrandingPreview();
                RefreshMainWindowBranding();

                SaveStatus.Text = "Icon removed.";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Remove icon failed:\n\n" + ex.Message,
                    "VTC Branding",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private static string PickImageFile()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select VTC branding image",
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.webp;*.bmp|PNG Files|*.png|JPEG Files|*.jpg;*.jpeg|WebP Files|*.webp|Bitmap Files|*.bmp|All Files|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            return dlg.ShowDialog() == true ? dlg.FileName : "";
        }

        private static string GetBrandingGuildId()
        {
            try
            {
                var cfg = VtcConfigService.LoadOrCreate();

                var gid = (cfg.Discord?.GuildId ?? cfg.GuildId ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(gid))
                    return gid;

                var identity = new DiscordIdentityService().LoadOrDefault();
                gid = (identity?.GuildId ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(gid))
                    return gid;
            }
            catch
            {
            }

            return "default";
        }

        private static string GetBrandingRootDirectory()
        {
            var guildId = GetBrandingGuildId();
            var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "VtcBranding", guildId);
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string GetBrandingConfigPath()
        {
            return Path.Combine(GetBrandingRootDirectory(), "branding.json");
        }

        private static VtcBrandingConfig LoadBrandingConfig()
        {
            try
            {
                var path = GetBrandingConfigPath();

                if (!File.Exists(path))
                    return new VtcBrandingConfig();

                return JsonSerializer.Deserialize<VtcBrandingConfig>(
                    File.ReadAllText(path),
                    _json) ?? new VtcBrandingConfig();
            }
            catch
            {
                return new VtcBrandingConfig();
            }
        }

        private static void SaveBrandingConfig(VtcBrandingConfig branding)
        {
            try
            {
                Directory.CreateDirectory(GetBrandingRootDirectory());
                File.WriteAllText(GetBrandingConfigPath(), JsonSerializer.Serialize(branding, _json));
            }
            catch
            {
            }
        }

        private static string CopyBrandingImage(string sourcePath, string name)
        {
            Directory.CreateDirectory(GetBrandingRootDirectory());

            var ext = Path.GetExtension(sourcePath);
            if (string.IsNullOrWhiteSpace(ext))
                ext = ".png";

            ext = ext.ToLowerInvariant();

            var dest = Path.Combine(GetBrandingRootDirectory(), $"{name}{ext}");
            File.Copy(sourcePath, dest, true);

            return dest;
        }

        private void LoadBrandingPreview()
        {
            try
            {
                var branding = LoadBrandingConfig();

                if (BrandingGuildText != null)
                    BrandingGuildText.Text = $"Guild: {GetBrandingGuildId()}";

                ApplyImagePreview(SettingsBannerImage, SettingsBannerFallback, branding.BannerImagePath);
                ApplyImagePreview(SettingsIconImage, SettingsIconFallback, branding.IconImagePath);
            }
            catch
            {
            }
        }

        private static void ApplyImagePreview(Image image, UIElement fallback, string path)
        {
            try
            {
                var exists = !string.IsNullOrWhiteSpace(path) && File.Exists(path);

                image.Visibility = exists ? Visibility.Visible : Visibility.Collapsed;
                fallback.Visibility = exists ? Visibility.Collapsed : Visibility.Visible;

                if (!exists)
                {
                    image.Source = null;
                    return;
                }

                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();

                image.Source = bmp;
            }
            catch
            {
                image.Source = null;
                image.Visibility = Visibility.Collapsed;
                fallback.Visibility = Visibility.Visible;
            }
        }

        private static void RefreshMainWindowBranding()
        {
            try
            {
                foreach (Window w in Application.Current.Windows)
                {
                    if (w is MainWindow main)
                    {
                        var method = typeof(MainWindow).GetMethod("RefreshVtcBranding");
                        method?.Invoke(main, null);
                        break;
                    }
                }
            }
            catch
            {
            }
        }

        private void OpenGuide_Click(object sender, RoutedEventArgs e)
        {
            HelpGuideService.OpenGuide(this);
        }

        private void TestWebhook_Click(object sender, RoutedEventArgs e) { }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}