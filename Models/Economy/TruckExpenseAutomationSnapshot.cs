using System;

namespace OverWatchELD.Models.Economy
{
    public sealed class TruckExpenseAutomationSnapshot
    {
        public string TruckKey { get; set; } = "";
        public string TruckNumber { get; set; } = "";
        public string TruckName { get; set; } = "";
        public string DriverName { get; set; } = "";

        public double? FuelPercent { get; set; }
        public double? OdometerMiles { get; set; }
        public double? HealthPercent { get; set; }
        public double? DamagePercent { get; set; }

        public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
    }
}
