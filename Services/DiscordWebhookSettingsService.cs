using OverWatchELD.Models;
using System;
using System.IO;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public sealed class DiscordWebhookSettingsService
    {
        private readonly string _path;

        public DiscordWebhookSettingsService()
        {
            _path = Path.Combine(AppContext.BaseDirectory, "discord_webhooks.json");
        }

        public DiscordWebhookSettings Load()
        {
            try
            {
                if (!File.Exists(_path))
                    return new DiscordWebhookSettings();

                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<DiscordWebhookSettings>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new DiscordWebhookSettings();
            }
            catch
            {
                return new DiscordWebhookSettings();
            }
        }

        public void Save(DiscordWebhookSettings settings)
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, json);
        }
    }
}
