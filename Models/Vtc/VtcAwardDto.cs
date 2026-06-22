using System;

namespace OverWatchELD.Models.Vtc
{
    public sealed class VtcAwardDto
    {
        public string Id { get; set; } = "";
        public string GuildId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string IconEmoji { get; set; } = "🏆";
        public string CreatedByUserId { get; set; } = "";
        public string CreatedByUsername { get; set; } = "";
        public DateTime CreatedUtc { get; set; }
        public bool IsAchievement { get; set; }
    }
}