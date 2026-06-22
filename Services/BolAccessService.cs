using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OverWatchELD.Models;

namespace OverWatchELD.Services
{
    public static class BolAccessService
    {
        public static bool CanViewAllBols()
        {
            var identity = DriverProfileIdentitySnapshot.Current();
            var roleText = GetCurrentRoleText();

            if (HasPrivilegedRole(roleText))
                return true;

            if (AdminAccessService.CanManageDiscordSettings(identity.DiscordUserId, roleText, SplitRoles(roleText)))
                return true;

            return false;
        }

        public static bool CanSendBolToDiscord() => CanViewAllBols();

        public static bool CanCurrentUserAccess(BolStore.BolRecord? record)
        {
            if (record == null)
                return false;

            if (CanViewAllBols())
                return true;

            var identity = DriverProfileIdentitySnapshot.Current();
            return IsOwnBol(record, identity);
        }

        public static bool IsOwnBol(BolStore.BolRecord? record, DriverProfileIdentitySnapshot? identity = null)
        {
            if (record == null)
                return false;

            identity ??= DriverProfileIdentitySnapshot.Current();

            var currentDriverId = GetStableDriverId(identity);
            var recordDriverId = FirstNonBlank(
                record.DriverId,
                DriverProfile.BuildStableDriverId(record.DriverDiscordUserId, record.DriverName, record.DriverDiscordName));

            // New permission rule: match the stable DriverId, not display name text.
            if (SameNonBlank(recordDriverId, currentDriverId))
                return true;

            // Backward compatibility only for old BOLs that were saved before DriverId existed.
            // Once the BOL is opened/saved, StampCurrentDriver backfills DriverId.
            if (string.IsNullOrWhiteSpace(record.DriverId))
            {
                return SameNonBlank(record.DriverDiscordUserId, identity.DiscordUserId)
                    || SameNonBlank(record.DriverDiscordName, identity.DiscordName);
            }

            return false;
        }

        public static string GetCurrentDriverId()
        {
            var identity = DriverProfileIdentitySnapshot.Current();
            return GetStableDriverId(identity);
        }

        public static string GetCurrentDriverName()
        {
            var identity = DriverProfileIdentitySnapshot.Current();
            return FirstNonBlank(identity.DisplayName, identity.DiscordName, identity.DiscordUserId);
        }

        public static void StampCurrentDriver(BolStore.BolRecord record)
        {
            if (record == null)
                return;

            var identity = DriverProfileIdentitySnapshot.Current();

            if (string.IsNullOrWhiteSpace(record.DriverId))
                record.DriverId = GetStableDriverId(identity);

            if (string.IsNullOrWhiteSpace(record.DriverDiscordUserId))
                record.DriverDiscordUserId = identity.DiscordUserId ?? "";

            if (string.IsNullOrWhiteSpace(record.DriverDiscordName))
                record.DriverDiscordName = identity.DiscordName ?? "";

            if (string.IsNullOrWhiteSpace(record.DriverName))
                record.DriverName = FirstNonBlank(identity.DisplayName, identity.DiscordName, identity.DiscordUserId);
        }

        public static string GetStableDriverId(DriverProfileIdentitySnapshot? identity = null)
        {
            identity ??= DriverProfileIdentitySnapshot.Current();
            return DriverProfile.BuildStableDriverId(identity.DiscordUserId, identity.DisplayName, identity.DiscordName);
        }

        public static string GetCurrentRoleText()
        {
            foreach (var value in ReadRoleCandidates())
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "Driver";
        }

        private static IEnumerable<string> ReadRoleCandidates()
        {
            foreach (var typeName in new[]
            {
                "OverWatchELD.Services.UserSession, OverWatchELD",
                "OverWatchELD.Services.EldCurrentUserService, OverWatchELD",
                "OverWatchELD.Services.CurrentUserService, OverWatchELD"
            })
            {
                string role = TryReadRoleFromType(typeName);
                if (!string.IsNullOrWhiteSpace(role))
                    yield return role;
            }

            yield return TryReadRoleFromSettings();
        }

        private static string TryReadRoleFromType(string typeName)
        {
            try
            {
                var type = Type.GetType(typeName);
                if (type == null)
                    return "";

                object? instance = null;
                foreach (var propName in new[] { "Instance", "Current", "CurrentUser", "User" })
                {
                    var p = type.GetProperty(propName, BindingFlags.Public | BindingFlags.Static);
                    instance = p?.GetValue(null);
                    if (instance != null)
                        break;
                }

                foreach (var source in new[] { instance, null })
                {
                    var sourceType = source?.GetType() ?? type;
                    var flags = BindingFlags.Public | (source == null ? BindingFlags.Static : BindingFlags.Instance);
                    foreach (var name in new[] { "Role", "UserRole", "Roles", "RoleName", "Permission", "Permissions" })
                    {
                        var p = sourceType.GetProperty(name, flags);
                        var value = p?.GetValue(source)?.ToString();
                        if (!string.IsNullOrWhiteSpace(value))
                            return value.Trim();
                    }
                }
            }
            catch { }

            return "";
        }

        private static string TryReadRoleFromSettings()
        {
            try
            {
                var settingsType = Type.GetType("OverWatchELD.Properties.Settings, OverWatchELD");
                var defaultProp = settingsType?.GetProperty("Default");
                var settings = defaultProp?.GetValue(null);
                if (settings == null)
                    return "";

                foreach (var name in new[] { "UserRole", "Role", "Roles", "RoleName" })
                {
                    var p = settings.GetType().GetProperty(name);
                    var value = p?.GetValue(settings)?.ToString();
                    if (!string.IsNullOrWhiteSpace(value))
                        return value.Trim();
                }
            }
            catch { }

            return "";
        }

        private static bool HasPrivilegedRole(string? roleText)
        {
            var roles = SplitRoles(roleText).Select(Normalize).ToList();
            return roles.Contains("owner")
                || roles.Contains("admin")
                || roles.Contains("administrator")
                || roles.Contains("dispatcher")
                || roles.Contains("dispatch")
                || roles.Contains("manager")
                || roles.Contains("management");
        }

        private static IEnumerable<string> SplitRoles(string? roleText) =>
            (roleText ?? "").Split(new[] { ',', ';', '|', '/', '\\', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(x => x.Trim())
                            .Where(x => !string.IsNullOrWhiteSpace(x));

        private static bool SameNonBlank(string? a, string? b) =>
            !string.IsNullOrWhiteSpace(a) &&
            !string.IsNullOrWhiteSpace(b) &&
            string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

        private static string FirstNonBlank(params string?[] values)
        {
            foreach (var value in values)
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            return "";
        }

        private static string Normalize(string? value) => (value ?? "").Trim().ToLowerInvariant();
    }
}
