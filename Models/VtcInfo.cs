namespace OverWatchELD.Models
{
    public class VtcInfo
    {
        public string CompanyName { get; set; } = "";
        public string DriverName { get; set; } = "";
        public string UnitNumber { get; set; } = "";
        public string DotNumber { get; set; } = "";
        public string McNumber { get; set; } = "";
        public string HomeTerminal { get; set; } = "";
        public string Notes { get; set; } = "";

        // Optional: webhook / server sync
        public string WebhookUrl { get; set; } = "";
        public string ApiKey { get; set; } = "";


        // ===== Discord / VTC Lock (optional) =====
        // If IsLockedToDiscord is true, CompanyName is controlled by Discord membership (1 guild = 1 VTC).
        public bool IsLockedToDiscord { get; set; } = false;

        // Bot/API that answers: /api/vtc-binding?discordUserId=...
        public string DiscordLockApiBaseUrl { get; set; } = "http://localhost:3555";

        // Discord settings
        public string DiscordGuildId { get; set; } = "";
        public string DiscordUserId { get; set; } = "";

        // Optional internal identifiers returned by the lock API
        public string VtcId { get; set; } = "";
        public string VtcName { get; set; } = "";

    }
}
