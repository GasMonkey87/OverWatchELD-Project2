using System;

namespace OverWatchELD.Models.Economy
{
    public sealed class EconomyTransaction
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public string Type { get; set; } = "";
        public string Category { get; set; } = "";
        public string Source { get; set; } = "";

        public string DriverName { get; set; } = "";
        public string TruckNumber { get; set; } = "";
        public string TruckName { get; set; } = "";
        public string GarageId { get; set; } = "";
        public string LoadNumber { get; set; } = "";

        public decimal Amount { get; set; }
        public decimal BalanceAfter { get; set; }

        public string Description { get; set; } = "";
        public string Notes { get; set; } = "";
    }
}
