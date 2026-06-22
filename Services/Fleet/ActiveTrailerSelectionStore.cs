using System;
using System.IO;
using System.Text.Json;

namespace OverWatchELD.Services.Fleet
{
    public sealed class ActiveTrailerSelection
    {
        public string DriverKey { get; set; } = "";
        public string TrailerId { get; set; } = "";
        public string TrailerNumber { get; set; } = "";
        public string TrailerName { get; set; } = "";
        public string Model { get; set; } = "";
        public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    public static class ActiveTrailerSelectionStore
    {
        private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

        public static void SaveForCurrentDriver(ActiveTrailerSelection selection)
        {
            if (selection == null) return;
            var key = GetCurrentDriverKey();
            if (string.IsNullOrWhiteSpace(key)) key = "local-driver";
            selection.DriverKey = key;
            selection.UpdatedUtc = DateTimeOffset.UtcNow;
            File.WriteAllText(GetPath(key), JsonSerializer.Serialize(selection, JsonOpts));
        }

        public static ActiveTrailerSelection? LoadForCurrentDriver()
        {
            try
            {
                var key = GetCurrentDriverKey();
                if (string.IsNullOrWhiteSpace(key)) key = "local-driver";
                var path = GetPath(key);
                if (!File.Exists(path)) return null;
                return JsonSerializer.Deserialize<ActiveTrailerSelection>(File.ReadAllText(path), JsonOpts);
            }
            catch { return null; }
        }

        private static string GetPath(string driverKey)
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OverWatchELD", "active-trailers");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, Sanitize(driverKey) + ".json");
        }

        private static string GetCurrentDriverKey()
        {
            try
            {
                var app = System.Windows.Application.Current as OverWatchELD.App;
                var session = app?.Session;
                if (session == null) return "local-driver";
                var type = session.GetType();
                foreach (var name in new[] { "DiscordUserId", "DriverDiscordUserId", "LinkedDiscordUserId", "UserId", "DiscordId", "DriverName" })
                {
                    var prop = type.GetProperty(name);
                    var value = prop?.GetValue(session)?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
            }
            catch { }
            return "local-driver";
        }

        private static string Sanitize(string value)
        {
            var chars = Path.GetInvalidFileNameChars();
            foreach (var c in chars) value = value.Replace(c, '_');
            return string.IsNullOrWhiteSpace(value) ? "local-driver" : value.Trim();
        }
    }
}
