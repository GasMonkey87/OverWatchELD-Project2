using System;

namespace OverWatchELD.Models
{
    public class InspectionEntry
    {
        public long Id { get; set; }
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

        public string? Officer { get; set; }
        public string? Location { get; set; }
        public string? Notes { get; set; }
        public bool Passed { get; set; } = true;
    }
}
