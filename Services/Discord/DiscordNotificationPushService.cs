using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OverWatchELD.Services.Discord
{
    public static class DiscordNotificationPushService
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

        public static async Task<bool> PushAsync(string category, string title, string message, string? details = null)
        {
            try
            {
                var settings = DiscordNotificationSettingsService.LoadAll();
                var setting = DiscordNotificationSettingsService.Get(category);
                if (setting == null || !setting.Enabled)
                    return false;

                if (string.IsNullOrWhiteSpace(setting.WebhookUrl) && !string.IsNullOrWhiteSpace(setting.FallbackCategory))
                {
                    var fallback = DiscordNotificationSettingsService.Get(setting.FallbackCategory);
                    if (fallback != null && fallback.Enabled && !string.IsNullOrWhiteSpace(fallback.WebhookUrl))
                        setting = fallback;
                }

                if (!string.IsNullOrWhiteSpace(setting.WebhookUrl))
                {
                    return await PostWebhookAsync(setting.WebhookUrl, category, title, message, details);
                }

                return await PostBotRouteAsync(setting, category, title, message, details);
            }
            catch
            {
                return false;
            }
        }

        public static void PushFireAndForget(string category, string title, string message, string? details = null)
        {
            _ = Task.Run(async () => await PushAsync(category, title, message, details));
        }

        private static async Task<bool> PostWebhookAsync(string webhookUrl, string category, string title, string message, string? details)
        {
            var payload = new
            {
                username = "OverWatch ELD",
                embeds = new[]
                {
                    new
                    {
                        title = $"{IconFor(category)} {title}",
                        description = string.IsNullOrWhiteSpace(details) ? message : message + "\n\n" + details,
                        color = ColorFor(category),
                        footer = new { text = $"OverWatch ELD • {category}" },
                        timestamp = DateTimeOffset.UtcNow.ToString("O")
                    }
                }
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await Http.PostAsync(webhookUrl.Trim(), content);
            return resp.IsSuccessStatusCode;
        }

        private static async Task<bool> PostBotRouteAsync(DiscordNotificationCategorySetting setting, string category, string title, string message, string? details)
        {
            var cfg = VtcConfigService.Load(forceReload: true);
            var baseUrl = (cfg.BotApiBaseUrl ?? "").Trim().TrimEnd('/');
            var guildId = (cfg.Discord?.GuildId ?? cfg.GuildId ?? "").Trim();

            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(guildId))
                return false;

            var payload = new
            {
                guildId,
                category,
                title,
                message,
                details = details ?? "",
                channelId = setting.ChannelId ?? "",
                defaultChannelName = setting.DefaultChannelName ?? ""
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var resp = await Http.PostAsync(baseUrl + "/api/notifications/push", content);
            return resp.IsSuccessStatusCode;
        }

        private static string IconFor(string? category)
        {
            return (category ?? "").Trim().ToLowerInvariant() switch
            {
                "dispatch" => "📦",
                "events" => "📅",
                "convoys" => "🚚",
                "garages" => "🏢",
                "fleet" => "🚛",
                "maintenance" => "🛠️",
                "achievements" => "🏆",
                "endorsements" => "🪪",
                _ => "📣"
            };
        }

        private static int ColorFor(string? category)
        {
            return (category ?? "").Trim().ToLowerInvariant() switch
            {
                "dispatch" => 0x3B82F6,
                "events" => 0xA855F7,
                "convoys" => 0xF97316,
                "garages" => 0x22C55E,
                "fleet" => 0x10B981,
                "maintenance" => 0xEF4444,
                "achievements" => 0xFACC15,
                "endorsements" => 0x38BDF8,
                _ => 0x94A3B8
            };
        }
    }
}
