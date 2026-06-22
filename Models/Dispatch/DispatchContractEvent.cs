using System;

namespace OverWatchELD.Models.Dispatch
{
    public sealed class DispatchContractEvent
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string ContractId { get; set; } = "";
        public string ContractNumber { get; set; } = "";
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public string EventType { get; set; } = "";
        public string Message { get; set; } = "";
        public string LoadNumber { get; set; } = "";
        public decimal Amount { get; set; }
    }
}
