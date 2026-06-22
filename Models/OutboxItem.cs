using System;
namespace OverWatchELD.Models
{
    public enum OutboxItemType
    {
        Reply = 0,
        DriverStatus = 1,
        Decision = 2
    }

    public sealed class OutboxItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public OutboxItemType Type { get; set; }
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

        public string DriverId { get; set; } = "default";

        public string JsonPayload { get; set; } = "";
        public string EndpointPath { get; set; } = "";
    }
}
