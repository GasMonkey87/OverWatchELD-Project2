using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using OverWatchELD.Models;

namespace OverWatchELD.Services
{
    public sealed class VtcRosterApiClient
    {
        private readonly HttpClient _http;
        private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

        public VtcRosterApiClient(HttpClient http)
        {
            _http = http;
        }

        // Calls bot: GET /api/vtc/roster?guildId=...
        // Bot returns ROOT ARRAY -> List<VtcDriver>
        public async Task<List<VtcDriver>> GetRosterAsync(string baseUrl, string guildId)
        {
            baseUrl = (baseUrl ?? "").Trim().TrimEnd('/');
            guildId = (guildId ?? "").Trim();

            if (string.IsNullOrWhiteSpace(baseUrl))
                return new List<VtcDriver>();

            // Use /api/vtc/roster first; if your ELD uses /api/api, we try that too.
            var url1 = $"{baseUrl}/api/vtc/roster?guildId={Uri.EscapeDataString(guildId)}";
            var url2 = $"{baseUrl}/api/api/vtc/roster?guildId={Uri.EscapeDataString(guildId)}";

            var list = await TryFetchAsync(url1) ?? await TryFetchAsync(url2);
            return list ?? new List<VtcDriver>();
        }

        private async Task<List<VtcDriver>?> TryFetchAsync(string url)
        {
            try
            {
                using var resp = await _http.GetAsync(url);
                if (!resp.IsSuccessStatusCode) return null;

                var json = await resp.Content.ReadAsStringAsync();
                var list = JsonSerializer.Deserialize<List<VtcDriver>>(json, _json);
                return list;
            }
            catch { return null; }
        }
    }
}