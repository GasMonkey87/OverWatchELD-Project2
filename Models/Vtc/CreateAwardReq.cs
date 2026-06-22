namespace OverWatchELD.Models.Vtc
{
    public sealed class CreateAwardReq
    {
        public string GuildId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string IconEmoji { get; set; } = "🏆";
        public bool IsAchievement { get; set; }
        public string CreatedByUserId { get; set; } = "";
        public string CreatedByUsername { get; set; } = "";
    }
}