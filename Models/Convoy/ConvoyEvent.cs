using System;
using System.Collections.Generic;

namespace OverWatchELD.Models.Convoy
{
    public sealed class ConvoyEvent
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = "";
        public string StartLocation { get; set; } = "";
        public string Destination { get; set; } = "";
        public string DateDisplay { get; set; } = "";
        public string TimeDisplay { get; set; } = "";
        public string MeetTime { get; set; } = "";
        public string DepartureTime { get; set; } = "";
        public string Server { get; set; } = "";
        public string LeadDriver { get; set; } = "";
        public string Status { get; set; } = "Planned";
        public string Notes { get; set; } = "";
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
        public List<ConvoyAttendee> Attendees { get; set; } = new();
    }

    public sealed class ConvoyAttendee
    {
        public string Name { get; set; } = "";
        public string Truck { get; set; } = "";
        public string Role { get; set; } = "Driver";
        public string Status { get; set; } = "Attending";
    }
}