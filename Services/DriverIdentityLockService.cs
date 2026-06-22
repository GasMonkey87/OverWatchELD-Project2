using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public sealed class DriverIdentityLock
    {
        public string DiscordUserId { get; set; } = "";
        public string DiscordUsername { get; set; } = "";
        public string DriverName { get; set; } = "";
        public string GuildId { get; set; } = "";
        public string VtcName { get; set; } = "";
        public string TruckersMpId { get; set; } = "";
        public string DeviceId { get; set; } = "";
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
        public string CreatedBy { get; set; } = "ELD Login";
        public string Notes { get; set; } = "";
    }

    public sealed class DriverIdentityAuditEntry
    {
        public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
        public string Action { get; set; } = "";
        public string Result { get; set; } = "";
        public string DiscordUserId { get; set; } = "";
        public string DiscordUsername { get; set; } = "";
        public string RequestedDriverName { get; set; } = "";
        public string ExistingDriverName { get; set; } = "";
        public string GuildId { get; set; } = "";
        public string VtcName { get; set; } = "";
        public string Reason { get; set; } = "";
    }

    public sealed class DriverIdentityLockResult
    {
        public bool Allowed { get; set; }
        public string Message { get; set; } = "";
        public DriverIdentityLock? Lock { get; set; }
    }

    /// <summary>
    /// Server-authoritative local ELD identity lock.
    /// One Discord user ID may only operate under one ELD driver name unless a fleet manager explicitly renames/transfers/unlocks it.
    /// </summary>
    public static class DriverIdentityLockService
    {
        private static readonly object Sync = new();
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private static string LocksPath => AppPaths.FileInConfig("driver_identity_locks.json");
        private static string AuditPath => AppPaths.FileInConfig("driver_identity_audit.jsonl");

        public static DriverIdentityLockResult Enforce(string discordUserId, string discordUsername, string requestedDriverName, string guildId, string vtcName, string truckersMpId = "", string deviceId = "")
        {
            discordUserId = Clean(discordUserId);
            discordUsername = Clean(discordUsername);
            requestedDriverName = Clean(requestedDriverName);
            guildId = Clean(guildId);
            vtcName = Clean(vtcName);
            truckersMpId = Clean(truckersMpId);
            deviceId = Clean(deviceId);

            if (string.IsNullOrWhiteSpace(discordUserId))
                return new DriverIdentityLockResult { Allowed = true, Message = "No Discord ID supplied; identity lock skipped." };

            if (string.IsNullOrWhiteSpace(requestedDriverName))
                requestedDriverName = string.IsNullOrWhiteSpace(discordUsername) ? "Driver" : discordUsername;

            lock (Sync)
            {
                var locks = LoadAllInternal();
                var existing = locks.FirstOrDefault(x => Same(x.DiscordUserId, discordUserId));

                if (existing == null)
                {
                    var created = new DriverIdentityLock
                    {
                        DiscordUserId = discordUserId,
                        DiscordUsername = discordUsername,
                        DriverName = requestedDriverName,
                        GuildId = guildId,
                        VtcName = vtcName,
                        TruckersMpId = truckersMpId,
                        DeviceId = deviceId,
                        CreatedUtc = DateTimeOffset.UtcNow,
                        UpdatedUtc = DateTimeOffset.UtcNow,
                        CreatedBy = "ELD Login"
                    };
                    locks.Add(created);
                    SaveAllInternal(locks);
                    Audit("LOCK_CREATED", "ALLOWED", discordUserId, discordUsername, requestedDriverName, "", guildId, vtcName, "Created first identity lock for Discord ID.");
                    return new DriverIdentityLockResult { Allowed = true, Message = $"Driver identity locked to {requestedDriverName}.", Lock = created };
                }

                if (!Same(existing.DriverName, requestedDriverName))
                {
                    Audit("LOGIN_BLOCKED", "BLOCKED", discordUserId, discordUsername, requestedDriverName, existing.DriverName, guildId, vtcName,
                        $"Discord ID is already registered to {existing.DriverName}.");

                    return new DriverIdentityLockResult
                    {
                        Allowed = false,
                        Lock = existing,
                        Message = $"This Discord account is already registered to {existing.DriverName}. You cannot sign in as {requestedDriverName}. Contact fleet management to rename, transfer, or unlock the identity."
                    };
                }

                existing.DiscordUsername = string.IsNullOrWhiteSpace(discordUsername) ? existing.DiscordUsername : discordUsername;
                existing.GuildId = string.IsNullOrWhiteSpace(guildId) ? existing.GuildId : guildId;
                existing.VtcName = string.IsNullOrWhiteSpace(vtcName) ? existing.VtcName : vtcName;
                existing.TruckersMpId = string.IsNullOrWhiteSpace(truckersMpId) ? existing.TruckersMpId : truckersMpId;
                existing.DeviceId = string.IsNullOrWhiteSpace(deviceId) ? existing.DeviceId : deviceId;
                existing.UpdatedUtc = DateTimeOffset.UtcNow;
                SaveAllInternal(locks);
                Audit("LOGIN_ALLOWED", "ALLOWED", discordUserId, discordUsername, requestedDriverName, existing.DriverName, guildId, vtcName, "Identity matched existing lock.");
                return new DriverIdentityLockResult { Allowed = true, Message = $"Identity verified for {existing.DriverName}.", Lock = existing };
            }
        }

        public static IReadOnlyList<DriverIdentityLock> LoadAll()
        {
            lock (Sync) return LoadAllInternal().OrderBy(x => x.DriverName).ToList();
        }

        public static IReadOnlyList<DriverIdentityAuditEntry> LoadAudit(int max = 250)
        {
            lock (Sync)
            {
                try
                {
                    if (!File.Exists(AuditPath)) return Array.Empty<DriverIdentityAuditEntry>();
                    return File.ReadLines(AuditPath)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => JsonSerializer.Deserialize<DriverIdentityAuditEntry>(x, JsonOptions))
                        .Where(x => x != null)
                        .Cast<DriverIdentityAuditEntry>()
                        .Reverse()
                        .Take(Math.Max(1, max))
                        .ToList();
                }
                catch { return Array.Empty<DriverIdentityAuditEntry>(); }
            }
        }

        public static bool Rename(string discordUserId, string newDriverName, string actor = "Fleet Manager")
        {
            discordUserId = Clean(discordUserId);
            newDriverName = Clean(newDriverName);
            if (string.IsNullOrWhiteSpace(discordUserId) || string.IsNullOrWhiteSpace(newDriverName)) return false;

            lock (Sync)
            {
                var locks = LoadAllInternal();
                var item = locks.FirstOrDefault(x => Same(x.DiscordUserId, discordUserId));
                if (item == null) return false;
                var old = item.DriverName;
                item.DriverName = newDriverName;
                item.UpdatedUtc = DateTimeOffset.UtcNow;
                item.Notes = $"Renamed by {actor} from {old} to {newDriverName}.";
                SaveAllInternal(locks);
                Audit("RENAME", "ALLOWED", item.DiscordUserId, item.DiscordUsername, newDriverName, old, item.GuildId, item.VtcName, item.Notes);
                return true;
            }
        }

        public static bool Unlock(string discordUserId, string actor = "Fleet Manager")
        {
            discordUserId = Clean(discordUserId);
            if (string.IsNullOrWhiteSpace(discordUserId)) return false;

            lock (Sync)
            {
                var locks = LoadAllInternal();
                var item = locks.FirstOrDefault(x => Same(x.DiscordUserId, discordUserId));
                if (item == null) return false;
                locks.Remove(item);
                SaveAllInternal(locks);
                Audit("UNLOCK", "ALLOWED", item.DiscordUserId, item.DiscordUsername, "", item.DriverName, item.GuildId, item.VtcName, $"Unlocked by {actor}.");
                return true;
            }
        }

        private static List<DriverIdentityLock> LoadAllInternal()
        {
            try
            {
                var dir = Path.GetDirectoryName(LocksPath);
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                if (!File.Exists(LocksPath)) return new List<DriverIdentityLock>();
                var json = File.ReadAllText(LocksPath);
                return JsonSerializer.Deserialize<List<DriverIdentityLock>>(json, JsonOptions) ?? new List<DriverIdentityLock>();
            }
            catch { return new List<DriverIdentityLock>(); }
        }

        private static void SaveAllInternal(List<DriverIdentityLock> locks)
        {
            var dir = Path.GetDirectoryName(LocksPath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(LocksPath, JsonSerializer.Serialize(locks, JsonOptions));
        }

        private static void Audit(string action, string result, string discordUserId, string discordUsername, string requestedDriverName, string existingDriverName, string guildId, string vtcName, string reason)
        {
            try
            {
                var dir = Path.GetDirectoryName(AuditPath);
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                var row = new DriverIdentityAuditEntry
                {
                    Action = action,
                    Result = result,
                    DiscordUserId = discordUserId,
                    DiscordUsername = discordUsername,
                    RequestedDriverName = requestedDriverName,
                    ExistingDriverName = existingDriverName,
                    GuildId = guildId,
                    VtcName = vtcName,
                    Reason = reason
                };
                File.AppendAllText(AuditPath, JsonSerializer.Serialize(row, JsonOptions).Replace("\r", "").Replace("\n", "") + Environment.NewLine);
            }
            catch { }
        }

        private static bool Same(string? a, string? b) => string.Equals(Clean(a), Clean(b), StringComparison.OrdinalIgnoreCase);
        private static string Clean(string? value) => (value ?? "").Trim();
    }
}
