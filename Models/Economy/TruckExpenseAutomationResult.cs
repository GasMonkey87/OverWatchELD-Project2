using System;
using System.Collections.Generic;

namespace OverWatchELD.Models.Economy
{
    public sealed class TruckExpenseAutomationResult
    {
        public DateTime ProcessedUtc { get; set; } = DateTime.UtcNow;
        public string TruckKey { get; set; } = "";
        public string TruckNumber { get; set; } = "";
        public string TruckName { get; set; } = "";
        public string DriverName { get; set; } = "";

        public bool PostedFuelExpense { get; set; }
        public bool PostedWearExpense { get; set; }
        public bool PostedRepairExpense { get; set; }

        public decimal FuelExpense { get; set; }
        public decimal WearExpense { get; set; }
        public decimal RepairExpense { get; set; }

        public double MilesDelta { get; set; }
        public double FuelPercentDelta { get; set; }
        public double DamagePercentDelta { get; set; }

        public List<string> Messages { get; set; } = new();
    }
}
