using OverWatchELD.Models.Economy;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OverWatchELD.Services.Economy
{
    public static class TruckProfitabilityLeaderboardService
    {
        public static List<TruckProfitabilitySummary> BuildTruckProfitability()
        {
            RealDriverEconomyPayrollService.SyncDeliveredLoadsAndPayroll();

            var rows = EconomyStore.LoadTransactions();

            var truckGroups = rows
                .Where(x => !string.IsNullOrWhiteSpace(x.TruckNumber) ||
                            !string.IsNullOrWhiteSpace(x.TruckName))
                .GroupBy(x => TruckKey(x.TruckNumber, x.TruckName), StringComparer.OrdinalIgnoreCase);

            var result = new List<TruckProfitabilitySummary>();

            foreach (var group in truckGroups)
            {
                var list = group.ToList();

                var revenueRows = list
                    .Where(x =>
                        string.Equals(x.Type, "RealLoadPayout", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(x.Type, "LoadPayout", StringComparison.OrdinalIgnoreCase));

                var payrollRows = list
                    .Where(x =>
                        string.Equals(x.Type, "RealDriverPayroll", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(x.Type, "DriverPayroll", StringComparison.OrdinalIgnoreCase));

                var fuelRows = list
                    .Where(x =>
                        string.Equals(x.Category, "Fuel", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(x.Type, "Fuel", StringComparison.OrdinalIgnoreCase));

                var maintenanceRows = list
                    .Where(x =>
                        string.Equals(x.Category, "Maintenance", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(x.Type, "Maintenance", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(x.Type, "AiMaintenanceReserve", StringComparison.OrdinalIgnoreCase));

                var knownCostIds = payrollRows.Select(x => x.Id)
                    .Concat(fuelRows.Select(x => x.Id))
                    .Concat(maintenanceRows.Select(x => x.Id))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var otherCostRows = list
                    .Where(x => x.Amount < 0 && !knownCostIds.Contains(x.Id));

                var summary = new TruckProfitabilitySummary
                {
                    TruckNumber = list.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.TruckNumber))?.TruckNumber ?? "",
                    TruckName = list.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.TruckName))?.TruckName ?? "",
                    PrimaryDriver = list
                        .Where(x => !string.IsNullOrWhiteSpace(x.DriverName))
                        .GroupBy(x => x.DriverName.Trim(), StringComparer.OrdinalIgnoreCase)
                        .OrderByDescending(g => g.Count())
                        .Select(g => g.Key)
                        .FirstOrDefault() ?? "",

                    LoadsDelivered = revenueRows
                        .Select(x => x.LoadNumber)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count(),

                    MilesDriven = EstimateMilesFromNotes(revenueRows.ToList()),

                    GrossRevenue = revenueRows.Sum(x => Math.Max(0, x.Amount)),
                    PayrollCost = payrollRows.Sum(x => x.Amount < 0 ? Math.Abs(x.Amount) : 0),
                    FuelCost = fuelRows.Sum(x => x.Amount < 0 ? Math.Abs(x.Amount) : 0),
                    MaintenanceCost = maintenanceRows.Sum(x => x.Amount < 0 ? Math.Abs(x.Amount) : 0),
                    OtherCost = otherCostRows.Sum(x => Math.Abs(x.Amount)),

                    LastActivityUtc = list
                        .OrderByDescending(x => x.CreatedUtc)
                        .Select(x => (DateTime?)x.CreatedUtc)
                        .FirstOrDefault()
                };

                result.Add(summary);
            }

            return result
                .OrderByDescending(x => x.NetProfit)
                .ThenByDescending(x => x.GrossRevenue)
                .ToList();
        }

        public static List<DriverLeaderboardRow> BuildDriverLeaderboard()
        {
            var summaries = RealDriverEconomyPayrollService.BuildDriverSummaries();

            var rows = summaries
                .Select(x => new DriverLeaderboardRow
                {
                    DriverName = x.DriverName,
                    TruckNumber = x.TruckNumber,
                    TruckName = x.TruckName,
                    LoadsDelivered = x.LoadsDelivered,
                    MilesDriven = x.MilesDriven,
                    GrossRevenue = x.GrossRevenue,
                    PayrollPaid = x.PayrollPaid,
                    CompanyProfit = x.CompanyProfit,
                    LastDeliveryUtc = x.LastDeliveryUtc
                })
                .OrderByDescending(x => x.CompanyProfit)
                .ThenByDescending(x => x.GrossRevenue)
                .ThenByDescending(x => x.LoadsDelivered)
                .ToList();

            for (var i = 0; i < rows.Count; i++)
                rows[i].Rank = i + 1;

            return rows;
        }

        public static List<DriverLeaderboardRow> BuildRevenueLeaderboard()
        {
            var rows = BuildDriverLeaderboard()
                .OrderByDescending(x => x.GrossRevenue)
                .ThenByDescending(x => x.LoadsDelivered)
                .ToList();

            for (var i = 0; i < rows.Count; i++)
                rows[i].Rank = i + 1;

            return rows;
        }

        public static List<DriverLeaderboardRow> BuildPayrollLeaderboard()
        {
            var rows = BuildDriverLeaderboard()
                .OrderByDescending(x => x.PayrollPaid)
                .ThenByDescending(x => x.GrossRevenue)
                .ToList();

            for (var i = 0; i < rows.Count; i++)
                rows[i].Rank = i + 1;

            return rows;
        }

        public static List<TruckProfitabilitySummary> BuildProblemTruckList()
        {
            return BuildTruckProfitability()
                .OrderBy(x => x.NetProfit)
                .ThenByDescending(x => x.MaintenanceCost)
                .Take(25)
                .ToList();
        }

        private static string TruckKey(string? number, string? name)
        {
            if (!string.IsNullOrWhiteSpace(number))
                return "num:" + number.Trim();

            if (!string.IsNullOrWhiteSpace(name))
                return "name:" + name.Trim();

            return "unknown";
        }

        private static double EstimateMilesFromNotes(List<EconomyTransaction> rows)
        {
            double total = 0;

            foreach (var row in rows)
            {
                var notes = row.Notes ?? "";
                var marker = " miles";
                var idx = notes.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

                if (idx <= 0)
                    continue;

                var start = idx - 1;
                while (start >= 0 && (char.IsDigit(notes[start]) || notes[start] == ',' || notes[start] == '.'))
                    start--;

                var text = notes.Substring(start + 1, idx - start - 1).Replace(",", "");

                if (double.TryParse(text, out var miles))
                    total += miles;
            }

            return total;
        }
    }
}
