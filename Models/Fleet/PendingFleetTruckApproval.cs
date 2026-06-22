using System;

namespace OverWatchELD.Models.Fleet
{
    public sealed class PendingFleetTruckApproval
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string TruckNumber { get; set; } = "";
        public string TruckName { get; set; } = "";
        public string MakeModel { get; set; } = "";
        public string PlateNumber { get; set; } = "";
        public string AssignedDriver { get; set; } = "";
        public string DriverDiscordId { get; set; } = "";
        public string CurrentLocation { get; set; } = "";
        public double OdometerMiles { get; set; }
        public double FuelPercent { get; set; }
        public double HealthPercent { get; set; }
        public double DamagePercent { get; set; }
        public string Source { get; set; } = "Telemetry";
        public string Status { get; set; } = "Pending";
        public string Notes { get; set; } = "";
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime? ReviewedUtc { get; set; }
        public string ReviewedBy { get; set; } = "";
    }
}
