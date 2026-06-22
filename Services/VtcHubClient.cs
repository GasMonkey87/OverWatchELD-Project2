using System;
using System.Net.Http;
using System.Net.Http.Headers;

namespace OverWatchELD.Services
{
    /// <summary>
    /// Small helper to call the Railway hub in a SaaS/multi-guild-safe way.
    /// - Adds guildId (query + X-Guild-Id header) when configured.
    /// - Adds optional Bearer token when configured (DeviceToken or BotApiKey).
    /// Public release friendly: if no guildId is configured, requests are sent as-is.
    /// </summary>
    internal static class VtcHubClient
    {
        public static string GetBaseUrl()
        {
            var cfg = VtcConfigService.Load();
            return (cfg?.BotApiBaseUrl ?? "").Trim().TrimEnd('/');
        }

        public static string GetGuildId()
        {
            var cfg = VtcConfigService.Load();
            var gid = (cfg?.Discord?.GuildId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(gid) || gid == "0") return "";
            return gid;
        }

        public static HttpRequestMessage Create(HttpMethod method, string url)
        {
            var cfg = VtcConfigService.Load();
            var gid = (cfg?.Discord?.GuildId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(gid) || gid == "0") gid = "";

            // If caller didn't add guildId, add it (query).
            if (!string.IsNullOrWhiteSpace(gid) && url.IndexOf("guildId=", StringComparison.OrdinalIgnoreCase) < 0)
            {
                url = url.Contains("?")
                    ? (url + "&guildId=" + Uri.EscapeDataString(gid))
                    : (url + "?guildId=" + Uri.EscapeDataString(gid));
            }

            var req = new HttpRequestMessage(method, url);
            req.Headers.Accept.Clear();
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (!string.IsNullOrWhiteSpace(gid))
            {
                try { req.Headers.Remove("X-Guild-Id"); } catch { }
                req.Headers.Add("X-Guild-Id", gid);
            }

            // Prefer per-install token; fallback to BotApiKey (legacy).
            var token = (cfg?.DeviceToken ?? "").Trim();
            if (string.IsNullOrWhiteSpace(token)) token = (cfg?.BotApiKey ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(token))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            return req;
        }
    }
}
