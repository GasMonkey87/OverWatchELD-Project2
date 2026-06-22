using System;

namespace OverWatchELD.Models.Fleet
{
    public sealed class FleetCommandTruck
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        // Unified truck identity
        public string TruckNumber { get; set; } = "";       // preferred fleet/unit id
        public string PlateNumber { get; set; } = "";
        public string TruckName { get; set; } = "";
        public string Model { get; set; } = "";
        public string ModName { get; set; } = "";

        // Parked Trucks
        public bool IsParked { get; set; }

        public DateTime? ParkedUtc { get; set; }

        public double? LastKnownMapX { get; set; }

        public double? LastKnownMapY { get; set; }

        public string? LastKnownCity { get; set; }

        public string? LastKnownState { get; set; }

        public string? CurrentGarageId { get; set; }

        public string? HomeGarageId { get; set; }

        // Driver assignment
        public string AssignedDriver { get; set; } = "";
        public string DriverDiscordId { get; set; } = "";

        // Load tracking
        public string CurrentLoadNumber { get; set; } = "";

        // Status / live state
        public string Status { get; set; } = "Unassigned"; // Active / Assigned Load / Unassigned / Needs Service / Needs Inspection / Out of Service / Offline
        public string Location { get; set; } = "";
        public int HealthPercent { get; set; } = 100;
        public double FuelPercent { get; set; } = 100;
        public double OdometerMiles { get; set; } = 0;
        public bool IsActive { get; set; } = true;
        public bool IsOnline { get; set; }
        public bool IsDriving { get; set; }

        // Maintenance / inspection
        public DateTime? ServiceDueDate { get; set; }
        public DateTime? LastServiceDate { get; set; }
        public DateTime? InspectionDueDate { get; set; }
        public DateTime? LastInspectionDate { get; set; }

        // Cost rollups carried forward from prior fleet systems
        public double TotalFuelCost { get; set; }
        public double TotalTollCost { get; set; }
        public double TotalMaintenanceCost { get; set; }
        public double TotalRepairCost { get; set; }

        public DateTimeOffset? LastFuelUtc { get; set; }
        public DateTimeOffset? LastTollUtc { get; set; }
        public DateTimeOffset? LastMaintenanceUtc { get; set; }
        public DateTimeOffset? LastRepairUtc { get; set; }
        public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    }
}
