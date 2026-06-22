using OverWatchELD.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace OverWatchELD.Services
{
    public class DiscordChatApiClient
    {
        private readonly HttpClient _http = new();
        private readonly JsonSerializerOptions _opts = new() { PropertyNameCaseInsensitive = true };

        private readonly string _baseUrl;

        public DiscordChatApiClient(string baseUrl)
        {
            _baseUrl = baseUrl.TrimEnd('/');
        }

        public async Task<List<ChatMessage>> GetThreadMessagesAsync(string guildId, string threadId)
        {
            var url = $"{_baseUrl}/api/messages/thread?guildId={guildId}&threadId={threadId}";
            var resp = await _http.GetFromJsonAsync<ApiResp>(url, _opts);
            return resp?.Items ?? new List<ChatMessage>();
        }

        public async Task SendTextAsync(string guildId, string threadId, string text)
        {
            var url = $"{_baseUrl}/api/messages/thread/send?guildId={guildId}&threadId={threadId}";
            await _http.PostAsJsonAsync(url, new { text });
        }

        public async Task SendWithFilesAsync(string guildId, string threadId, string text, List<string> filePaths)
        {
            var url = $"{_baseUrl}/api/messages/thread/sendform?guildId={guildId}&threadId={threadId}";

            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(text ?? ""), "text");

            foreach (var path in filePaths)
            {
                if (!File.Exists(path)) continue;

                var stream = File.OpenRead(path);
                var content = new StreamContent(stream);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                form.Add(content, "files", Path.GetFileName(path));
            }

            await _http.PostAsync(url, form);
        }

        public async Task MarkReadAsync(string channelId, string messageId)
        {
            await _http.PostAsJsonAsync($"{_baseUrl}/api/messages/markread",
                new { channelId, messageId });
        }

        public async Task BulkMarkReadAsync(string channelId, List<string> ids)
        {
            await _http.PostAsJsonAsync($"{_baseUrl}/api/messages/markread/bulk",
                new { channelId, messageIds = ids });
        }

        public async Task DeleteAsync(string channelId, string messageId)
        {
            var req = new HttpRequestMessage(HttpMethod.Delete, $"{_baseUrl}/api/messages/delete")
            {
                Content = JsonContent.Create(new { channelId, messageId })
            };
            await _http.SendAsync(req);
        }

        public async Task BulkDeleteAsync(string channelId, List<string> ids)
        {
            var req = new HttpRequestMessage(HttpMethod.Delete, $"{_baseUrl}/api/messages/delete/bulk")
            {
                Content = JsonContent.Create(new { channelId, messageIds = ids })
            };
            await _http.SendAsync(req);
        }

        private class ApiResp
        {
            public bool Ok { get; set; }
            public List<ChatMessage> Items { get; set; } = new();
        }
    }
}