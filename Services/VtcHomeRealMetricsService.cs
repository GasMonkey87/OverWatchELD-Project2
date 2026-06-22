using OverWatchELD.Models.Economy;
using OverWatchELD.Services.Economy;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace OverWatchELD.Services
{
    public sealed class VtcHomeMileageSummary
    {
        public double WeeklyMiles { get; set; }
        public double MonthlyMiles { get; set; }
        public int WeeklyDeliveredLoads { get; set; }
        public int MonthlyDeliveredLoads { get; set; }
        public string Source { get; set; } = "";
    }

    public static class VtcHomeRealMetricsService
    {
        public static VtcHomeMileageSummary BuildMileageSummary()
        {
            var now = DateTime.UtcNow;
            var weekStart = StartOfWeekUtc(now);
            var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            var fromDispatch = BuildFromDispatchJobs(weekStart, monthStart);

            // Prefer Dispatch jobs because they are the real load source.
            if (fromDispatch.MonthlyMiles > 0 || fromDispatch.MonthlyDeliveredLoads > 0)
                return fromDispatch;

            // Fallback for older installs where economy transactions already exist
            // but DispatchService.Jobs has not loaded the historical delivered jobs yet.
            return BuildFromEconomyTransactions(weekStart, monthStart);
        }

        private static VtcHomeMileageSummary BuildFromDispatchJobs(DateTime weekStart, DateTime monthStart)
        {
            var rows = DispatchService.Jobs
                .Where(IsDelivered)
                .Select(job => new
                {
                    DateUtc = BestJobDateUtc(job),
                    Miles = BestJobMiles(job)
                })
                .Where(x => x.DateUtc.HasValue)
                .ToList();

            var weeklyRows = rows.Where(x => x.DateUtc!.Value >= weekStart).ToList();
            var monthlyRows = rows.Where(x => x.DateUtc!.Value >= monthStart).ToList();

            return new VtcHomeMileageSummary
            {
                WeeklyMiles = weeklyRows.Sum(x => x.Miles),
                MonthlyMiles = monthlyRows.Sum(x => x.Miles),
                WeeklyDeliveredLoads = weeklyRows.Count,
                MonthlyDeliveredLoads = monthlyRows.Count,
                Source = "Dispatch"
            };
        }

        private static VtcHomeMileageSummary BuildFromEconomyTransactions(DateTime weekStart, DateTime monthStart)
        {
            var tx = EconomyStore.LoadTransactions()
                .Where(x => IsLoadRevenueTransaction(x))
                .Select(x => new
                {
                    x.CreatedUtc,
                    Miles = ExtractMiles(x.Notes)
                })
                .Where(x => x.Miles > 0)
                .ToList();

            var weeklyRows = tx.Where(x => x.CreatedUtc >= weekStart).ToList();
            var monthlyRows = tx.Where(x => x.CreatedUtc >= monthStart).ToList();

            return new VtcHomeMileageSummary
            {
                WeeklyMiles = weeklyRows.Sum(x => x.Miles),
                MonthlyMiles = monthlyRows.Sum(x => x.Miles),
                WeeklyDeliveredLoads = weeklyRows.Count,
                MonthlyDeliveredLoads = monthlyRows.Count,
                Source = "Economy"
            };
        }

        private static bool IsDelivered(DispatchJob job)
        {
            if (job == null)
                return false;

            if (job.DeliveredUtc.HasValue)
                return true;

            return string.Equals(job.Status, "Delivered", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(job.Status, "BOL Complete", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(job.Status, "Completed", StringComparison.OrdinalIgnoreCase);
        }

        private static DateTime? BestJobDateUtc(DispatchJob job)
        {
            if (job.DeliveredUtc.HasValue)
                return EnsureUtc(job.DeliveredUtc.Value);

            if (job.LastStatusChangeUtc.HasValue)
                return EnsureUtc(job.LastStatusChangeUtc.Value);

            if (job.UpdatedUtc != default)
                return EnsureUtc(job.UpdatedUtc);

            if (job.CreatedUtc != default)
                return EnsureUtc(job.CreatedUtc);

            return null;
        }

        private static DateTime EnsureUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Utc)
                return value;

            if (value.Kind == DateTimeKind.Local)
                return value.ToUniversalTime();

            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        private static double BestJobMiles(DispatchJob job)
        {
            if (job.ActualDrivenMiles > 0)
                return job.ActualDrivenMiles;

            if (job.Miles > 0)
                return job.Miles;

            if (job.StartOdometerMiles.HasValue && job.LastKnownOdometerMiles > job.StartOdometerMiles.Value)
                return job.LastKnownOdometerMiles - job.StartOdometerMiles.Value;

            return 0;
        }

        private static bool IsLoadRevenueTransaction(EconomyTransaction tx)
        {
            if (tx == null)
                return false;

            if (tx.Amount <= 0)
                return false;

            return Contains(tx.Type, "LoadPayout") ||
                   Contains(tx.Type, "RealLoadPayout") ||
                   Contains(tx.Type, "ContractLoadRevenue") ||
                   Contains(tx.Category, "Dispatch") ||
                   Contains(tx.Category, "Contract");
        }

        private static double ExtractMiles(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            var match = Regex.Match(text, @"(?<miles>[0-9][0-9,]*(?:\.[0-9]+)?)\s*miles", RegexOptions.IgnoreCase);

            if (!match.Success)
                return 0;

            var raw = match.Groups["miles"].Value.Replace(",", "");

            return double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0;
        }

        private static bool Contains(string? haystack, string needle)
        {
            return !string.IsNullOrWhiteSpace(haystack) &&
                   haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
        }

        private static DateTime StartOfWeekUtc(DateTime utcNow)
        {
            var date = utcNow.Date;
            var diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
            return date.AddDays(-diff);
        }
    }
}
