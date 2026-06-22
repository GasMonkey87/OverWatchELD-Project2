using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OverWatchELD.Services
{
    public sealed class DiscordBotApiClient
    {
        private readonly HttpClient _http;

        public DiscordBotApiClient(HttpClient http) => _http = http;

        public async Task<(bool ok, string? apiKey, string message)> ConfirmLinkAsync(
            string baseUrl, string linkSecret, string code, string driverName, string deviceName)
        {
            baseUrl = (baseUrl ?? "").Trim().TrimEnd('/');
            var url = baseUrl + "/api/link/confirm";

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Add("X-Link-Secret", linkSecret);

            var payload = new
            {
                code = code?.Trim(),
                driverName = driverName?.Trim(),
                deviceName = deviceName?.Trim()
            };

            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var resp = await _http.SendAsync(req);
            var text = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                return (false, null, $"Link failed: {(int)resp.StatusCode} {text}");

            try
            {
                using var doc = JsonDocument.Parse(text);
                var ok = doc.RootElement.GetProperty("ok").GetBoolean();
                var apiKey = doc.RootElement.TryGetProperty("apiKey", out var k) ? k.GetString() : null;
                return (ok, apiKey, ok ? "Linked ✅" : "Link failed");
            }
            catch
            {
                return (false, null, "Link response parse failed");
            }
        }

        public async Task<bool> PostStatusAsync(string baseUrl, string apiKey, object statusPayload)
        {
            baseUrl = (baseUrl ?? "").Trim().TrimEnd('/');
            var url = baseUrl + "/api/status";

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Content = new StringContent(JsonSerializer.Serialize(statusPayload), Encoding.UTF8, "application/json");

            using var resp = await _http.SendAsync(req);
            return resp.IsSuccessStatusCode;
        }
    }
}
