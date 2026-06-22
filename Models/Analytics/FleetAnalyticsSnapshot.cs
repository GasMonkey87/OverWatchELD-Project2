using System;
using System.Collections.Generic;

namespace OverWatchELD.Models.Analytics
{
    public sealed class FleetAnalyticsSnapshot
    {
        public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;

        public decimal CompanyBalance { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal NetProfit { get; set; }

        public decimal TodayRevenue { get; set; }
        public decimal TodayExpenses { get; set; }
        public decimal TodayProfit { get; set; }

        public decimal WeekRevenue { get; set; }
        public decimal WeekExpenses { get; set; }
        public decimal WeekProfit { get; set; }

        public decimal MonthRevenue { get; set; }
        public decimal MonthExpenses { get; set; }
        public decimal MonthProfit { get; set; }

        public int TotalTransactions { get; set; }
        public int TotalLoadsDelivered { get; set; }
        public int ActiveContracts { get; set; }
        public int CompletedContracts { get; set; }
        public int FailedContracts { get; set; }

        public int TrucksTracked { get; set; }
        public int DriversTracked { get; set; }

        public double TotalMiles { get; set; }
        public decimal RevenuePerMile { get; set; }
        public decimal ProfitPerMile { get; set; }

        public decimal FuelCost { get; set; }
        public decimal MaintenanceCost { get; set; }
        public decimal PayrollCost { get; set; }

        public double AverageDriverScore { get; set; }
        public double AverageSafetyScore { get; set; }

        public List<FleetAnalyticsTrendPoint> DailyTrends { get; set; } = new();
        public List<FleetAnalyticsCategoryRow> ExpenseBreakdown { get; set; } = new();
        public List<FleetAnalyticsCategoryRow> RevenueBreakdown { get; set; } = new();
        public List<FleetAnalyticsRankRow> TopDrivers { get; set; } = new();
        public List<FleetAnalyticsRankRow> TopTrucks { get; set; } = new();
        public List<FleetAnalyticsRankRow> ProblemTrucks { get; set; } = new();
        public List<FleetAnalyticsRankRow> TopContracts { get; set; } = new();
    }

    public sealed class FleetAnalyticsTrendPoint
    {
        public DateTime Date { get; set; }
        public decimal Revenue { get; set; }
        public decimal Expenses { get; set; }
        public decimal Profit => Revenue - Expenses;
        public int Transactions { get; set; }
    }

    public sealed class FleetAnalyticsCategoryRow
    {
        public string Category { get; set; } = "";
        public decimal Amount { get; set; }
        public int Count { get; set; }
    }

    public sealed class FleetAnalyticsRankRow
    {
        public int Rank { get; set; }
        public string Name { get; set; } = "";
        public string Secondary { get; set; } = "";
        public decimal Revenue { get; set; }
        public decimal Cost { get; set; }
        public decimal Profit { get; set; }
        public double Miles { get; set; }
        public int Loads { get; set; }
        public double Score { get; set; }
    }
}
