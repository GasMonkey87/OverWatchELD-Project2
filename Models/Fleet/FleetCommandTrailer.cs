using System;

namespace OverWatchELD.Models.Fleet
{
    public sealed class FleetCommandTrailer
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string TrailerNumber { get; set; } = "";
        public string PlateNumber { get; set; } = "";
        public string TrailerName { get; set; } = "";
        public string Model { get; set; } = "";
        public string TrailerType { get; set; } = "";
        public string ModName { get; set; } = "";
        public string AssignedDriver { get; set; } = "";
        public string DriverDiscordId { get; set; } = "";
        public string CurrentLoadNumber { get; set; } = "";
        public string Status { get; set; } = "Unassigned";
        public string Location { get; set; } = "";
        public int HealthPercent { get; set; } = 100;
        public double OdometerMiles { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsOnline { get; set; }
        public DateTime? ServiceDueDate { get; set; }
        public DateTime? LastServiceDate { get; set; }
        public DateTime? InspectionDueDate { get; set; }
        public DateTime? LastInspectionDate { get; set; }
        public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    }
}
