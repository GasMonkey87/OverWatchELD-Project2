// Services/BotApiService.cs  ✅ FULL COPY/REPLACE
// Purpose: Simple client used by the ELD to talk to your Railway API (Hub OR Bot API).
// Fixes:
// - Always uses an ABSOLUTE base URL (prevents "invalid request URI" errors)
// - Sends guildId as a QUERY PARAM (?guildId=...) which matches your bot API design
// - Optional Bearer token support (works with your Hub if you require an API key later)
// - FIXED: Location ping method is inside BotApiService class
// - FIXED: Removed old invalid references (GuildId / BotApiBaseUrl / _write)
// - FIXED: Clean brace structure so file compiles
// - FIXED: Dispatch messaging sends a CLEAN JSON body with Text only (no duplicate text/body/message/content keys)

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace OverWatchELD.Services
{
    public sealed class BotApiService
    {
        private static readonly JsonSerializerOptions JsonReadOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly JsonSerializerOptions JsonWriteOpts = new()
        {
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly HttpClient _http;
        private readonly string _baseUrl;     // normalized, no trailing slash
        private readonly string? _guildId;    // optional default
        private readonly string? _bearer;     // optional

        public BotApiService(string? baseUrl, string? defaultGuildId = null, string? bearerToken = null)
        {
            _baseUrl = NormalizeBaseUrl(baseUrl);
            _guildId = string.IsNullOrWhiteSpace(defaultGuildId) ? null : defaultGuildId.Trim();
            _bearer = string.IsNullOrWhiteSpace(bearerToken) ? null : bearerToken.Trim();

            _http = new HttpClient
            {
                BaseAddress = new Uri(_baseUrl + "/", UriKind.Absolute),
                Timeout = TimeSpan.FromSeconds(10)
            };

            if (!string.IsNullOrWhiteSpace(_bearer))
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _bearer);
        }

        public string BaseUrl => _baseUrl;

        public async Task<bool> HealthAsync(CancellationToken ct = default)
        {
            using var resp = await _http.GetAsync("health", ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }

        public async Task<IReadOnlyList<MessageDto>> GetMessagesAsync(
            string? guildId = null,
            string? driverName = null,
            CancellationToken ct = default)
        {
            var gid = ResolveGuildId(guildId);

            var qs = new List<string>();
            if (!string.IsNullOrWhiteSpace(gid))
                qs.Add("guildId=" + Uri.EscapeDataString(gid));
            if (!string.IsNullOrWhiteSpace(driverName))
                qs.Add("driverName=" + Uri.EscapeDataString(driverName.Trim()));

            var url = "api/messages";
            if (qs.Count > 0)
                url += "?" + string.Join("&", qs);

            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            var txt = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"GET {url} failed: {(int)resp.StatusCode} {resp.ReasonPhrase} | {txt}");

            try
            {
                var wrapper = JsonSerializer.Deserialize<MessagesResponse>(txt, JsonReadOpts);
                if (wrapper?.Items != null)
                    return wrapper.Items;
            }
            catch
            {
                // fall through to raw array parsing
            }

            var arr = JsonSerializer.Deserialize<List<MessageDto>>(txt, JsonReadOpts) ?? new List<MessageDto>();
            return arr;
        }

        public async Task SendMessageAsync(
            string text,
            string? driverName = null,
            string source = "eld",
            string? guildId = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Message text is empty.", nameof(text));

            var gid = ResolveGuildId(guildId);

            string discordUserId = "";
            string discordUsername = "";
            try
            {
                var ident = DiscordIdentityStore.Load();
                if (string.IsNullOrWhiteSpace(gid))
                    gid = (ident.GuildId ?? "").Trim();

                discordUserId = (ident.DiscordUserId ?? "").Trim();
                discordUsername = (ident.DiscordUsername ?? "").Trim();
            }
            catch
            {
                // ignore identity load failure
            }

            var cleanText = text.Trim();
            var displayName = NormalizeDisplayName(driverName, discordUsername);

            var url = "api/messages/send";
            var urlParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(gid))
                urlParts.Add("guildId=" + Uri.EscapeDataString(gid));
            urlParts.Add("route=dispatch");
            urlParts.Add("direction=from_driver");
            urlParts.Add("text=" + Uri.EscapeDataString(cleanText));
            urlParts.Add("driverName=" + Uri.EscapeDataString(displayName));
            if (!string.IsNullOrWhiteSpace(discordUserId))
            {
                urlParts.Add("discordUserId=" + Uri.EscapeDataString(discordUserId));
                urlParts.Add("userId=" + Uri.EscapeDataString(discordUserId));
            }
            url += "?" + string.Join("&", urlParts);

            // IMPORTANT:
            // Keep this payload CLEAN. Do not send duplicate Text/text/Body/body/Message/message keys.
            // The deployed bot accepts Text from the JSON body. Sending duplicate casing caused BadJson.
            var payload = new SendDispatchMessagePayload
            {
                Text = cleanText,
                DriverDiscordUserId = discordUserId,
                DriverName = displayName,
                Route = "dispatch",
                Direction = "from_driver",
                Source = string.IsNullOrWhiteSpace(source) ? "eld" : source.Trim()
            };

            var json = JsonSerializer.Serialize(payload, JsonWriteOpts);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var resp = await _http.PostAsync(url, content, ct).ConfigureAwait(false);
            var txt = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            System.Diagnostics.Debug.WriteLine($"[DISPATCH SEND] POST {url} -> {(int)resp.StatusCode}: {txt}");
            System.Diagnostics.Debug.WriteLine($"[DISPATCH SEND] JSON: {json}");

            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"POST {url} failed: {(int)resp.StatusCode} {resp.ReasonPhrase} | {txt} | Sent JSON: {json}");
        }

        // ============================================================
        // ✅ Live Map: push driver location to VTC Bot
        // POST {baseUrl}/api/location/ping?guildId=...
        // ============================================================
        public async Task<bool> PostLocationPingAsync(LocationPing ping, CancellationToken ct = default)
        {
            try
            {
                var gid = ResolveGuildId(null);

                if (string.IsNullOrWhiteSpace(gid) || gid == "0")
                    return false;
                if (ping == null)
                    return false;
                if (string.IsNullOrWhiteSpace(ping.DriverId))
                    return false;

                var url = $"api/location/ping?guildId={Uri.EscapeDataString(gid)}";

                var payload = JsonSerializer.Serialize(new
                {
                    guildId = gid,
                    driverId = ping.DriverId,
                    driverName = ping.DriverName,
                    x = ping.X,
                    z = ping.Z,
                    speedMph = ping.SpeedMph,
                    headingDeg = ping.HeadingDeg,
                    dutyStatus = ping.DutyStatus
                }, JsonWriteOpts);

                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var resp = await _http.PostAsync(url, content, ct).ConfigureAwait(false);
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private string? ResolveGuildId(string? guildId)
        {
            if (!string.IsNullOrWhiteSpace(guildId))
                return guildId.Trim();

            return _guildId;
        }

        private static string NormalizeBaseUrl(string? url)
        {
            url = (url ?? "").Trim();

            if (string.IsNullOrWhiteSpace(url))
                return "http://127.0.0.1:8080";

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            while (url.EndsWith("/"))
                url = url.Substring(0, url.Length - 1);

            _ = new Uri(url, UriKind.Absolute);
            return url;
        }

        private static string NormalizeDisplayName(string? requested, string discordUsername)
        {
            var dn = (requested ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(dn) &&
                !dn.Equals("User", StringComparison.OrdinalIgnoreCase))
                return dn;

            if (!string.IsNullOrWhiteSpace(discordUsername))
                return discordUsername;

            return string.IsNullOrWhiteSpace(dn) ? "User" : dn;
        }

        private sealed class MessagesResponse
        {
            public bool Ok { get; set; }
            public string? GuildId { get; set; }
            public List<MessageDto>? Items { get; set; }
        }

        private sealed class SendDispatchMessagePayload
        {
            public string Text { get; set; } = "";
            public string? DriverDiscordUserId { get; set; }
            public string? DriverName { get; set; }
            public string Route { get; set; } = "dispatch";
            public string Direction { get; set; } = "from_driver";
            public string Source { get; set; } = "eld";
        }

        public sealed class SendMessageReq
        {
            [JsonPropertyName("driverName")]
            public string? DisplayName { get; set; }

            public string? DiscordUserId { get; set; }
            public string? DiscordUsername { get; set; }
            public string Text { get; set; } = "";
            public string? Source { get; set; }
        }

        public sealed class MessageDto
        {
            public string Id { get; set; } = "";
            public string GuildId { get; set; } = "";

            [JsonPropertyName("driverName")]
            public string DisplayName { get; set; } = "";

            public string Text { get; set; } = "";
            public string Source { get; set; } = "";
            public DateTimeOffset CreatedUtc { get; set; }
        }
    }

    public sealed class LocationPing
    {
        public string DriverId { get; set; } = "";
        public string? DriverName { get; set; }
        public double X { get; set; }
        public double Z { get; set; }
        public double SpeedMph { get; set; }
        public double HeadingDeg { get; set; }
        public string? DutyStatus { get; set; }
    }
}
