using System;

namespace OverWatchELD.Models
{
    public sealed class VtcDriver
    {
        public string DriverId { get; set; } = "";
        public string Name { get; set; } = "";

        public string DiscordUserId { get; set; } = "";
        public string TruckNumber { get; set; } = "";
        public string Role { get; set; } = "";
        public string Status { get; set; } = "";
        public string Notes { get; set; } = "";

        public DateTimeOffset? CreatedUtc { get; set; }
        public DateTimeOffset? UpdatedUtc { get; set; }
    }
}