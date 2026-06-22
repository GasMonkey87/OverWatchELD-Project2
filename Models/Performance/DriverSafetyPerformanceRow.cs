using System;

namespace OverWatchELD.Models.Performance
{
    public sealed class DriverSafetyPerformanceRow
    {
        public int Rank { get; set; }

        public string DriverName { get; set; } = "";
        public string DriverDiscordId { get; set; } = "";
        public string TruckNumber { get; set; } = "";
        public string TruckName { get; set; } = "";

        public int LoadsDelivered { get; set; }
        public int LoadsPickedUp { get; set; }
        public double MilesDriven { get; set; }

        public decimal GrossRevenue { get; set; }
        public decimal PayrollPaid { get; set; }
        public decimal CompanyProfit { get; set; }

        public int SpeedingEvents { get; set; }
        public int HarshBrakeEvents { get; set; }
        public int IdleEvents { get; set; }
        public int DamageEvents { get; set; }
        public int LateDeliveries { get; set; }

        public double SafetyScore { get; set; } = 100;
        public double PerformanceScore { get; set; } = 100;
        public double EconomyScore { get; set; } = 100;
        public double OverallScore { get; set; } = 100;

        public string Grade { get; set; } = "A";
        public DateTime? LastActivityUtc { get; set; }
        public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;
    }
}
