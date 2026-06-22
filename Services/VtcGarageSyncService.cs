using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using OverWatchELD.Models;

namespace OverWatchELD.Services
{
    public static class VtcGarageSyncService
    {
        private static readonly HttpClient Http = new();

        public static async Task<(bool Ok, string Message)> SyncAsync(string botBaseUrl, string guildId)
        {
            try
            {
                botBaseUrl = (botBaseUrl ?? "").Trim().TrimEnd('/');
                guildId = (guildId ?? "").Trim();

                if (string.IsNullOrWhiteSpace(botBaseUrl))
                    return (false, "Missing bot URL.");

                if (string.IsNullOrWhiteSpace(guildId))
                    return (false, "Missing guild ID.");

                var garages = VtcGarageStore.Load();

                var payload = new
                {
                    garages = garages.Select(g => new
                    {
                        id = g.Id,
                        city = g.CityName,
                        state = g.State,
                        size = g.Size,
                        capacity = g.TruckCapacity,
                        assigned = g.AssignedTruckNumbers.Count,
                        trucks = g.AssignedTruckNumbers,
                        owned = g.IsOwned,
                        mapX = g.MapX,
                        mapY = g.MapY
                    }).ToList()
                };

                var json = JsonSerializer.Serialize(payload);
                var url = $"{botBaseUrl}/api/vtc/garages/save?guildId={Uri.EscapeDataString(guildId)}";

                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var res = await Http.PostAsync(url, content).ConfigureAwait(false);

                var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!res.IsSuccessStatusCode)
                    return (false, $"Sync failed: HTTP {(int)res.StatusCode} {body}");

                return (true, "Garages synced to VTC live map.");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}