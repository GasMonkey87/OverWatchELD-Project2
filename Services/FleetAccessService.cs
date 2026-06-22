using System;
using System.Linq;

namespace OverWatchELD.Services
{
    public static class FleetAccessService
    {
        public static bool CanViewMaintenanceFinance()
        {
            try
            {
                var roleText = GetRoleText();
                if (string.IsNullOrWhiteSpace(roleText))
                    return false;

                var roles = roleText
                    .Split(new[] { ',', ';', '|', '/' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim().ToLowerInvariant())
                    .ToList();

                return roles.Contains("owner")
                    || roles.Contains("admin")
                    || roles.Contains("administrator")
                    || roles.Contains("manager");
            }
            catch
            {
                return false;
            }
        }

        private static string GetRoleText()
        {
            try
            {
                // Try current user session first
                var sessionType = Type.GetType("OverWatchELD.Services.UserSession, OverWatchELD");
                if (sessionType != null)
                {
                    var instanceProp = sessionType.GetProperty("Instance");
                    var instance = instanceProp?.GetValue(null);

                    if (instance != null)
                    {
                        var roleProp =
                            sessionType.GetProperty("Role")
                            ?? sessionType.GetProperty("UserRole")
                            ?? sessionType.GetProperty("Roles")
                            ?? sessionType.GetProperty("Permission")
                            ?? sessionType.GetProperty("Permissions");

                        var roleVal = roleProp?.GetValue(instance)?.ToString();
                        if (!string.IsNullOrWhiteSpace(roleVal))
                            return roleVal!;
                    }
                }
            }
            catch
            {
            }

            try
            {
                // Try app settings fallback
                var settingsType = Type.GetType("OverWatchELD.Properties.Settings, OverWatchELD");
                var defaultProp = settingsType?.GetProperty("Default");
                var settings = defaultProp?.GetValue(null);

                if (settings != null)
                {
                    var roleProp =
                        settings.GetType().GetProperty("UserRole")
                        ?? settings.GetType().GetProperty("Role")
                        ?? settings.GetType().GetProperty("Roles");

                    var roleVal = roleProp?.GetValue(settings)?.ToString();
                    if (!string.IsNullOrWhiteSpace(roleVal))
                        return roleVal!;
                }
            }
            catch
            {
            }

            return "";
        }
    }
}