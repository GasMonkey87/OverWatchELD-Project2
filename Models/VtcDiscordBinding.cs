namespace OverWatchELD.Models
{
    public sealed class VtcDiscordBinding
    {
        public string DiscordUserId { get; set; } = "";
        public string GuildId { get; set; } = "";
        public string VtcId { get; set; } = "";
        public string VtcName { get; set; } = "";
        public bool Locked { get; set; } = true;
        public bool IsMember { get; set; } = true;
    }
}
