using System;

namespace OverWatchELD.Overlay
{
    public sealed class OverlaySnapshot
    {
        public string DutyStatus { get; set; } = "ON DUTY";
        public string HosRemaining { get; set; } = "--:--";
        public string DriverName { get; set; } = "Driver";
        public string LoadName { get; set; } = "No active load";
        public string Route { get; set; } = "Pickup → Delivery";
        public string Speed { get; set; } = "0 MPH";
        public string Fuel { get; set; } = "--";
        public string Maintenance { get; set; } = "READY";
        public string StatusLine { get; set; } = "Waiting for ATS telemetry";
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}
