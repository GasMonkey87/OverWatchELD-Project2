using System;
using System.IO;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public static class OnlinePresenceService
    {
        private static readonly string Dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OverWatchELD");

        private static readonly string FilePath =
            Path.Combine(Dir, "presence.json");

        public static bool IsOnline { get; private set; } = true;

        public static event Action<bool>? OnlineChanged;

        public static bool Load()
        {
            try
            {
                Directory.CreateDirectory(Dir);

                if (!File.Exists(FilePath))
                {
                    Save(true);
                    return true;
                }

                var json = File.ReadAllText(FilePath);
                var model = JsonSerializer.Deserialize<PresenceModel>(json);

                IsOnline = model?.IsOnline ?? true;
                return IsOnline;
            }
            catch
            {
                IsOnline = true;
                return true;
            }
        }

        public static void Save(bool isOnline)
        {
            try
            {
                Directory.CreateDirectory(Dir);

                IsOnline = isOnline;

                var json = JsonSerializer.Serialize(
                    new PresenceModel
                    {
                        IsOnline = isOnline,
                        UpdatedUtc = DateTime.UtcNow
                    },
                    new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(FilePath, json);

                OnlineChanged?.Invoke(isOnline);
            }
            catch
            {
                IsOnline = isOnline;
                OnlineChanged?.Invoke(isOnline);
            }
        }

        private sealed class PresenceModel
        {
            public bool IsOnline { get; set; } = true;
            public DateTime UpdatedUtc { get; set; }
        }
    }
}