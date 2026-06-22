using System;
using System.Collections.Generic;

namespace OverWatchELD.Models.Fleet
{
    public sealed class FleetTruck
    {
        public string? LastKnownLocation { get; set; }
        public double ConditionPercent { get; set; } = 100;
        public double FuelPercent { get; set; } = 100;
        public bool NeedsService { get; set; }
        public bool IsDriving { get; set; }
        public bool IsOnline { get; set; }
        // Primary key
        public string Plate { get; set; } = "";

        public string Nickname { get; set; } = "";       // optional
        public string MakeModel { get; set; } = "";      // optional
        public string AssignedDriver { get; set; } = ""; // optional

        // Live state (updated by telemetry)
        public double OdometerMiles { get; set; } = 0;
        public double FuelPct { get; set; } = 0; // 0..100

        public string? CurrentGarageId { get; set; }
        public string? HomeGarageId { get; set; }

        public bool IsParked { get; set; }
        public DateTime? ParkedUtc { get; set; }

        public double? LastKnownMapX { get; set; }
        public double? LastKnownMapY { get; set; }
        public string? LastKnownCity { get; set; }
        public string? LastKnownState { get; set; }

        public double TotalFuelCost { get; set; }
        public double TotalTollCost { get; set; }
        public double TotalMaintenanceCost { get; set; }
        public double TotalRepairCost { get; set; }

        public double? LastKnownOdometerMiles { get; set; }
        public string? LastKnownDutyStatus { get; set; }

        public DateTimeOffset? LastFuelUtc { get; set; }
        public DateTimeOffset? LastTollUtc { get; set; }
        public DateTimeOffset? LastMaintenanceUtc { get; set; }
        public DateTimeOffset? LastRepairUtc { get; set; }

        // Damage 0..100 (percent)
        public double EngineDamagePct { get; set; } = 0;
        public double TransmissionDamagePct { get; set; } = 0;
        public double CabinDamagePct { get; set; } = 0;
        public double ChassisDamagePct { get; set; } = 0;
        public double WheelsDamagePct { get; set; } = 0;

        public DateTimeOffset LastTelemetryUtc { get; set; } = DateTimeOffset.MinValue;

        // Auto fuel-fill detection
        public double LastFuelPctSeen { get; set; } = -1;
        public double LastFuelOdometerSeen { get; set; } = -1;
        public DateTimeOffset LastFuelFillUtc { get; set; } = DateTimeOffset.MinValue;

        // Maintenance tracking (miles)
        public double LastOilChangeMiles { get; set; } = 0;
        public double LastTireServiceMiles { get; set; } = 0;
        public double LastMajorServiceMiles { get; set; } = 0;

        public DateTimeOffset LastDotInspectionUtc { get; set; } = DateTimeOffset.MinValue;

        // Auto event dedupe / threshold tracking
        public double LastDamageAlertMaxPct { get; set; } = 0;
        public DateTimeOffset LastDamageLedgerUtc { get; set; } = DateTimeOffset.MinValue;
        public decimal LastTollAmount { get; set; } = 0;
        public DateTimeOffset LastTollLoggedUtc { get; set; } = DateTimeOffset.MinValue;

        public List<MaintenanceRecord> Maintenance { get; set; } = new();
        public List<FuelRecord> FuelLog { get; set; } = new();
        public List<TollRecord> TollLog { get; set; } = new();
    }

    public sealed class MaintenanceRecord
    {
        public DateTimeOffset DateUtc { get; set; } = DateTimeOffset.UtcNow;
        public string Type { get; set; } = "";
        public double Mileage { get; set; } = 0;
        public decimal Cost { get; set; } = 0;
        public string Notes { get; set; } = "";
    }

    public sealed class FuelRecord
    {
        public DateTimeOffset DateUtc { get; set; } = DateTimeOffset.UtcNow;
        public double OdometerMiles { get; set; } = 0;
        public double FuelPctAfter { get; set; } = 0;
        public decimal Cost { get; set; } = 0;
        public string Notes { get; set; } = "";
    }


    public sealed class TollRecord
    {
        public DateTimeOffset DateUtc { get; set; } = DateTimeOffset.UtcNow;
        public double OdometerMiles { get; set; } = 0;
        public decimal Cost { get; set; } = 0;
        public string Notes { get; set; } = "";
    }
}