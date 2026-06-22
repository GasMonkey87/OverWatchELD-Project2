using OverWatchELD.Models.Economy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OverWatchELD.Services.Economy
{
    public static class RealDriverEconomyPayrollService
    {
        public static void SyncDeliveredLoadsAndPayroll()
        {
            try
            {
                foreach (var job in DispatchService.Jobs.ToList())
                    TryPostDeliveredLoadEconomy(job);
            }
            catch
            {
            }
        }

        public static bool TryPostDeliveredLoadEconomy(object? job)
        {
            if (job == null)
                return false;

            try
            {
                var loadNumber = FirstNonEmpty(Read(job, "LoadNumber"), Read(job, "Id"));
                if (string.IsNullOrWhiteSpace(loadNumber))
                    return false;

                var status = Read(job, "Status");
                var deliveredUtc = Read(job, "DeliveredUtc");

                var isDelivered =
                    status.Equals("Delivered", StringComparison.OrdinalIgnoreCase) ||
                    status.Equals("BOL Complete", StringComparison.OrdinalIgnoreCase) ||
                    !string.IsNullOrWhiteSpace(deliveredUtc);

                if (!isDelivered)
                    return false;

                var revenue =
                    ReadDecimal(job, "RevenueUsd") ??
                    ReadDecimal(job, "Payout") ??
                    ReadDecimal(job, "Pay") ??
                    EstimateRevenue(job);

                if (revenue <= 0)
                    revenue = EstimateRevenue(job);

                var driver = FirstNonEmpty(
                    Read(job, "AssignedDriver"),
                    Read(job, "ClaimedBy"),
                    Read(job, "DriverName"),
                    "Unknown Driver");

                var driverId = FirstNonEmpty(
                    Read(job, "AssignedDriverDiscordId"),
                    Read(job, "ClaimedByDiscordId"),
                    Read(job, "DriverDiscordId"));

                var truckName = FirstNonEmpty(
                    Read(job, "AssignedTruck"),
                    Read(job, "TruckName"),
                    Read(job, "LastKnownTruckName"));

                var truckNumber = FirstNonEmpty(
                    Read(job, "TruckNumber"),
                    Read(job, "UnitNumber"));

                var cargo = Read(job, "Cargo");
                var miles = ReadDouble(job, "ActualDrivenMiles") ?? ReadDouble(job, "Miles") ?? 0;

                var loadPayoutType = "RealLoadPayout";
                var payrollType = "RealDriverPayroll";

                if (!EconomyStore.HasTransactionForLoad(loadNumber, loadPayoutType))
                {
                    EconomyStore.AddTransaction(new EconomyTransaction
                    {
                        Type = loadPayoutType,
                        Category = "Dispatch Revenue",
                        Source = "Real Driver Economy",
                        Amount = revenue,
                        LoadNumber = loadNumber,
                        DriverName = driver,
                        TruckNumber = truckNumber,
                        TruckName = truckName,
                        Description = $"Delivered load {loadNumber}",
                        Notes = $"{cargo} • {miles:N0} miles"
                    });
                }

                if (!EconomyStore.HasTransactionForLoad(loadNumber, payrollType))
                {
                    var profile = RealDriverPayrollStore.GetOrCreate(driver, driverId);

                    if (profile.Enabled)
                    {
                        var payroll = CalculatePayroll(profile, revenue, miles);

                        if (payroll > 0)
                        {
                            EconomyStore.AddTransaction(new EconomyTransaction
                            {
                                Type = payrollType,
                                Category = "Driver Payroll",
                                Source = "Real Driver Economy",
                                Amount = -payroll,
                                LoadNumber = loadNumber,
                                DriverName = driver,
                                TruckNumber = truckNumber,
                                TruckName = truckName,
                                Description = $"Driver payroll for {driver} / load {loadNumber}",
                                Notes = $"{profile.PayMode} pay • Gross load revenue {revenue:C0}"
                            });
                        }
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static List<RealDriverEconomySummary> BuildDriverSummaries()
        {
            SyncDeliveredLoadsAndPayroll();

            var rows = EconomyStore.LoadTransactions();

            var drivers = rows
                .Where(x => !string.IsNullOrWhiteSpace(x.DriverName))
                .GroupBy(x => x.DriverName.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var driverRows = g.ToList();

                    var gross = driverRows
                        .Where(x => string.Equals(x.Type, "RealLoadPayout", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(x.Type, "LoadPayout", StringComparison.OrdinalIgnoreCase))
                        .Sum(x => x.Amount > 0 ? x.Amount : 0);

                    var payroll = driverRows
                        .Where(x => string.Equals(x.Type, "RealDriverPayroll", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(x.Type, "DriverPayroll", StringComparison.OrdinalIgnoreCase))
                        .Sum(x => x.Amount < 0 ? Math.Abs(x.Amount) : 0);

                    var deliveredLoads = driverRows
                        .Where(x => string.Equals(x.Type, "RealLoadPayout", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(x.Type, "LoadPayout", StringComparison.OrdinalIgnoreCase))
                        .Select(x => x.LoadNumber)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count();

                    return new RealDriverEconomySummary
                    {
                        DriverName = g.Key,
                        TruckName = driverRows.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.TruckName))?.TruckName ?? "",
                        TruckNumber = driverRows.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.TruckNumber))?.TruckNumber ?? "",
                        GrossRevenue = gross,
                        PayrollPaid = payroll,
                        CompanyProfit = gross - payroll,
                        LoadsDelivered = deliveredLoads,
                        MilesDriven = EstimateMilesFromNotes(driverRows),
                        LastDeliveryUtc = driverRows
                            .Where(x => string.Equals(x.Type, "RealLoadPayout", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(x.Type, "LoadPayout", StringComparison.OrdinalIgnoreCase))
                            .OrderByDescending(x => x.CreatedUtc)
                            .Select(x => (DateTime?)x.CreatedUtc)
                            .FirstOrDefault(),
                        GeneratedUtc = DateTime.UtcNow
                    };
                })
                .OrderByDescending(x => x.GrossRevenue)
                .ThenBy(x => x.DriverName)
                .ToList();

            return drivers;
        }

        public static decimal CalculatePayroll(RealDriverPayrollProfile profile, decimal loadRevenue, double miles)
        {
            if (profile == null || !profile.Enabled)
                return 0m;

            var payMode = (profile.PayMode ?? "Percent").Trim();

            decimal pay = payMode.ToLowerInvariant() switch
            {
                "permile" or "per mile" => Math.Round((decimal)miles * (profile.CentsPerMile / 100m), 2),
                "flatperload" or "flat per load" => profile.FlatPerLoad,
                _ => Math.Round(loadRevenue * (profile.PercentOfLoad / 100m), 2)
            };

            if (profile.SafetyBonusPercent > 0)
                pay += Math.Round(pay * (profile.SafetyBonusPercent / 100m), 2);

            return Math.Max(0, pay);
        }

        public static void SetDriverPayProfile(
            string driverName,
            string payMode,
            decimal percentOfLoad,
            decimal centsPerMile,
            decimal flatPerLoad,
            bool enabled)
        {
            var profile = RealDriverPayrollStore.GetOrCreate(driverName);
            profile.PayMode = payMode;
            profile.PercentOfLoad = percentOfLoad;
            profile.CentsPerMile = centsPerMile;
            profile.FlatPerLoad = flatPerLoad;
            profile.Enabled = enabled;
            RealDriverPayrollStore.Upsert(profile);
        }

        private static decimal EstimateRevenue(object job)
        {
            var miles = ReadDouble(job, "ActualDrivenMiles") ?? ReadDouble(job, "Miles") ?? 0;
            if (miles <= 0)
                return 1500m;

            return Math.Round((decimal)miles * 4.25m, 2);
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

        private static string Read(object obj, string propertyName)
        {
            try
            {
                var prop = obj.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                return prop?.GetValue(obj)?.ToString()?.Trim() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static decimal? ReadDecimal(object obj, params string[] names)
        {
            foreach (var name in names)
            {
                var raw = Read(obj, name)
                    .Replace("$", "")
                    .Replace(",", "")
                    .Trim();

                if (decimal.TryParse(raw, out var value))
                    return value;
            }

            return null;
        }

        private static double? ReadDouble(object obj, params string[] names)
        {
            foreach (var name in names)
            {
                var raw = Read(obj, name)
                    .Replace(",", "")
                    .Trim();

                if (double.TryParse(raw, out var value))
                    return value;
            }

            return null;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }
    }
}
