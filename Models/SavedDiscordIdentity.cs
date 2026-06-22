using System;

namespace OverWatchELD.Models
{
    public sealed class SavedDiscordIdentity
    {
        public string DiscordUserId { get; set; } = "";
        public string DiscordUsername { get; set; } = "";
        public string GuildId { get; set; } = "";
        public string VtcName { get; set; } = "";
        public string LastPairCode { get; set; } = "";
        public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;

        public override string ToString()
        {
            if (!string.IsNullOrWhiteSpace(VtcName))
                return $"{DiscordUsername} ({VtcName})";

            return DiscordUsername;
        }
    }
}