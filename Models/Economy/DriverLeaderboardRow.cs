using System;

namespace OverWatchELD.Models.Economy
{
    public sealed class DriverLeaderboardRow
    {
        public int Rank { get; set; }
        public string DriverName { get; set; } = "";
        public string TruckNumber { get; set; } = "";
        public string TruckName { get; set; } = "";

        public int LoadsDelivered { get; set; }
        public double MilesDriven { get; set; }

        public decimal GrossRevenue { get; set; }
        public decimal PayrollPaid { get; set; }
        public decimal CompanyProfit { get; set; }

        public decimal RevenuePerMile => MilesDriven > 0
            ? GrossRevenue / (decimal)MilesDriven
            : 0m;

        public decimal ProfitPerMile => MilesDriven > 0
            ? CompanyProfit / (decimal)MilesDriven
            : 0m;

        public DateTime? LastDeliveryUtc { get; set; }
    }
}
