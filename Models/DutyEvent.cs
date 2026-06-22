using System;

namespace OverWatchELD.Models
{
    public class DutyEvent
    {
        public long Id { get; set; }
        public DateTimeOffset StartUtc { get; set; }
        public DateTimeOffset? EndUtc { get; set; }
        public DutyStatus Status { get; set; }

        // Core fields
        public string? Notes { get; set; }
        public string? LocationText { get; set; }

    // Back-compat alias: older code references `Location` while the DB column is `location_text`.
    // Keep both names to avoid breaking callers.
    public string? Location
    {
        get => LocationText;
        set => LocationText = value;
    }
        public string? Source { get; set; }

        // Coordinates
        public double? Lat { get; set; }
        public double? Lon { get; set; }

        // Edit tracking
        public bool IsEdited { get; set; }
        public DateTimeOffset? EditedAtUtc { get; set; }
        public string? EditReason { get; set; }

        // Graph/UI helpers
        public bool IsLocked { get; set; }
        public bool IsSelected { get; set; }

        public double GraphLeft { get; set; }
        public double GraphWidth { get; set; }
        public double GraphTop { get; set; }
        public double GraphHeight { get; set; }

        public double DotLeft { get; set; }
        public double DotTop { get; set; }

        // Derived helpers
        public DateTimeOffset EffectiveEndUtc =>
            EndUtc ?? DateTimeOffset.UtcNow;
    }
}
