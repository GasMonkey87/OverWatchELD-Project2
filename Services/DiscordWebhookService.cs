using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OverWatchELD.Services
{
    public static partial class DiscordWebhookService
    {
        private static readonly HttpClient _http = new HttpClient();
        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        public static async Task<bool> SendTextAsync(string webhookUrl, string content)
        {
            try
            {
                webhookUrl = (webhookUrl ?? "").Trim();
                if (string.IsNullOrWhiteSpace(webhookUrl)) return false;

                var payload = new { content = content ?? "" };
                var json = JsonSerializer.Serialize(payload, JsonOpts);

                using var req = new HttpRequestMessage(HttpMethod.Post, webhookUrl);
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using var resp = await _http.SendAsync(req).ConfigureAwait(false);
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<(bool ok, string err)> SendWebhookAsync(string webhookUrl, string? username, string content)
        {
            try
            {
                webhookUrl = (webhookUrl ?? "").Trim();
                if (string.IsNullOrWhiteSpace(webhookUrl))
                    return (false, "Webhook URL is empty.");

                if (!Uri.TryCreate(webhookUrl, UriKind.Absolute, out var uri))
                    return (false, "Webhook URL is not a valid absolute URL.");

                var payload = new
                {
                    content = content ?? "",
                    username = string.IsNullOrWhiteSpace(username) ? null : username
                };

                // Use your JsonOpts (case-insensitive is fine; Discord ignores unknown fields)
                var json = JsonSerializer.Serialize(payload, JsonOpts);

                using var req = new HttpRequestMessage(HttpMethod.Post, uri);
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using var resp = await _http.SendAsync(req).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(text) && text.Length > 240) text = text.Substring(0, 240) + "…";
                    var detail = string.IsNullOrWhiteSpace(text) ? "" : $": {text}";
                    return (false, $"Discord returned {(int)resp.StatusCode} {resp.ReasonPhrase}{detail}");
                }

                return (true, "");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
        public static async Task<bool> SendFileAsync(string webhookUrl, string filePath, string? content = null)
        {
            try
            {
                webhookUrl = (webhookUrl ?? "").Trim();
                if (string.IsNullOrWhiteSpace(webhookUrl)) return false;
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;

                using var form = new MultipartFormDataContent();

                // content message
                if (!string.IsNullOrWhiteSpace(content))
                {
                    form.Add(new StringContent(content, Encoding.UTF8), "content");
                }

                // file
                var fileName = Path.GetFileName(filePath);
                var bytes = await File.ReadAllBytesAsync(filePath).ConfigureAwait(false);
                var fileContent = new ByteArrayContent(bytes);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                form.Add(fileContent, "file", fileName);

                using var resp = await _http.PostAsync(webhookUrl, form).ConfigureAwait(false);
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}
