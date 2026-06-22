using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace OverWatchELD.Services
{
    public sealed class VtcAwardsApiService
    {
        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public sealed class VtcAwardDto
        {
            public string Id { get; set; } = "";
            public string GuildId { get; set; } = "";
            public string Name { get; set; } = "";
            public string Description { get; set; } = "";
            public string IconEmoji { get; set; } = "🏆";
            public string CreatedByUserId { get; set; } = "";
            public string CreatedByUsername { get; set; } = "";
            public DateTime CreatedUtc { get; set; }
            public bool IsAchievement { get; set; }
        }

        public sealed class DriverAwardDto
        {
            public string DriverId { get; set; } = "";
            public string DriverName { get; set; } = "";
            public string AwardId { get; set; } = "";
            public DateTime AwardedUtc { get; set; }
            public string AwardedByUsername { get; set; } = "";
            public string Note { get; set; } = "";
            public VtcAwardDto? Award { get; set; }
        }

        public sealed class CreateAwardReq
        {
            public string GuildId { get; set; } = "";
            public string Name { get; set; } = "";
            public string Description { get; set; } = "";
            public string IconEmoji { get; set; } = "🏆";
            public bool IsAchievement { get; set; }
            public string CreatedByUserId { get; set; } = "";
            public string CreatedByUsername { get; set; } = "";
        }

        public sealed class AssignAwardReq
        {
            public string GuildId { get; set; } = "";
            public string DriverId { get; set; } = "";
            public string DriverName { get; set; } = "";
            public string AwardId { get; set; } = "";
            public string AwardedByUserId { get; set; } = "";
            public string AwardedByUsername { get; set; } = "";
            public string Note { get; set; } = "";
        }

        private sealed class AwardsListResponse
        {
            public bool Ok { get; set; }
            public List<VtcAwardDto>? Awards { get; set; }
        }

        private sealed class DriverAwardsResponse
        {
            public bool Ok { get; set; }
            public List<DriverAwardDto>? Awards { get; set; }
        }

        private sealed class CreateAwardResponse
        {
            public bool Ok { get; set; }
            public VtcAwardDto? Award { get; set; }
        }

        public async Task<List<VtcAwardDto>> GetAwardsAsync(string botBaseUrl, string guildId)
        {
            try
            {
                botBaseUrl = (botBaseUrl ?? "").Trim().TrimEnd('/');
                guildId = (guildId ?? "").Trim();

                if (string.IsNullOrWhiteSpace(botBaseUrl) || string.IsNullOrWhiteSpace(guildId))
                    return new List<VtcAwardDto>();

                var url = $"{botBaseUrl}/api/vtc/awards?guildId={Uri.EscapeDataString(guildId)}";
                var data = await Http.GetFromJsonAsync<AwardsListResponse>(url, JsonOpts);
                return data?.Awards ?? new List<VtcAwardDto>();
            }
            catch
            {
                return new List<VtcAwardDto>();
            }
        }

        public async Task<List<DriverAwardDto>> GetDriverAwardsAsync(string botBaseUrl, string guildId, string driverId)
        {
            try
            {
                botBaseUrl = (botBaseUrl ?? "").Trim().TrimEnd('/');
                guildId = (guildId ?? "").Trim();
                driverId = (driverId ?? "").Trim();

                if (string.IsNullOrWhiteSpace(botBaseUrl) ||
                    string.IsNullOrWhiteSpace(guildId) ||
                    string.IsNullOrWhiteSpace(driverId))
                    return new List<DriverAwardDto>();

                var url = $"{botBaseUrl}/api/vtc/awards/driver?guildId={Uri.EscapeDataString(guildId)}&driverId={Uri.EscapeDataString(driverId)}";
                var data = await Http.GetFromJsonAsync<DriverAwardsResponse>(url, JsonOpts);
                return data?.Awards ?? new List<DriverAwardDto>();
            }
            catch
            {
                return new List<DriverAwardDto>();
            }
        }

        public async Task<VtcAwardDto?> CreateAwardAsync(string botBaseUrl, CreateAwardReq req)
        {
            try
            {
                botBaseUrl = (botBaseUrl ?? "").Trim().TrimEnd('/');
                if (string.IsNullOrWhiteSpace(botBaseUrl))
                    return null;

                var resp = await Http.PostAsJsonAsync($"{botBaseUrl}/api/vtc/awards/create", req);
                if (!resp.IsSuccessStatusCode)
                    return null;

                var data = await resp.Content.ReadFromJsonAsync<CreateAwardResponse>(JsonOpts);
                return data?.Award;
            }
            catch
            {
                return null;
            }
        }

        public async Task<bool> AssignAwardAsync(string botBaseUrl, AssignAwardReq req)
        {
            try
            {
                botBaseUrl = (botBaseUrl ?? "").Trim().TrimEnd('/');
                if (string.IsNullOrWhiteSpace(botBaseUrl))
                    return false;

                var resp = await Http.PostAsJsonAsync($"{botBaseUrl}/api/vtc/awards/assign", req);
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}