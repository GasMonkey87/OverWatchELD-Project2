using System;

namespace OverWatchELD.Services
{
    public sealed class VtcAccessProfile
    {
        public string DiscordUserId { get; set; } = "";
        public string DiscordUsername { get; set; } = "";
        public string Role { get; set; } = "Driver";
        public string GuildId { get; set; } = "";
        public string VtcName { get; set; } = "";
    }

    public sealed class VtcAccessService
    {
        private readonly DiscordIdentityService _identityService = new();

        public VtcAccessProfile LoadCurrentProfile()
        {
            var id = _identityService.LoadOrDefault();
            var role = "Driver";

            try
            {
                var prop = id.GetType().GetProperty("Role");
                if (prop != null)
                {
                    var value = prop.GetValue(id)?.ToString();
                    if (!string.IsNullOrWhiteSpace(value))
                        role = value.Trim();
                }
            }
            catch { }

            return new VtcAccessProfile
            {
                DiscordUserId = id.DiscordUserId,
                DiscordUsername = id.DiscordUsername,
                GuildId = id.GuildId,
                VtcName = id.VtcName,
                Role = NormalizeRole(role)
            };
        }

        public bool IsManagement(string? role)
        {
            var r = NormalizeRole(role);
            return string.Equals(r, "Owner", StringComparison.OrdinalIgnoreCase)
                || string.Equals(r, "Manager", StringComparison.OrdinalIgnoreCase);
        }

        public string NormalizeRole(string? role)
        {
            var r = (role ?? "").Trim();

            if (string.Equals(r, "Owner", StringComparison.OrdinalIgnoreCase))
                return "Owner";

            if (string.Equals(r, "Manager", StringComparison.OrdinalIgnoreCase))
                return "Manager";

            return "Driver";
        }
    }
}
