using OverWatchELD.Models;
using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace OverWatchELD.Services.Identity
{
    /// <summary>
    /// Resolves the currently signed-in driver's permanent identity.
    /// DiscordUserId is the authoritative key when available; display names are treated as labels only.
    /// </summary>
    public static class DriverIdentityContextService
    {
        public static DriverIdentityMetadata Current(string? fallbackDriverName = null)
        {
            var session = TryGetSession();
            var driverName = FirstNonBlank(
                fallbackDriverName,
                GetProp(session, "DriverName"),
                EldDriverIdentityResolver.DriverName(),
                Environment.UserName,
                "Driver");

            var discordId = FirstNonBlank(
                GetProp(session, "DiscordUserId"),
                GetProp(session, "DiscordId"),
                FromJsonStore("DiscordUserId"),
                FromJsonStore("DiscordId"));

            var discordUsername = FirstNonBlank(
                GetProp(session, "DiscordUsername"),
                GetProp(session, "DiscordName"),
                FromJsonStore("DiscordUsername"),
                FromJsonStore("DiscordName"));

            var tmpId = FirstNonBlank(
                GetProp(session, "TruckersMpId"),
                GetProp(session, "TruckersMPId"),
                FromJsonStore("TruckersMpId"),
                FromJsonStore("TruckersMPId"));

            var deviceId = GetStableDeviceId();
            var hash = BuildIdentityHash(discordId, tmpId, deviceId, driverName);

            return new DriverIdentityMetadata
            {
                DriverName = driverName.Trim(),
                DiscordUserId = discordId.Trim(),
                DiscordUsername = discordUsername.Trim(),
                TruckersMpId = tmpId.Trim(),
                DeviceId = deviceId,
                IdentityHash = hash,
                CapturedUtc = DateTimeOffset.UtcNow
            };
        }

        public static string PermanentDriverKey(string? fallbackDriverName = null)
            => Current(fallbackDriverName).PermanentDriverKey;

        public static void ApplyToSession(AppSession session, DriverIdentityMetadata identity)
        {
            if (session == null || identity == null) return;
            session.DriverName = FirstNonBlank(identity.DriverName, session.DriverName, "Driver");
            session.DiscordUserId = identity.DiscordUserId;
            session.DiscordUsername = identity.DiscordUsername;
            session.TruckersMpId = identity.TruckersMpId;
            session.DeviceId = identity.DeviceId;
            session.IdentityHash = identity.IdentityHash;
        }

        public static string BuildIdentityHash(string? discordUserId, string? truckersMpId, string? deviceId, string? driverName)
        {
            var raw = string.Join("|",
                Clean(discordUserId),
                Clean(truckersMpId),
                Clean(deviceId),
                Clean(driverName).ToLowerInvariant());

            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private static object? TryGetSession()
        {
            try { return (Application.Current as OverWatchELD.App)?.Session; }
            catch { return null; }
        }

        private static string GetProp(object? obj, string name)
        {
            try
            {
                if (obj == null) return "";
                return obj.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)
                    ?.GetValue(obj)?.ToString()?.Trim() ?? "";
            }
            catch { return ""; }
        }

        private static string FromJsonStore(string propertyName)
        {
            foreach (var file in CandidateIdentityFiles())
            {
                try
                {
                    if (!File.Exists(file)) continue;
                    using var doc = JsonDocument.Parse(File.ReadAllText(file));
                    if (TryFindProperty(doc.RootElement, propertyName, out var value))
                        return value;
                }
                catch { }
            }
            return "";
        }

        private static bool TryFindProperty(JsonElement element, string propertyName, out string value)
        {
            value = "";
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in element.EnumerateObject())
                {
                    if (string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        value = p.Value.ToString().Trim();
                        return !string.IsNullOrWhiteSpace(value);
                    }
                    if (TryFindProperty(p.Value, propertyName, out value)) return true;
                }
            }
            return false;
        }

        private static string[] CandidateIdentityFiles()
        {
            var docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OverWatchELD");
            var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OverWatchELD");
            return new[]
            {
                AppPaths.FileInConfig("session.json"),
                AppPaths.FileInConfig("discord_identity.json"),
                AppPaths.FileInConfig("vtc_pairing.json"),
                Path.Combine(docs, "driver_identity.json"),
                Path.Combine(docs, "discord_identity.json"),
                Path.Combine(local, "driver_identity.json")
            };
        }

        private static string GetStableDeviceId()
        {
            try
            {
                var path = AppPaths.FileInConfig("device_identity.txt");
                if (File.Exists(path))
                {
                    var existing = File.ReadAllText(path).Trim();
                    if (!string.IsNullOrWhiteSpace(existing)) return existing;
                }
                var id = Guid.NewGuid().ToString("N");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, id);
                return id;
            }
            catch { return Environment.MachineName; }
        }

        private static string FirstNonBlank(params string?[] values)
        {
            foreach (var v in values)
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            return "";
        }

        private static string Clean(string? value) => (value ?? "").Trim();
    }
}
