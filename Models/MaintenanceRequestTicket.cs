using System;

namespace OverWatchELD.Models
{
    public sealed class MaintenanceRequestTicket
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string RequestNumber { get; set; } = "";
        public string GuildId { get; set; } = "";

        public string TruckId { get; set; } = "";
        public string UnitNumber { get; set; } = "";
        public string TruckName { get; set; } = "";
        public string PlateNumber { get; set; } = "";
        public string DriverName { get; set; } = "";
        public string DriverDiscordId { get; set; } = "";
        public string Location { get; set; } = "";

        public double OdometerMiles { get; set; }
        public double FuelPercent { get; set; }
        public double ConditionPercent { get; set; }
        public string CurrentIssue { get; set; } = "";
        public string CurrentIssueSeverity { get; set; } = "";
        public bool OutOfService { get; set; }

        public bool DotInspectionRequested { get; set; }
        public bool DamageRepairRequested { get; set; }
        public bool OtherMaintenanceRequested { get; set; }
        public bool MalfunctionRepairRequested { get; set; }

        public string Notes { get; set; } = "";
        public string Status { get; set; } = "Open";
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime? FixedUtc { get; set; }
        public string FixedBy { get; set; } = "";
        public string FixNotes { get; set; } = "";

        public string CreatedLocalDisplay => CreatedUtc.ToLocalTime().ToString("g");
        public string FixedLocalDisplay => FixedUtc == null ? "" : FixedUtc.Value.ToLocalTime().ToString("g");
        public string RequestSummary
        {
            get
            {
                var parts = new System.Collections.Generic.List<string>();
                if (DotInspectionRequested) parts.Add("DOT");
                if (DamageRepairRequested) parts.Add("Damage");
                if (OtherMaintenanceRequested) parts.Add("Maintenance");
                if (MalfunctionRepairRequested) parts.Add("Malfunction");
                return parts.Count == 0 ? "General" : string.Join(", ", parts);
            }
        }
    }
}
