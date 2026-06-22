using System;
using System.Collections.Generic;

namespace OverWatchELD.Services.ATS
{
    public sealed class AtsModScanLoadCandidate
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string SourceFile { get; set; } = "";
        public string SourceModName { get; set; } = "";
        public string CargoToken { get; set; } = "";
        public string CargoName { get; set; } = "";
        public string TrailerToken { get; set; } = "";
        public string TrailerName { get; set; } = "";
        public int WeightLbs { get; set; }
        public string PickupCity { get; set; } = "";
        public string DeliveryCity { get; set; } = "";
        public string CompanyFrom { get; set; } = "";
        public string CompanyTo { get; set; } = "";
        public bool IsModded => !string.IsNullOrWhiteSpace(SourceModName);
    }

    public sealed class AtsCreatedLoad
    {
        public string LoadNumber { get; set; } = "";
        public string CargoName { get; set; } = "";
        public string TrailerName { get; set; } = "";
        public string SourceModName { get; set; } = "";
        public int WeightLbs { get; set; }
        public string PickupCity { get; set; } = "";
        public string DeliveryCity { get; set; } = "";
        public string CompanyFrom { get; set; } = "";
        public string CompanyTo { get; set; } = "";
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public Dictionary<string, string> AtsTokens { get; set; } = new();
    }

    public sealed class AtsSaveInjectionResult
    {
        public bool Ok { get; set; }
        public string Message { get; set; } = "";
        public string SaveFolder { get; set; } = "";
        public string BackupPath { get; set; } = "";
        public string ExportPath { get; set; } = "";
    }
}
