using System;
using System.Collections.Generic;

namespace OverWatchELD.Models
{
    public class VtcMaintenanceTruck
    {
        public string TruckId { get; set; } = Guid.NewGuid().ToString("N");
        public string UnitNumber { get; set; } = "";
        public string TruckName { get; set; } = "";
        public string AssignedDriver { get; set; } = "";
        public string Location { get; set; } = "";
        public string PlateNumber { get; set; } = "";

        public double OdometerMiles { get; set; }
        public double FuelPercent { get; set; }
        public double ConditionPercent { get; set; } = 100;

        public string CurrentIssue { get; set; } = "";
        public string CurrentIssueSeverity { get; set; } = "";

        public double RandomMalfunctionStartOdometerMiles { get; set; }
        public double RandomMalfunctionTargetOdometerMiles { get; set; }

        public DateTime? LastServiceUtc { get; set; }
        public DateTime? LastInspectionUtc { get; set; }
        public DateTime? DotExpirationUtc { get; set; }

        public bool OutOfService { get; set; }
        public string Notes { get; set; } = "";

        public List<VtcServiceRecord> ServiceHistory { get; set; } = new();
        public List<VtcDamageReport> DamageReports { get; set; } = new();
    }

    public class VtcServiceRecord
    {
        public string RecordId { get; set; } = Guid.NewGuid().ToString("N");
        public string ServiceType { get; set; } = "";
        public string Notes { get; set; } = "";
        public double OdometerMiles { get; set; }
        public string CompletedBy { get; set; } = "";
        public DateTime CompletedUtc { get; set; } = DateTime.UtcNow;
    }

    public class VtcDamageReport
    {
        public string ReportId { get; set; } = Guid.NewGuid().ToString("N");
        public string Severity { get; set; } = "Warning";
        public string ReportedBy { get; set; } = "";
        public string Notes { get; set; } = "";
        public DateTime ReportedUtc { get; set; } = DateTime.UtcNow;
        public bool Resolved { get; set; }
    }

    public class VtcMaintenanceAlert
    {
        public string AlertId { get; set; } = Guid.NewGuid().ToString("N");
        public string TruckId { get; set; } = "";
        public string UnitNumber { get; set; } = "";
        public string Severity { get; set; } = "Info";
        public string Message { get; set; } = "";
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public bool Acknowledged { get; set; }
    }

    public class VtcMaintenanceState
    {
        public bool RandomMalfunctionsEnabled { get; set; }
        public List<VtcMaintenanceTruck> Trucks { get; set; } = new();
        public List<VtcMaintenanceAlert> Alerts { get; set; } = new();
    }
}