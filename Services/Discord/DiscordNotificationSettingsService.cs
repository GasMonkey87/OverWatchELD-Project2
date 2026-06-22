using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services.Discord
{
    public sealed class DiscordNotificationCategorySetting
    {
        public string Category { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string DefaultChannelName { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public string ChannelId { get; set; } = "";
        public string WebhookUrl { get; set; } = "";
        public string FallbackCategory { get; set; } = "System";
    }

    public static class DiscordNotificationSettingsService
    {
        private static readonly object Gate = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private static string StorePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "OverWatchELD",
                "discord_notification_settings.json");

        public static List<DiscordNotificationCategorySetting> DefaultSettings()
        {
            return new List<DiscordNotificationCategorySetting>
            {
                Setting("System", "System / ELD", "eld-notifications", true, ""),
                Setting("Dispatch", "Dispatch", "dispatch", true, "System"),
                Setting("BOL", "Bills of Lading", "bol", true, "Dispatch"),
                Setting("Events", "Events", "events", true, "System"),
                Setting("Convoys", "Convoys", "convoy", true, "Events"),
                Setting("Garages", "Garages", "garages", true, "System"),
                Setting("Fleet", "Fleet", "fleet", true, "System"),
                Setting("Maintenance", "Maintenance", "maintenance", true, "System"),
                Setting("Achievements", "Achievements", "achievements", true, "System"),
                Setting("Endorsements", "Driver Endorsements", "driver-endorsements", true, "System")
            };
        }

        public static List<DiscordNotificationCategorySetting> LoadAll()
        {
            lock (Gate)
            {
                try
                {
                    var defaults = DefaultSettings();

                    if (!File.Exists(StorePath))
                    {
                        SaveAll(defaults);
                        return defaults;
                    }

                    var json = File.ReadAllText(StorePath);
                    var saved = JsonSerializer.Deserialize<List<DiscordNotificationCategorySetting>>(json, JsonOptions) ?? new();

                    var merged = defaults
                        .Select(d =>
                        {
                            var match = saved.FirstOrDefault(s => Same(s.Category, d.Category));
                            if (match == null)
                                return d;

                            d.Enabled = match.Enabled;
                            d.ChannelId = match.ChannelId ?? "";
                            d.WebhookUrl = match.WebhookUrl ?? "";
                            d.DisplayName = string.IsNullOrWhiteSpace(match.DisplayName) ? d.DisplayName : match.DisplayName;
                            d.DefaultChannelName = string.IsNullOrWhiteSpace(match.DefaultChannelName) ? d.DefaultChannelName : match.DefaultChannelName;
                            d.FallbackCategory = string.IsNullOrWhiteSpace(match.FallbackCategory) ? d.FallbackCategory : match.FallbackCategory;
                            return d;
                        })
                        .ToList();

                    SaveAll(merged);
                    return merged;
                }
                catch
                {
                    return DefaultSettings();
                }
            }
        }

        public static DiscordNotificationCategorySetting? Get(string? category)
        {
            if (string.IsNullOrWhiteSpace(category))
                category = "System";

            return LoadAll().FirstOrDefault(x => Same(x.Category, category))
                ?? LoadAll().FirstOrDefault(x => Same(x.Category, "System"));
        }

        public static void SaveAll(IEnumerable<DiscordNotificationCategorySetting>? settings)
        {
            lock (Gate)
            {
                try
                {
                    var rows = (settings ?? DefaultSettings())
                        .Where(x => !string.IsNullOrWhiteSpace(x.Category))
                        .GroupBy(x => x.Category.Trim(), StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.First())
                        .OrderBy(x => OrderFor(x.Category))
                        .ToList();

                    var dir = Path.GetDirectoryName(StorePath);
                    if (!string.IsNullOrWhiteSpace(dir))
                        Directory.CreateDirectory(dir);

                    File.WriteAllText(StorePath, JsonSerializer.Serialize(rows, JsonOptions));
                }
                catch
                {
                }
            }
        }

        public static void ResetToDefaults()
        {
            SaveAll(DefaultSettings());
        }

        public static void ApplyDiscordSetupPayload(JsonElement root, VtcConfig cfg)
        {
            try
            {
                var settings = LoadAll();

                if (root.TryGetProperty("channels", out var channels))
                {
                    ApplyChannel(settings, "Dispatch", channels, "dispatchChannelId");
                    ApplyChannel(settings, "BOL", channels,"bolChannelId","billsOfLadingChannelId");
                    ApplyChannel(settings, "Maintenance", channels, "maintenanceChannelId");
                    ApplyChannel(settings, "System", channels, "eldNotificationsChannelId", "systemLogChannelId", "announcementsChannelId");
                    ApplyChannel(settings, "Events", channels, "eventsChannelId", "announcementsChannelId");
                    ApplyChannel(settings, "Convoys", channels, "convoyChannelId", "eventsChannelId", "announcementsChannelId");
                    ApplyChannel(settings, "Garages", channels, "garagesChannelId", "systemLogChannelId");
                    ApplyChannel(settings, "Fleet", channels, "fleetChannelId", "fleetTrucksChannelId", "loadboardChannelId");
                    ApplyChannel(settings, "Achievements", channels, "achievementsChannelId", "leaderboardChannelId", "announcementsChannelId");
                    ApplyChannel(settings, "Endorsements", channels, "driverEndorsementsChannelId", "endorsementsChannelId", "systemLogChannelId");
                }

                if (root.TryGetProperty("webhooks", out var webhooks))
                {
                    ApplyWebhook(settings, "Dispatch", webhooks, "dispatchWebhookUrl");
                    ApplyWebhook(settings, "BOL", webhooks, "bolWebhookUrl", "billsOfLadingWebhookUrl");
                    ApplyWebhook(settings, "Maintenance", webhooks, "maintenanceWebhookUrl");
                    ApplyWebhook(settings, "System", webhooks, "eldNotificationsWebhookUrl", "systemWebhookUrl", "announcementsWebhookUrl");
                    ApplyWebhook(settings, "Events", webhooks, "eventsWebhookUrl", "announcementsWebhookUrl");
                    ApplyWebhook(settings, "Convoys", webhooks, "convoyWebhookUrl", "eventsWebhookUrl", "announcementsWebhookUrl");
                    ApplyWebhook(settings, "Garages", webhooks, "garagesWebhookUrl", "systemWebhookUrl");
                    ApplyWebhook(settings, "Fleet", webhooks, "fleetWebhookUrl", "fleetTrucksWebhookUrl", "leaderboardWebhookUrl");
                    ApplyWebhook(settings, "Achievements", webhooks, "achievementsWebhookUrl", "leaderboardWebhookUrl", "announcementsWebhookUrl");
                    ApplyWebhook(settings, "Endorsements", webhooks, "driverEndorsementsWebhookUrl", "endorsementsWebhookUrl", "systemWebhookUrl");
                }

                // Existing config fallback so the system works before bot-side notification fields are added.
                ApplyExistingConfigFallback(settings, cfg);

                SaveAll(settings);
            }
            catch
            {
            }
        }

        public static void ApplyExistingConfigFallback(List<DiscordNotificationCategorySetting> settings, VtcConfig cfg)
        {
            try
            {
                cfg.Discord ??= new VtcConfig.DiscordConfig();

                SetIfBlank(settings, "Dispatch", cfg.Discord.DispatchChannelId, cfg.Discord.DispatchWebhookUrl);
                SetIfBlank(settings, "BOL", cfg.Discord.BolChannelId, cfg.Discord.BolWebhookUrl);
                SetIfBlank(settings, "Maintenance", cfg.Discord.MaintenanceChannelId, cfg.Discord.MaintenanceWebhookUrl);
                SetIfBlank(settings, "System", FirstNonBlank(cfg.Discord.SystemLogChannelId, cfg.Discord.AnnouncementsChannelId), FirstNonBlank(cfg.Discord.SystemWebhookUrl, cfg.Discord.AnnouncementsWebhookUrl));
                SetIfBlank(settings, "Events", cfg.Discord.AnnouncementsChannelId, cfg.Discord.AnnouncementsWebhookUrl);
                SetIfBlank(settings, "Convoys", cfg.Discord.AnnouncementsChannelId, cfg.Discord.AnnouncementsWebhookUrl);
                SetIfBlank(settings, "Fleet", FirstNonBlank(cfg.Discord.LoadboardChannelId, cfg.Discord.LeaderboardChannelId), cfg.Discord.LeaderboardWebhookUrl);
                SetIfBlank(settings, "Achievements", FirstNonBlank(cfg.Discord.LeaderboardChannelId, cfg.Discord.AnnouncementsChannelId), FirstNonBlank(cfg.Discord.LeaderboardWebhookUrl, cfg.Discord.AnnouncementsWebhookUrl));
                SetIfBlank(settings, "Endorsements", cfg.Discord.SystemLogChannelId, cfg.Discord.SystemWebhookUrl);
            }
            catch
            {
            }
        }

        private static void SetIfBlank(List<DiscordNotificationCategorySetting> settings, string category, string? channelId, string? webhookUrl)
        {
            var row = settings.FirstOrDefault(x => Same(x.Category, category));
            if (row == null) return;

            if (string.IsNullOrWhiteSpace(row.ChannelId) && !string.IsNullOrWhiteSpace(channelId))
                row.ChannelId = channelId.Trim();

            if (string.IsNullOrWhiteSpace(row.WebhookUrl) && !string.IsNullOrWhiteSpace(webhookUrl))
                row.WebhookUrl = webhookUrl.Trim();
        }

        private static void ApplyChannel(List<DiscordNotificationCategorySetting> settings, string category, JsonElement source, params string[] names)
        {
            var value = FirstJsonString(source, names);
            if (string.IsNullOrWhiteSpace(value)) return;
            var row = settings.FirstOrDefault(x => Same(x.Category, category));
            if (row != null) row.ChannelId = value.Trim();
        }

        private static void ApplyWebhook(List<DiscordNotificationCategorySetting> settings, string category, JsonElement source, params string[] names)
        {
            var value = FirstJsonString(source, names);
            if (string.IsNullOrWhiteSpace(value)) return;
            var row = settings.FirstOrDefault(x => Same(x.Category, category));
            if (row != null) row.WebhookUrl = value.Trim();
        }

        private static string FirstJsonString(JsonElement source, params string[] names)
        {
            foreach (var name in names)
            {
                if (source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    var text = (value.GetString() ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }
            return "";
        }

        private static DiscordNotificationCategorySetting Setting(string category, string display, string channel, bool enabled, string fallback)
        {
            return new DiscordNotificationCategorySetting
            {
                Category = category,
                DisplayName = display,
                DefaultChannelName = channel,
                Enabled = enabled,
                FallbackCategory = string.IsNullOrWhiteSpace(fallback) ? "System" : fallback
            };
        }

        private static int OrderFor(string? category)
        {
            var order = new[] { "System", "Dispatch", "BOL", "Events", "Convoys", "Garages", "Fleet", "Maintenance", "Achievements", "Endorsements" };
            var idx = Array.FindIndex(order, x => Same(x, category));
            return idx < 0 ? 999 : idx;
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

        private static bool Same(string? a, string? b)
        {
            return string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
