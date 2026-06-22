using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public sealed class DiscordDriverNameLock
    {
        public string GuildId { get; set; } = "";
        public string DiscordUserId { get; set; } = "";
        public string LockedDriverName { get; set; } = "";
        public string FirstSeenDiscordUsername { get; set; } = "";
        public string LastSeenDiscordUsername { get; set; } = "";
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    public sealed class DiscordDriverNameLockResult
    {
        public bool Allowed { get; set; }
        public string Reason { get; set; } = "";
        public DiscordDriverNameLock? Lock { get; set; }
        public string RequestedName { get; set; } = "";
        public string DiscordUserId { get; set; } = "";
    }

    public static class DiscordDriverNameLockStore
    {
        private static readonly object _sync = new();
        private static readonly JsonSerializerOptions _json = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private static string StorePath => AppPaths.FileInConfig("discord_driver_name_locks.json");

        public static DiscordDriverNameLockResult Register(string? discordUserId, string? requestedDriverName, string? guildId = null, string? discordUsername = null)
        {
            lock (_sync)
            {
                var uid = Clean(discordUserId);
                var name = Clean(requestedDriverName);
                var gid = Clean(guildId);
                var username = Clean(discordUsername);

                if (string.IsNullOrWhiteSpace(uid))
                {
                    return new DiscordDriverNameLockResult
                    {
                        Allowed = false,
                        Reason = "Discord ID is required before the companion app can sign in.",
                        DiscordUserId = uid,
                        RequestedName = name
                    };
                }

                if (string.IsNullOrWhiteSpace(name))
                    name = FirstNonBlank(username, uid);

                var all = LoadUnsafe();
                var existing = all.FirstOrDefault(x => SameId(x.DiscordUserId, uid) && SameGuild(x.GuildId, gid));

                if (existing == null)
                {
                    existing = new DiscordDriverNameLock
                    {
                        GuildId = gid,
                        DiscordUserId = uid,
                        LockedDriverName = name,
                        FirstSeenDiscordUsername = username,
                        LastSeenDiscordUsername = username,
                        CreatedUtc = DateTimeOffset.UtcNow,
                        UpdatedUtc = DateTimeOffset.UtcNow
                    };
                    all.Add(existing);
                    SaveUnsafe(all);
                    return Allowed(existing, name, uid, "Discord ID locked to driver name.");
                }

                if (!SameName(existing.LockedDriverName, name))
                {
                    return new DiscordDriverNameLockResult
                    {
                        Allowed = false,
                        Reason = $"Discord ID {uid} is already locked to '{existing.LockedDriverName}'. It cannot also sign in as '{name}'.",
                        Lock = existing,
                        RequestedName = name,
                        DiscordUserId = uid
                    };
                }

                existing.LastSeenDiscordUsername = username;
                existing.UpdatedUtc = DateTimeOffset.UtcNow;
                SaveUnsafe(all);
                return Allowed(existing, name, uid, "Discord ID matches locked driver name.");
            }
        }

        public static DiscordDriverNameLockResult GetStatus(string? discordUserId, string? guildId = null, string? requestedDriverName = null)
        {
            lock (_sync)
            {
                var uid = Clean(discordUserId);
                var gid = Clean(guildId);
                var name = Clean(requestedDriverName);
                var existing = LoadUnsafe().FirstOrDefault(x => SameId(x.DiscordUserId, uid) && SameGuild(x.GuildId, gid));

                if (existing == null)
                {
                    return new DiscordDriverNameLockResult
                    {
                        Allowed = true,
                        Reason = string.IsNullOrWhiteSpace(uid) ? "No Discord ID detected yet." : "Discord ID is not locked yet.",
                        DiscordUserId = uid,
                        RequestedName = name
                    };
                }

                var allowed = string.IsNullOrWhiteSpace(name) || SameName(existing.LockedDriverName, name);
                return new DiscordDriverNameLockResult
                {
                    Allowed = allowed,
                    Reason = allowed
                        ? "Discord ID matches locked driver name."
                        : $"Discord ID {uid} is already locked to '{existing.LockedDriverName}'. It cannot also sign in as '{name}'.",
                    Lock = existing,
                    RequestedName = name,
                    DiscordUserId = uid
                };
            }
        }

        public static List<DiscordDriverNameLock> LoadAll()
        {
            lock (_sync)
                return LoadUnsafe();
        }

        private static DiscordDriverNameLockResult Allowed(DiscordDriverNameLock locked, string requestedName, string uid, string reason)
        {
            return new DiscordDriverNameLockResult
            {
                Allowed = true,
                Reason = reason,
                Lock = locked,
                RequestedName = requestedName,
                DiscordUserId = uid
            };
        }

        private static List<DiscordDriverNameLock> LoadUnsafe()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath) ?? AppContext.BaseDirectory);
                if (!File.Exists(StorePath))
                    return new List<DiscordDriverNameLock>();

                var json = File.ReadAllText(StorePath);
                return JsonSerializer.Deserialize<List<DiscordDriverNameLock>>(json, _json) ?? new List<DiscordDriverNameLock>();
            }
            catch
            {
                return new List<DiscordDriverNameLock>();
            }
        }

        private static void SaveUnsafe(List<DiscordDriverNameLock> locks)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath) ?? AppContext.BaseDirectory);
            var json = JsonSerializer.Serialize(locks
                .Where(x => !string.IsNullOrWhiteSpace(x.DiscordUserId))
                .OrderBy(x => x.GuildId)
                .ThenBy(x => x.LockedDriverName)
                .ToList(), _json);
            File.WriteAllText(StorePath, json);
        }

        private static string Clean(string? value) => (value ?? "").Trim();
        private static string FirstNonBlank(params string[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";
        private static bool SameId(string? a, string? b) => string.Equals(Clean(a), Clean(b), StringComparison.OrdinalIgnoreCase);
        private static bool SameGuild(string? a, string? b) => string.Equals(Clean(a), Clean(b), StringComparison.OrdinalIgnoreCase);

        private static bool SameName(string? a, string? b)
        {
            static string Norm(string? s)
            {
                s = (s ?? "").Trim();
                if (s.StartsWith("@", StringComparison.Ordinal)) s = s.Substring(1);
                return s.ToLowerInvariant();
            }
            return string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);
        }
    }
}
