using System;

namespace OverWatchELD.Models.Performance
{
    public sealed class DriverPerformanceEvent
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public string DriverName { get; set; } = "";
        public string DriverDiscordId { get; set; } = "";
        public string TruckNumber { get; set; } = "";
        public string TruckName { get; set; } = "";

        public string EventType { get; set; } = "";
        public string Severity { get; set; } = "";
        public double Value { get; set; }
        public string Description { get; set; } = "";
        public string Source { get; set; } = "";
    }
}
