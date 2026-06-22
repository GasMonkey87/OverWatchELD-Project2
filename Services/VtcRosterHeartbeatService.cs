using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OverWatchELD.Services
{
    public sealed class VtcRosterHeartbeatService
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public static async Task TryPostAsync(string botBaseUrl, TelemetrySnapshot snapshot, string dutyStatusText)
        {
            try
            {
                var pairing = VtcPairingStore.Load();
                if (pairing == null) return;

                if (string.IsNullOrWhiteSpace(pairing.GuildId) ||
                    string.IsNullOrWhiteSpace(pairing.DiscordUserId))
                    return;

                if (string.IsNullOrWhiteSpace(botBaseUrl)) return;

                var payload = new
                {
                    guildId = pairing.GuildId,
                    discordUserId = pairing.DiscordUserId,
                    discordUsername = pairing.DiscordUsername,

                    truck = snapshot.TruckMakeModel ?? "",
                    city = snapshot.City ?? "",
                    state = snapshot.State ?? "",
                    dutyStatus = dutyStatusText ?? ""
                };

                var json = JsonSerializer.Serialize(payload, JsonOpts);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");

                var url = botBaseUrl.TrimEnd('/') + "/api/vtc/roster/heartbeat";
                _ = await Http.PostAsync(url, content).ConfigureAwait(false);
            }
            catch { }
        }
    }
}