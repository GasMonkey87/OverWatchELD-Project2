using OverWatchELD.Services;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OverWatchELD.Services
{
    public static class VtcDriverPresenceSyncService
    {
        private static readonly HttpClient Http = new HttpClient();

        public static async Task SyncAsync(bool isOnline)
        {
            try
            {
                var cfg = VtcConfigService.Load();
                var identity = DiscordIdentityStore.Load();

                var guildId = cfg?.GuildId ?? "";
                var baseUrl = cfg?.BotApiBaseUrl ?? "https://overwatcheld.up.railway.app";

                var discordUserId = identity?.DiscordUserId ?? "";
                var driverName = identity?.DiscordUsername ?? Environment.UserName;

                if (string.IsNullOrWhiteSpace(guildId) || string.IsNullOrWhiteSpace(discordUserId))
                    return;

                var payload = new
                {
                    guildId,
                    discordUserId,
                    driverName,
                    isOnline,
                    status = isOnline ? "Online" : "Offline",
                    lastSeenUtc = DateTime.UtcNow
                };

                var json = JsonSerializer.Serialize(payload);
                using var body = new StringContent(json, Encoding.UTF8, "application/json");

                await Http.PostAsync($"{baseUrl.TrimEnd('/')}/api/drivers/presence", body);
            }
            catch
            {
            }
        }
    }
}