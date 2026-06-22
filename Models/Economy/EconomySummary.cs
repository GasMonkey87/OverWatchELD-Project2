using System;

namespace OverWatchELD.Models.Economy
{
    public sealed class EconomySummary
    {
        public decimal Balance { get; set; }
        public decimal LifetimeRevenue { get; set; }
        public decimal LifetimeExpenses { get; set; }
        public decimal LifetimeProfit { get; set; }

        public decimal TodayRevenue { get; set; }
        public decimal TodayExpenses { get; set; }
        public decimal TodayProfit => TodayRevenue - TodayExpenses;

        public decimal WeekRevenue { get; set; }
        public decimal WeekExpenses { get; set; }
        public decimal WeekProfit => WeekRevenue - WeekExpenses;

        public decimal MonthRevenue { get; set; }
        public decimal MonthExpenses { get; set; }
        public decimal MonthProfit => MonthRevenue - MonthExpenses;

        public int DeliveredLoadsToday { get; set; }
        public int TransactionsToday { get; set; }
        public int TotalTransactions { get; set; }

        public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;
    }
}
