namespace OverWatchELD.Models
{
    public class DiscordVtcBinding
    {
        public bool locked { get; set; }
        public bool isMember { get; set; }

        public string? guildId { get; set; }
        public string? vtcId { get; set; }
        public string? vtcName { get; set; }
    }
}
