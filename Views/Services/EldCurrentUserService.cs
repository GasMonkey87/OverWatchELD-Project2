using System;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public static class EldCurrentUserService
    {
        public static string DisplayName =>
            FirstNonBlank(
                GetDiscordIdentity("DisplayName"),
                GetDiscordIdentity("DiscordName"),
                GetDiscordIdentity("Username"),
                GetVtcConfig("DriverName"),
                GetVtcConfig("DisplayName"),
                "ELD Driver");

        public static string DiscordUserId =>
            FirstNonBlank(
                GetDiscordIdentity("DiscordUserId"),
                GetDiscordIdentity("DriverDiscordUserId"),
                GetDiscordIdentity("UserId"),
                GetDiscordIdentity("Id"));

        public static string SafeDisplayName()
        {
            var name = DisplayName?.Trim();

            if (string.IsNullOrWhiteSpace(name))
                return "ELD Driver";

            // Do NOT call SafeDisplayName() from inside SafeDisplayName().
            // That caused infinite recursion / StackOverflowException.
            if (name.Equals(Environment.UserName, StringComparison.OrdinalIgnoreCase))
                return "ELD Driver";

            if (name.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                return "ELD Driver";

            return name;
        }

        public static string ForDiscordEmbedAuthor()
        {
            return SafeDisplayName();
        }

        public static string ForBolDriverName()
        {
            return SafeDisplayName();
        }

        public static string ForTruckAssignment()
        {
            return SafeDisplayName();
        }

        private static string GetDiscordIdentity(string propertyName)
        {
            try
            {
                var identity = DiscordIdentityService.Load();
                if (identity == null)
                    return "";

                var type = identity.GetType();

                var prop = type.GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                if (prop != null)
                    return prop.GetValue(identity)?.ToString()?.Trim() ?? "";

                var field = type.GetField(
                    propertyName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                if (field != null)
                    return field.GetValue(identity)?.ToString()?.Trim() ?? "";
            }
            catch
            {
            }

            return "";
        }

        private static string GetVtcConfig(string propertyName)
        {
            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "OverWatchELD",
                    "vtc.config.json");

                if (!File.Exists(path))
                    return "";

                using var doc = JsonDocument.Parse(File.ReadAllText(path));

                if (doc.RootElement.TryGetProperty(propertyName, out var direct))
                    return direct.GetString()?.Trim() ?? "";

                if (doc.RootElement.TryGetProperty("driver", out var driver) &&
                    driver.TryGetProperty(propertyName, out var driverValue))
                    return driverValue.GetString()?.Trim() ?? "";

                if (doc.RootElement.TryGetProperty("user", out var user) &&
                    user.TryGetProperty(propertyName, out var userValue))
                    return userValue.GetString()?.Trim() ?? "";
            }
            catch
            {
            }

            return "";
        }

        private static string FirstNonBlank(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }
    }
}