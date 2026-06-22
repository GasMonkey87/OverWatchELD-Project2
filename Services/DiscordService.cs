using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OverWatchELD.Services
{
    public static class DiscordService
    {
        public static string WebhookUrl { get; set; } = "";

        public static async Task PostAsync(string message)
        {
            if (string.IsNullOrWhiteSpace(WebhookUrl)) return;

            using var http = new HttpClient();
            var payload = JsonSerializer.Serialize(new { content = message });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            await http.PostAsync(WebhookUrl, content);
        }
    }
}