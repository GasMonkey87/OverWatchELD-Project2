using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public static class VtcLinkDirectoryService
    {
        private static readonly object _lock = new();
        private static readonly JsonSerializerOptions _opts = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

        private static string PathFile =>
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vtc_links.json");

        public sealed class LinkEntry
        {
            public string DiscordUserId { get; set; } = "";
            public string DiscordUserName { get; set; } = "";
            public string DriverKey { get; set; } = "";
            public string DriverName { get; set; } = "";
            public DateTimeOffset LinkedUtc { get; set; } = DateTimeOffset.UtcNow;
        }

        public static void Upsert(string discordUserId, string discordUserName, string driverKey, string driverName)
        {
            discordUserId = (discordUserId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(discordUserId)) return;

            lock (_lock)
            {
                var all = LoadAllInternal();
                var ex = all.FirstOrDefault(x => string.Equals(x.DiscordUserId, discordUserId, StringComparison.Ordinal));
                if (ex == null)
                {
                    all.Add(new LinkEntry
                    {
                        DiscordUserId = discordUserId,
                        DiscordUserName = (discordUserName ?? "").Trim(),
                        DriverKey = (driverKey ?? "").Trim(),
                        DriverName = (driverName ?? "").Trim(),
                        LinkedUtc = DateTimeOffset.UtcNow
                    });
                }
                else
                {
                    ex.DiscordUserName = (discordUserName ?? ex.DiscordUserName).Trim();
                    ex.DriverKey = (driverKey ?? ex.DriverKey).Trim();
                    ex.DriverName = (driverName ?? ex.DriverName).Trim();
                    ex.LinkedUtc = DateTimeOffset.UtcNow;
                }

                SaveAllInternal(all);
            }
        }

        public static LinkEntry? FindByDiscordId(string discordUserId)
        {
            discordUserId = (discordUserId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(discordUserId)) return null;

            lock (_lock)
            {
                var all = LoadAllInternal();
                return all.FirstOrDefault(x => string.Equals(x.DiscordUserId, discordUserId, StringComparison.Ordinal));
            }
        }

        private static List<LinkEntry> LoadAllInternal()
        {
            try
            {
                if (!File.Exists(PathFile)) return new List<LinkEntry>();
                return JsonSerializer.Deserialize<List<LinkEntry>>(File.ReadAllText(PathFile), _opts) ?? new List<LinkEntry>();
            }
            catch { return new List<LinkEntry>(); }
        }

        private static void SaveAllInternal(List<LinkEntry> all)
        {
            try { File.WriteAllText(PathFile, JsonSerializer.Serialize(all, _opts)); } catch { }
        }
    }
}