using System;

namespace OverWatchELD.Models
{
    public sealed class InspectionRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string InspectionNumber { get; set; } = "";
        public string InspectionType { get; set; } = "Pre-Trip";
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public string DriverName { get; set; } = "";
        public string DriverDiscordId { get; set; } = "";
        public string TruckId { get; set; } = "";
        public string UnitNumber { get; set; } = "";
        public string TruckName { get; set; } = "";
        public string PlateNumber { get; set; } = "";
        public string Location { get; set; } = "";

        public bool Passed { get; set; } = true;
        public string Defects { get; set; } = "";
        public string Notes { get; set; } = "";

        public string CreatedLocalDisplay => CreatedUtc.ToLocalTime().ToString("g");
        public string StatusText => Passed ? "Passed" : "Defects Found";
    }
}