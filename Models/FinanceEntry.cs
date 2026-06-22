using System;

namespace OverWatchELD.Models
{
    public class FinanceEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public DateTime DateUtc { get; set; } = DateTime.UtcNow;

        // "Income" or "Expense"
        public string EntryType { get; set; } = "Expense";

        // Fuel, Maintenance, Repairs, Delivery Revenue, etc.
        public string Category { get; set; } = "";

        public decimal Amount { get; set; }

        public string TruckId { get; set; } = "";
        public string TruckName { get; set; } = "";

        public string DiscordUserId { get; set; } = "";
        public string DiscordUsername { get; set; } = "";

        public string Description { get; set; } = "";
        public string EnteredBy { get; set; } = "";
        public string Source { get; set; } = "Manual";
    }
}