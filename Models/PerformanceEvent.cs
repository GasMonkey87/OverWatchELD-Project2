using System;

namespace OverWatchELD.Models
{
    public sealed class PerformanceEvent
    {
        public long Id { get; set; }
        public string DriverName { get; set; } = "";
        public string EventType { get; set; } = "";
        public int Severity { get; set; }
        public double Value { get; set; }
        public DateTime CreatedUtc { get; set; }
    }
}
