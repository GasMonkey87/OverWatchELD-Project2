using System;
using System.IO;
using System.Text.Json;
using OverWatchELD.Models;

namespace OverWatchELD.Services
{
    public sealed class AppSettingsService
    {
        private readonly string _settingsPath;

        public AppSettingsService()
        {
            var baseDir = AppPaths.ConfigFolder();
            _settingsPath = Path.Combine(baseDir, "settings.json");

            try
            {
                if (!File.Exists(_settingsPath))
                {
                    var legacyCandidates = new[]
                    {
                        Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "OverWatchELD",
                            "settings.json"),

                        Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "ATS_ELD",
                            "settings.json"),

                        Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            "OverWatchELD",
                            "settings.json"),

                        Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            "ATS_ELD",
                            "settings.json"),

                        Path.Combine(AppContext.BaseDirectory, "settings.json")
                    };

                    var oldPath = Array.Find(legacyCandidates, File.Exists);
                    if (!string.IsNullOrWhiteSpace(oldPath))
                        File.Copy(oldPath, _settingsPath, overwrite: false);
                }
            }
            catch { }
        }

        public AppSettings Load()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                    return new AppSettings();

                var json = File.ReadAllText(_settingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                var s = settings ?? new AppSettings();

                try
                {
                    if (!string.IsNullOrWhiteSpace(s.DiscordWebhookUrl) &&
                        (s.Discord == null || string.IsNullOrWhiteSpace(s.Discord.ExportWebhookUrl)))
                    {
                        s.Discord ??= new DiscordSettings();
                        s.Discord.ExportWebhookUrl = s.DiscordWebhookUrl.Trim();
                    }
                }
                catch { }

                return s;
            }
            catch
            {
                return new AppSettings();
            }
        }

        public void Save(AppSettings settings)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);

                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(_settingsPath, json);
            }
            catch
            {
            }
        }

        public string GetSettingsPath() => _settingsPath;
    }
}