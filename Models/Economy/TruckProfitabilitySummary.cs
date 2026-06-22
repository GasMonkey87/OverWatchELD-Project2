using System;

namespace OverWatchELD.Models.Economy
{
    public sealed class TruckProfitabilitySummary
    {
        public string TruckNumber { get; set; } = "";
        public string TruckName { get; set; } = "";
        public string PrimaryDriver { get; set; } = "";

        public int LoadsDelivered { get; set; }
        public double MilesDriven { get; set; }

        public decimal GrossRevenue { get; set; }
        public decimal PayrollCost { get; set; }
        public decimal FuelCost { get; set; }
        public decimal MaintenanceCost { get; set; }
        public decimal OtherCost { get; set; }

        public decimal TotalCost => PayrollCost + FuelCost + MaintenanceCost + OtherCost;
        public decimal NetProfit => GrossRevenue - TotalCost;

        public decimal RevenuePerMile => MilesDriven > 0
            ? GrossRevenue / (decimal)MilesDriven
            : 0m;

        public decimal ProfitPerMile => MilesDriven > 0
            ? NetProfit / (decimal)MilesDriven
            : 0m;

        public DateTime? LastActivityUtc { get; set; }
    }
}
