using System;

namespace OverWatchELD.Models
{
    /// <summary>
    /// Server/local authoritative identity metadata used to keep driver history stable
    /// even when a driver changes display names.
    /// </summary>
    public sealed class DriverIdentityMetadata
    {
        public string DriverName { get; set; } = "";
        public string DiscordUserId { get; set; } = "";
        public string DiscordUsername { get; set; } = "";
        public string TruckersMpId { get; set; } = "";
        public string DeviceId { get; set; } = "";
        public string IdentityHash { get; set; } = "";
        public DateTimeOffset CapturedUtc { get; set; } = DateTimeOffset.UtcNow;

        public string PermanentDriverKey => !string.IsNullOrWhiteSpace(DiscordUserId)
            ? "discord:" + DiscordUserId.Trim()
            : IdentityHash;
    }
}
