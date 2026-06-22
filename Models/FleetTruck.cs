using System;

namespace OverWatchELD.Models
{
    public class FleetTruck
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string TruckName { get; set; } = "";
        public string Model { get; set; } = "";
        public string ModName { get; set; } = "";
        public string PlateNumber { get; set; } = "";
        public string DiscordUserId { get; set; } = "";
        public string DiscordUsername { get; set; } = "";
        public string DriverName { get; set; } = "";
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
        public double? OdometerMiles { get; set; }
        public bool IsActive { get; set; } = true;

        public string LastSeenLocation { get; set; } = "";

        // ✅ Added so telemetry/fleet views can save live truck state
        public double FuelPercent { get; set; } = 100;
        public double HealthPercent { get; set; } = 100;

        // Optional compatibility aliases
        public double FuelPct
        {
            get => FuelPercent;
            set => FuelPercent = value;
        }

        public double ConditionPercent
        {
            get => HealthPercent;
            set => HealthPercent = value;
        }

        public string FleetStatus =>
            IsActive ? "Active" :
            !string.IsNullOrWhiteSpace(DiscordUsername) ? "Linked" :
            "Unlinked";

        public string LastSeenAgo
        {
            get
            {
                if (LastSeenUtc == default)
                    return "--";

                var utc = LastSeenUtc.Kind == DateTimeKind.Utc
                    ? LastSeenUtc
                    : DateTime.SpecifyKind(LastSeenUtc, DateTimeKind.Utc);

                var span = DateTime.UtcNow - utc;

                if (span.TotalSeconds < 60)
                    return "Just now";

                if (span.TotalMinutes < 60)
                    return $"{Math.Max(1, (int)span.TotalMinutes)}m ago";

                if (span.TotalHours < 24)
                    return $"{Math.Max(1, (int)span.TotalHours)}h ago";

                if (span.TotalDays < 2)
                    return "Yesterday";

                if (span.TotalDays < 7)
                    return $"{Math.Max(1, (int)span.TotalDays)}d ago";

                return utc.ToLocalTime().ToString("g");
            }
        }

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
    }
}