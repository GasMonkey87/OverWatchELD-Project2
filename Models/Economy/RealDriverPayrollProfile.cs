using System;

namespace OverWatchELD.Models.Economy
{
    public sealed class RealDriverPayrollProfile
    {
        public string DriverName { get; set; } = "";
        public string DriverDiscordId { get; set; } = "";
        public string PayMode { get; set; } = "Percent"; // Percent, PerMile, FlatPerLoad
        public decimal PercentOfLoad { get; set; } = 25m;
        public decimal CentsPerMile { get; set; } = 65m;
        public decimal FlatPerLoad { get; set; } = 250m;
        public decimal SafetyBonusPercent { get; set; } = 0m;
        public bool Enabled { get; set; } = true;
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }
}
