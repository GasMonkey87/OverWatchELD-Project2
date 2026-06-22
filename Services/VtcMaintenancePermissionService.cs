using System;

namespace OverWatchELD.Services
{
    public static class VtcMaintenancePermissionService
    {
        public static bool CanEdit(string? discordRole)
        {
            var role = (discordRole ?? "").Trim().ToLowerInvariant();

            return role.Contains("owner")
                || role.Contains("admin")
                || role.Contains("manager")
                || role.Contains("mechanic");
        }

        public static bool CanView(string? discordRole)
        {
            var role = (discordRole ?? "").Trim().ToLowerInvariant();

            return CanEdit(role)
                || role.Contains("driver")
                || role.Contains("dispatcher");
        }
    }
}