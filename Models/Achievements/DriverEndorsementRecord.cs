using System;

namespace OverWatchELD.Models.Achievements
{
    public sealed class DriverEndorsementRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public string DriverName { get; set; } = "";

        public string DriverDiscordId { get; set; } = "";

        public string Title { get; set; } = "";

        public string Icon { get; set; } = "⭐";

        public string Notes { get; set; } = "";

        public string CreatedBy { get; set; } = "";

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public bool IsActive { get; set; } = true;
    }
}
