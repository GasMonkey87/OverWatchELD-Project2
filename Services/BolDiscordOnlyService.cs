using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using OverWatchELD.Services.Discord;

namespace OverWatchELD.Services
{
    public sealed class BolDiscordOnlyService
    {
        public static BolDiscordOnlyService Shared { get; } = new();

        private readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        private string LogPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OverWatchELD",
                "bol_discord.log");

        private BolDiscordOnlyService()
        {
        }

        public async Task<string> PostAsync(
            string loadNumber,
            string driver,
            string truck,
            string cargo,
            double weight,
            string startLocation,
            string endLocation,
            string status)
        {
            var cfg = VtcConfigService.LoadOrCreate();

            var baseUrl = (cfg.BotApiBaseUrl ?? "").Trim().TrimEnd('/');
            var guildId = (cfg.Discord?.GuildId ?? cfg.GuildId ?? "").Trim();

            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new InvalidOperationException("BotApiBaseUrl is empty in VTC config.");

            if (string.IsNullOrWhiteSpace(guildId))
                throw new InvalidOperationException("GuildId is empty in VTC config.");

            var title = status.Equals("Delivered", StringComparison.OrdinalIgnoreCase)
                ? "Load Delivered"
                : "BOL Posted";

            var message =
                $"Load #: {Safe(loadNumber)}\n" +
                $"Driver: {Safe(driver)}\n" +
                $"Truck: {Safe(truck)}\n" +
                $"Cargo: {Safe(cargo)}\n" +
                $"Weight: {weight:N0} lbs\n" +
                $"From: {Safe(startLocation)}\n" +
                $"To: {Safe(endLocation)}\n" +
                $"Status: {Safe(status)}";

            var routePayload = new
            {
                guildId,
                GuildId = guildId,
                loadNumber = loadNumber ?? "",
                currentLoadNumber = loadNumber ?? "",
                driver = driver ?? "",
                truck = truck ?? "",
                cargo = cargo ?? "",
                weight,
                startLocation = startLocation ?? "",
                endLocation = endLocation ?? "",
                status = status ?? "",
                category = "BOL",
                title,
                message,
                details = message,
                defaultChannelName = "bol",
                DefaultChannelName = "bol"
            };

            var firstError = "";

            try
            {
                var result = await PostJsonAsync($"{baseUrl}/api/loads/bol/post", routePayload);
                Log("BOL route succeeded: " + result);
                return result;
            }
            catch (Exception ex)
            {
                firstError = ex.Message;
                Log("BOL route without query failed: " + ex);
            }

            try
            {
                var result = await PostJsonAsync($"{baseUrl}/api/loads/bol/post?guildId={Uri.EscapeDataString(guildId)}", routePayload);
                Log("BOL route with query succeeded: " + result);
                return result;
            }
            catch (Exception ex)
            {
                Log("BOL route with query failed: " + ex);
            }

            try
            {
                var notificationPayload = new
                {
                    guildId,
                    GuildId = guildId,
                    category = "BOL",
                    Category = "BOL",
                    title,
                    Title = title,
                    message,
                    Message = message,
                    details = message,
                    Details = message,
                    defaultChannelName = "bol",
                    DefaultChannelName = "bol"
                };

                var result = await PostJsonAsync($"{baseUrl}/api/notifications/push", notificationPayload);
                Log("Notification fallback succeeded: " + result);
                return result;
            }
            catch (Exception ex)
            {
                Log("Notification fallback failed: " + ex);
            }

            try
            {
                var pushed = await DiscordNotificationPushService.PushAsync("BOL", title, message, message);
                if (pushed)
                {
                    var result = "Posted by DiscordNotificationPushService fallback.";
                    Log(result);
                    return result;
                }
            }
            catch (Exception ex)
            {
                Log("DiscordNotificationPushService fallback failed: " + ex);
            }

            throw new InvalidOperationException(
                "Failed to post BOL to Discord. The bot BOL route returned an error and the notification fallback also failed. First route error: " + firstError);
        }

        private async Task<string> PostJsonAsync(string url, object payload)
        {
            var json = JsonSerializer.Serialize(payload);
            Log("POST " + url + " JSON: " + json);

            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync(url, content);
            var body = await resp.Content.ReadAsStringAsync();
            var result = $"POST {url} HTTP {(int)resp.StatusCode}: {body}";
            Log(result);

            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException(result);

            return result;
        }

        private static string Safe(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();
        }

        private void Log(string message)
        {
            try
            {
                var dir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch
            {
            }
        }
    }
}
