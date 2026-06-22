using System;

namespace OverWatchELD.Models.Economy
{
    public sealed class CompanyAccount
    {
        public string CompanyName { get; set; } = "OverWatch VTC";
        public decimal Balance { get; set; } = 50000m;
        public decimal LifetimeRevenue { get; set; }
        public decimal LifetimeExpenses { get; set; }
        public decimal LifetimeProfit => LifetimeRevenue - LifetimeExpenses;
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }
}
