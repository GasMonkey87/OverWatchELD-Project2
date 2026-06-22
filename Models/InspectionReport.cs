using System;
using System.Collections.Generic;

namespace OverWatchELD.Models
{
    public sealed class InspectionReport
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public DateTimeOffset SubmittedUtc { get; set; } = DateTimeOffset.UtcNow;

        // "PreTrip" | "PostTrip" | "Vehicle"
        public string Type { get; set; } = "PreTrip";

        public string DriverName { get; set; } = "";
        public string VehicleId { get; set; } = "";

        public string Signature { get; set; } = "";
        public bool Certified { get; set; }

        public bool HasDefects { get; set; }
        public List<string> Defects { get; set; } = new();

        public List<ItemLine> Items { get; set; } = new();

        public string Notes { get; set; } = "";

        public sealed class ItemLine
        {
            public string Category { get; set; } = "";
            public string Name { get; set; } = "";
            public bool IsOk { get; set; }
            public bool IsDefect { get; set; }
            public string Note { get; set; } = "";
        }
    }
}