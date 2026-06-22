using System;

namespace OverWatchELD.Models.Achievements
{
    public sealed class AchievementRecord
    {
        public string Id { get; set; } = "";
        public string Category { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Icon { get; set; } = "🏆";

        public bool IsUnlocked { get; set; }

        public bool Unlocked
        {
            get => IsUnlocked;
            set => IsUnlocked = value;
        }

        public DateTime? UnlockedUtc { get; set; }

        public DateTime? EarnedUtc
        {
            get => UnlockedUtc;
            set => UnlockedUtc = value;
        }

        public double Progress { get; set; }
        public double Target { get; set; }
        public string ProgressText { get; set; } = "";
        public string RewardText { get; set; } = "";
        public string DriverName { get; set; } = "";

        public string Rarity { get; set; } = "Common";
        public bool IsCustom { get; set; }
        public string CreatedBy { get; set; } = "";
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }
}
