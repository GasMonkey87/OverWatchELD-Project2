using System;

namespace OverWatchELD.Models.Economy
{
    public sealed class RealDriverEconomySummary
    {
        public string DriverName { get; set; } = "";
        public string DriverDiscordId { get; set; } = "";
        public string TruckName { get; set; } = "";
        public string TruckNumber { get; set; } = "";

        public int LoadsDelivered { get; set; }
        public int LoadsPickedUp { get; set; }
        public double MilesDriven { get; set; }

        public decimal GrossRevenue { get; set; }
        public decimal PayrollPaid { get; set; }
        public decimal CompanyProfit { get; set; }

        public DateTime? LastDeliveryUtc { get; set; }
        public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;
    }
}
