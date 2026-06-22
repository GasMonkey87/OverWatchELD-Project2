using System;
using System.Collections.Generic;

namespace OverWatchELD.Models.Dispatch
{
    public sealed class DispatchContractAtsExportResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string ContractId { get; set; } = "";
        public string ContractNumber { get; set; } = "";
        public string LoadNumber { get; set; } = "";
        public string DispatchJobId { get; set; } = "";
        public string SavePath { get; set; } = "";
        public string BackupPath { get; set; } = "";
        public string InjectedUnitId { get; set; } = "";
        public List<string> Warnings { get; set; } = new();
        public DateTime ExportedUtc { get; set; } = DateTime.UtcNow;
    }
}
