using OverWatchELD.Models.Analytics;
using OverWatchELD.Services.Dispatch;
using OverWatchELD.Services.Economy;
using OverWatchELD.Services.Performance;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OverWatchELD.Services.Analytics
{
    public static class FleetAnalyticsService
    {
        public static FleetAnalyticsSnapshot BuildSnapshot()
        {
            TryRefreshUpstream();

            var economy = EconomyStore.BuildSummary();
            var tx = EconomyStore.LoadTransactions();

            var drivers = Safe(() => RealDriverEconomyPayrollService.BuildDriverSummaries(), new());
            var trucks = Safe(() => TruckProfitabilityLeaderboardService.BuildTruckProfitability(), new());
            var scores = Safe(() => DriverSafetyPerformanceService.BuildLeaderboard(), new());
            var contracts = Safe(() => DispatchContractService.LoadContracts(), new());

            var totalMiles = drivers.Sum(x => x.MilesDriven);

            var snapshot = new FleetAnalyticsSnapshot
            {
                GeneratedUtc = DateTime.UtcNow,
                CompanyBalance = economy.Balance,
                TotalRevenue = economy.LifetimeRevenue,
                TotalExpenses = economy.LifetimeExpenses,
                NetProfit = economy.LifetimeProfit,
                TodayRevenue = economy.TodayRevenue,
                TodayExpenses = economy.TodayExpenses,
                TodayProfit = economy.TodayProfit,
                WeekRevenue = economy.WeekRevenue,
                WeekExpenses = economy.WeekExpenses,
                WeekProfit = economy.WeekProfit,
                MonthRevenue = economy.MonthRevenue,
                MonthExpenses = economy.MonthExpenses,
                MonthProfit = economy.MonthProfit,
                TotalTransactions = economy.TotalTransactions,
                TotalLoadsDelivered = drivers.Sum(x => x.LoadsDelivered),
                ActiveContracts = contracts.Count(x => Same(x.Status, "Active")),
                CompletedContracts = contracts.Count(x => Same(x.Status, "Completed")),
                FailedContracts = contracts.Count(x => Same(x.Status, "Failed")),
                TrucksTracked = trucks.Count,
                DriversTracked = drivers.Count,
                TotalMiles = totalMiles,
                RevenuePerMile = totalMiles > 0 ? economy.LifetimeRevenue / (decimal)totalMiles : 0,
                ProfitPerMile = totalMiles > 0 ? economy.LifetimeProfit / (decimal)totalMiles : 0,
                FuelCost = tx.Where(x => Same(x.Category, "Fuel") || Same(x.Type, "AutoFuelExpense")).Where(x => x.Amount < 0).Sum(x => Math.Abs(x.Amount)),
                MaintenanceCost = tx.Where(x => Same(x.Category, "Maintenance") || x.Type.Contains("Maintenance", StringComparison.OrdinalIgnoreCase) || x.Type.Contains("Repair", StringComparison.OrdinalIgnoreCase) || x.Type.Contains("Wear", StringComparison.OrdinalIgnoreCase)).Where(x => x.Amount < 0).Sum(x => Math.Abs(x.Amount)),
                PayrollCost = tx.Where(x => Same(x.Category, "Payroll") || x.Type.Contains("Payroll", StringComparison.OrdinalIgnoreCase)).Where(x => x.Amount < 0).Sum(x => Math.Abs(x.Amount)),
                AverageDriverScore = scores.Count > 0 ? Math.Round(scores.Average(x => x.OverallScore), 1) : 0,
                AverageSafetyScore = scores.Count > 0 ? Math.Round(scores.Average(x => x.SafetyScore), 1) : 0
            };

            snapshot.DailyTrends = BuildDailyTrends(tx, 30);
            snapshot.ExpenseBreakdown = BuildExpenseBreakdown(tx);
            snapshot.RevenueBreakdown = BuildRevenueBreakdown(tx);
            snapshot.TopDrivers = BuildTopDrivers(drivers);
            snapshot.TopTrucks = BuildTopTrucks(trucks);
            snapshot.ProblemTrucks = BuildProblemTrucks(trucks);
            snapshot.TopContracts = BuildTopContracts(tx);

            return snapshot;
        }

        private static void TryRefreshUpstream()
        {
            try { RealDriverEconomyPayrollService.SyncDeliveredLoadsAndPayroll(); } catch { }
            try { DispatchContractAtsIntegrationService.SyncDeliveredContractLoads(); } catch { }
            try { FuelMaintenanceAutomationService.ProcessCurrentTelemetryIfAvailable(); } catch { }
        }

        private static List<FleetAnalyticsTrendPoint> BuildDailyTrends(List<Models.Economy.EconomyTransaction> tx, int days)
        {
            var start = DateTime.UtcNow.Date.AddDays(-(days - 1));
            var rows = new List<FleetAnalyticsTrendPoint>();

            for (var i = 0; i < days; i++)
            {
                var day = start.AddDays(i);
                var next = day.AddDays(1);
                var dayRows = tx.Where(x => x.CreatedUtc >= day && x.CreatedUtc < next).ToList();

                rows.Add(new FleetAnalyticsTrendPoint
                {
                    Date = day,
                    Revenue = dayRows.Where(x => x.Amount > 0).Sum(x => x.Amount),
                    Expenses = dayRows.Where(x => x.Amount < 0).Sum(x => Math.Abs(x.Amount)),
                    Transactions = dayRows.Count
                });
            }

            return rows;
        }

        private static List<FleetAnalyticsCategoryRow> BuildExpenseBreakdown(List<Models.Economy.EconomyTransaction> tx)
        {
            return tx.Where(x => x.Amount < 0)
                .GroupBy(x => CleanCategory(x.Category, x.Type), StringComparer.OrdinalIgnoreCase)
                .Select(g => new FleetAnalyticsCategoryRow { Category = g.Key, Amount = g.Sum(x => Math.Abs(x.Amount)), Count = g.Count() })
                .OrderByDescending(x => x.Amount)
                .ToList();
        }

        private static List<FleetAnalyticsCategoryRow> BuildRevenueBreakdown(List<Models.Economy.EconomyTransaction> tx)
        {
            return tx.Where(x => x.Amount > 0)
                .GroupBy(x => CleanCategory(x.Category, x.Type), StringComparer.OrdinalIgnoreCase)
                .Select(g => new FleetAnalyticsCategoryRow { Category = g.Key, Amount = g.Sum(x => x.Amount), Count = g.Count() })
                .OrderByDescending(x => x.Amount)
                .ToList();
        }

        private static List<FleetAnalyticsRankRow> BuildTopDrivers(List<Models.Economy.RealDriverEconomySummary> drivers)
        {
            return drivers.OrderByDescending(x => x.CompanyProfit).ThenByDescending(x => x.GrossRevenue).Take(25)
                .Select((x, i) => new FleetAnalyticsRankRow
                {
                    Rank = i + 1,
                    Name = x.DriverName,
                    Secondary = FirstNonEmpty(x.TruckNumber, x.TruckName),
                    Revenue = x.GrossRevenue,
                    Cost = x.PayrollPaid,
                    Profit = x.CompanyProfit,
                    Miles = x.MilesDriven,
                    Loads = x.LoadsDelivered
                }).ToList();
        }

        private static List<FleetAnalyticsRankRow> BuildTopTrucks(List<Models.Economy.TruckProfitabilitySummary> trucks)
        {
            return trucks.OrderByDescending(x => x.NetProfit).ThenByDescending(x => x.GrossRevenue).Take(25)
                .Select((x, i) => new FleetAnalyticsRankRow
                {
                    Rank = i + 1,
                    Name = FirstNonEmpty(x.TruckNumber, x.TruckName, "Unknown Truck"),
                    Secondary = x.PrimaryDriver,
                    Revenue = x.GrossRevenue,
                    Cost = x.TotalCost,
                    Profit = x.NetProfit,
                    Miles = x.MilesDriven,
                    Loads = x.LoadsDelivered
                }).ToList();
        }

        private static List<FleetAnalyticsRankRow> BuildProblemTrucks(List<Models.Economy.TruckProfitabilitySummary> trucks)
        {
            return trucks.OrderBy(x => x.NetProfit).ThenByDescending(x => x.MaintenanceCost).Take(25)
                .Select((x, i) => new FleetAnalyticsRankRow
                {
                    Rank = i + 1,
                    Name = FirstNonEmpty(x.TruckNumber, x.TruckName, "Unknown Truck"),
                    Secondary = x.PrimaryDriver,
                    Revenue = x.GrossRevenue,
                    Cost = x.TotalCost,
                    Profit = x.NetProfit,
                    Miles = x.MilesDriven,
                    Loads = x.LoadsDelivered
                }).ToList();
        }

        private static List<FleetAnalyticsRankRow> BuildTopContracts(List<Models.Economy.EconomyTransaction> tx)
        {
            var rows = tx.Where(x => x.Type.Contains("Contract", StringComparison.OrdinalIgnoreCase) || x.Category.Contains("Contract", StringComparison.OrdinalIgnoreCase))
                .Where(x => x.Amount > 0)
                .GroupBy(x => FirstNonEmpty(x.Description, x.Notes, "Contract"), StringComparer.OrdinalIgnoreCase)
                .Select(g => new FleetAnalyticsRankRow
                {
                    Name = g.Key,
                    Revenue = g.Sum(x => x.Amount),
                    Profit = g.Sum(x => x.Amount),
                    Loads = g.Select(x => x.LoadNumber).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                })
                .OrderByDescending(x => x.Revenue)
                .Take(25)
                .ToList();

            for (var i = 0; i < rows.Count; i++) rows[i].Rank = i + 1;
            return rows;
        }

        private static string CleanCategory(string? category, string? fallback)
        {
            var c = FirstNonEmpty(category, fallback, "Other");
            if (c.Contains("Payroll", StringComparison.OrdinalIgnoreCase)) return "Payroll";
            if (c.Contains("Fuel", StringComparison.OrdinalIgnoreCase)) return "Fuel";
            if (c.Contains("Maintenance", StringComparison.OrdinalIgnoreCase) || c.Contains("Repair", StringComparison.OrdinalIgnoreCase) || c.Contains("Wear", StringComparison.OrdinalIgnoreCase)) return "Maintenance";
            if (c.Contains("Contract", StringComparison.OrdinalIgnoreCase)) return "Contracts";
            if (c.Contains("Dispatch", StringComparison.OrdinalIgnoreCase)) return "Dispatch";
            if (c.Contains("Garage", StringComparison.OrdinalIgnoreCase)) return "Garages";
            return c;
        }

        private static T Safe<T>(Func<T> func, T fallback)
        {
            try { return func(); } catch { return fallback; }
        }

        private static bool Same(string? a, string b) => string.Equals((a ?? "").Trim(), b, StringComparison.OrdinalIgnoreCase);

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            return "";
        }
    }
}
