using System;
using System.IO;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public sealed class VtcPairingStore
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public sealed class Pairing
        {
            public string GuildId { get; set; } = "";
            public string VtcName { get; set; } = "";
            public string DiscordUserId { get; set; } = "";
            public string DiscordUsername { get; set; } = "";
            public DateTimeOffset PairedUtc { get; set; } = DateTimeOffset.UtcNow;

            public bool IsLinked => !string.IsNullOrWhiteSpace(GuildId);
        }

        public static string StorePath => AppPaths.FileInConfig("vtc_pairing.json");

        public static Pairing? Load()
        {
            try
            {
                TryMigrateLegacyFile();

                if (!File.Exists(StorePath))
                    return null;

                var json = File.ReadAllText(StorePath);
                var data = JsonSerializer.Deserialize<Pairing>(json, JsonOpts);
                if (data == null)
                    return null;

                data.GuildId ??= "";
                data.VtcName ??= "";
                data.DiscordUserId ??= "";
                data.DiscordUsername ??= "";

                return data;
            }
            catch
            {
                return null;
            }
        }

        public static void Save(Pairing p)
        {
            try
            {
                if (p == null)
                    return;

                var folder = Path.GetDirectoryName(StorePath);
                if (!string.IsNullOrWhiteSpace(folder))
                    Directory.CreateDirectory(folder);

                p.GuildId = (p.GuildId ?? "").Trim();
                p.VtcName = (p.VtcName ?? "").Trim();
                p.DiscordUserId = (p.DiscordUserId ?? "").Trim();
                p.DiscordUsername = (p.DiscordUsername ?? "").Trim();

                if (p.PairedUtc == default)
                    p.PairedUtc = DateTimeOffset.UtcNow;

                var json = JsonSerializer.Serialize(p, JsonOpts);
                File.WriteAllText(StorePath, json);
            }
            catch { }
        }

        public static void Clear()
        {
            try
            {
                if (File.Exists(StorePath))
                    File.Delete(StorePath);
            }
            catch { }
        }

        private static void TryMigrateLegacyFile()
        {
            try
            {
                if (File.Exists(StorePath))
                    return;

                var candidates = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OverWatchELD", "vtc_pairing.json"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ATS_ELD", "vtc_pairing.json"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OverWatchELD", "vtc_pairing.json"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ATS_ELD", "vtc_pairing.json")
                };

                var src = Array.Find(candidates, File.Exists);
                if (!string.IsNullOrWhiteSpace(src))
                    File.Copy(src, StorePath, overwrite: false);
            }
            catch { }
        }
    }
}