using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public sealed class DiscordIdentity
    {
        public string GuildId { get; set; } = "";
        public string DiscordUserId { get; set; } = "";
        public string DiscordUsername { get; set; } = "";
        public string LastPairCode { get; set; } = "";
        public string VtcName { get; set; } = "";
        public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    public static class DiscordIdentityStore
    {
        private static readonly object _lock = new();

        private static readonly JsonSerializerOptions _opts = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private static string FolderPath => AppPaths.ConfigFolder();

        private static string FilePath => Path.Combine(FolderPath, "discord_identity.json");
        private static string ListFilePath => Path.Combine(FolderPath, "discord_identities.json");

        public static DiscordIdentity Load()
        {
            lock (_lock)
            {
                try
                {
                    Directory.CreateDirectory(FolderPath);
                    TryMigrateLegacyFiles();

                    if (!File.Exists(FilePath))
                        return new DiscordIdentity();

                    var json = File.ReadAllText(FilePath);
                    return JsonSerializer.Deserialize<DiscordIdentity>(json, _opts) ?? new DiscordIdentity();
                }
                catch
                {
                    return new DiscordIdentity();
                }
            }
        }

        public static void Save(DiscordIdentity identity)
        {
            lock (_lock)
            {
                try
                {
                    Directory.CreateDirectory(FolderPath);

                    identity ??= new DiscordIdentity();
                    identity.UpdatedUtc = DateTimeOffset.UtcNow;

                    var json = JsonSerializer.Serialize(identity, _opts);
                    File.WriteAllText(FilePath, json);

                    UpsertInternal(identity);
                }
                catch
                {
                }
            }
        }

        public static List<DiscordIdentity> LoadAll()
        {
            lock (_lock)
            {
                try
                {
                    Directory.CreateDirectory(FolderPath);
                    TryMigrateLegacyFiles();

                    var results = new List<DiscordIdentity>();

                    if (File.Exists(ListFilePath))
                    {
                        var json = File.ReadAllText(ListFilePath);
                        var items = JsonSerializer.Deserialize<List<DiscordIdentity>>(json, _opts);
                        if (items != null)
                            results.AddRange(items);
                    }

                    if (File.Exists(FilePath))
                    {
                        var json = File.ReadAllText(FilePath);
                        var single = JsonSerializer.Deserialize<DiscordIdentity>(json, _opts);
                        if (single != null &&
                            (!string.IsNullOrWhiteSpace(single.DiscordUserId) || !string.IsNullOrWhiteSpace(single.DiscordUsername)))
                        {
                            if (!results.Any(x =>
                                string.Equals(x.DiscordUserId, single.DiscordUserId, StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(x.GuildId, single.GuildId, StringComparison.OrdinalIgnoreCase)))
                            {
                                results.Add(single);
                            }
                        }
                    }

                    return results
                        .Where(x => !string.IsNullOrWhiteSpace(x.DiscordUserId) || !string.IsNullOrWhiteSpace(x.DiscordUsername))
                        .OrderByDescending(x => x.UpdatedUtc)
                        .ThenBy(x => x.DiscordUsername)
                        .ToList();
                }
                catch
                {
                    return new List<DiscordIdentity>();
                }
            }
        }

        public static void Upsert(DiscordIdentity identity)
        {
            lock (_lock)
            {
                try
                {
                    Directory.CreateDirectory(FolderPath);

                    identity ??= new DiscordIdentity();
                    identity.UpdatedUtc = DateTimeOffset.UtcNow;

                    var json = JsonSerializer.Serialize(identity, _opts);
                    File.WriteAllText(FilePath, json);

                    UpsertInternal(identity);
                }
                catch
                {
                }
            }
        }

        private static void UpsertInternal(DiscordIdentity identity)
        {
            List<DiscordIdentity> items;

            if (File.Exists(ListFilePath))
            {
                try
                {
                    var json = File.ReadAllText(ListFilePath);
                    items = JsonSerializer.Deserialize<List<DiscordIdentity>>(json, _opts) ?? new List<DiscordIdentity>();
                }
                catch
                {
                    items = new List<DiscordIdentity>();
                }
            }
            else
            {
                items = new List<DiscordIdentity>();
            }

            var existing = items.FirstOrDefault(x =>
                string.Equals(x.DiscordUserId, identity.DiscordUserId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.GuildId, identity.GuildId, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                items.Add(identity);
            }
            else
            {
                existing.GuildId = identity.GuildId ?? existing.GuildId;
                existing.DiscordUserId = identity.DiscordUserId ?? existing.DiscordUserId;
                existing.DiscordUsername = identity.DiscordUsername ?? existing.DiscordUsername;
                existing.LastPairCode = identity.LastPairCode ?? existing.LastPairCode;
                existing.VtcName = identity.VtcName ?? existing.VtcName;
                existing.UpdatedUtc = DateTimeOffset.UtcNow;
            }

            items = items
                .Where(x => !string.IsNullOrWhiteSpace(x.DiscordUserId) || !string.IsNullOrWhiteSpace(x.DiscordUsername))
                .OrderByDescending(x => x.UpdatedUtc)
                .ThenBy(x => x.DiscordUsername)
                .ToList();

            var outJson = JsonSerializer.Serialize(items, _opts);
            File.WriteAllText(ListFilePath, outJson);
        }

        private static void TryMigrateLegacyFiles()
        {
            TryMigrateOne("discord_identity.json");
            TryMigrateOne("discord_identities.json");
        }

        private static void TryMigrateOne(string fileName)
        {
            try
            {
                var target = Path.Combine(FolderPath, fileName);
                if (File.Exists(target))
                    return;

                var candidates = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OverWatchELD", fileName),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ATS_ELD", fileName),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OverWatchELD", fileName),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ATS_ELD", fileName)
                };

                var src = Array.Find(candidates, File.Exists);
                if (!string.IsNullOrWhiteSpace(src))
                    File.Copy(src, target, overwrite: false);
            }
            catch { }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(FilePath))
                        File.Delete(FilePath);

                    if (File.Exists(ListFilePath))
                        File.Delete(ListFilePath);
                }
                catch { }
            }
        }

        public static string GetPathForDebug() => FilePath;
        public static string GetListPathForDebug() => ListFilePath;
    }
}