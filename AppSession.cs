using System;
using System.IO;
using System.Text.Json;
using OverWatchELD.Services;

namespace OverWatchELD
{
    public sealed class AppSession
    {
        public string DriverName { get; set; } = "User";
        public string? VtcProvider { get; set; } = "Discord";
        public string? DiscordUserId { get; set; }
        public string? DiscordUsername { get; set; }
        public string? TruckersMpId { get; set; }
        public string? DeviceId { get; set; }
        public string? IdentityHash { get; set; }


        private static string FilePath => AppPaths.FileInConfig("session.json");

        public static AppSession LoadOrCreate()
        {
            try
            {
                TryMigrateLegacyFile();

                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var s = JsonSerializer.Deserialize<AppSession>(json);
                    if (s != null) return s;
                }
            }
            catch { }

            return new AppSession();
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch { }
        }

        private static void TryMigrateLegacyFile()
        {
            try
            {
                if (File.Exists(FilePath))
                    return;

                var candidates = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ATS_ELD", "session.json"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OverWatchELD", "session.json"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ATS_ELD", "session.json"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OverWatchELD", "session.json")
                };

                var src = Array.Find(candidates, File.Exists);
                if (!string.IsNullOrWhiteSpace(src))
                    File.Copy(src, FilePath, overwrite: false);
            }
            catch { }
        }
    }
}