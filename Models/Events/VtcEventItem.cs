using System;

namespace OverWatchELD.Models.Events
{
    public sealed class VtcEventItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = "";
        public string EventType { get; set; } = "Event";
        public DateTime EventDate { get; set; } = DateTime.Today;
        public string TimeDisplay { get; set; } = "";
        public string Location { get; set; } = "";
        public string Host { get; set; } = "";
        public string Notes { get; set; } = "";
        public int AttendeeCount { get; set; }
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    }
}