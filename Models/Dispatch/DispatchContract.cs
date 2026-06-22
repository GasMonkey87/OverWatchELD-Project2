using System;

namespace OverWatchELD.Models.Dispatch
{
    public sealed class DispatchContract
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public string ContractNumber { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string ContractType { get; set; } = "Dedicated Lane"; // Dedicated Lane, Recurring Freight, Priority Freight, Spot Contract

        public string OriginCity { get; set; } = "";
        public string OriginState { get; set; } = "";
        public string DestinationCity { get; set; } = "";
        public string DestinationState { get; set; } = "";

        public string Cargo { get; set; } = "";
        public string TrailerType { get; set; } = "";

        public string AssignedDriver { get; set; } = "";
        public string AssignedTruckNumber { get; set; } = "";
        public string AssignedTruckName { get; set; } = "";

        public int RequiredLoads { get; set; } = 5;
        public int CompletedLoads { get; set; }
        public double EstimatedMilesPerLoad { get; set; } = 500;

        public decimal RatePerMile { get; set; } = 4.25m;
        public decimal BonusAmount { get; set; } = 1000m;
        public decimal PenaltyAmount { get; set; } = 500m;

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime StartUtc { get; set; } = DateTime.UtcNow;
        public DateTime DueUtc { get; set; } = DateTime.UtcNow.AddDays(7);
        public DateTime? CompletedUtc { get; set; }

        public string Status { get; set; } = "Active"; // Active, Completed, Failed, Cancelled
        public string Notes { get; set; } = "";

        public decimal EstimatedRevenue =>
            Math.Round((decimal)EstimatedMilesPerLoad * RatePerMile * RequiredLoads, 2);

        public decimal ProgressPercent =>
            RequiredLoads <= 0 ? 0 : Math.Round((decimal)CompletedLoads / RequiredLoads * 100m, 1);

        public bool IsOverdue =>
            Status.Equals("Active", StringComparison.OrdinalIgnoreCase) &&
            DateTime.UtcNow > DueUtc;
    }
}
