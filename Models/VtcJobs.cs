// Models/VtcJob.cs

using System;

namespace OverWatchELD.Models
{
    public sealed class VtcJob
    {
        public string Id { get; set; } = "";
        public string DriverKey { get; set; } = "";
        public string DriverName { get; set; } = "";
        public string Title { get; set; } = "";
        public string Body { get; set; } = "";

        public string Origin { get; set; } = "";
        public string Destination { get; set; } = "";

        public string Status { get; set; } = "Assigned"; // Assigned / Accepted / InProgress / Completed / Cancelled
        public string StatusNote { get; set; } = "";

        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? DueUtc { get; set; }
        public DateTimeOffset? UpdatedUtc { get; set; }
    }
}