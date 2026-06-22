using System;
using System.Collections.Generic;
using System.Linq;
using OverWatchELD.Models;

namespace OverWatchELD.Services
{
    /// <summary>
    /// Helper for evaluating whether a given LOCAL log day has any 11/14/70/break violations.
    /// Used to block Daily Log Certification when violations exist.
    /// </summary>
    public static class ComplianceDayService
    {
        /// <summary>
        /// Returns a human-friendly violation summary (empty string if none).
        /// </summary>
        public static string GetViolationSummaryForLocalDay(DateTime localDay)
        {
            try
            {
                var day = localDay.Date;

                // Convert local day window to UTC
                var offset = TimeZoneInfo.Local.GetUtcOffset(day);
                var dayStartLocal = new DateTimeOffset(day, offset);
                var dayEndLocal = dayStartLocal.AddDays(1);

                var startUtc = dayStartLocal.ToUniversalTime();
                var endUtc = dayEndLocal.ToUniversalTime();

                // Pull enough history for cycle calculation
                var historyStartUtc = startUtc.AddDays(-8);

                var events = DatabaseService.GetDutyEvents(historyStartUtc, endUtc) ?? new List<DutyEvent>();
                if (events.Count == 0) return "";

                // Evaluate at the end of the day (just before midnight local)
                var nowUtc = endUtc.AddSeconds(-1);

                // Determine status at evaluation time
                var active = FindActiveEventAt(events, nowUtc) ?? events.OrderByDescending(e => e.StartUtc).FirstOrDefault();
                var status = active?.Status ?? DutyStatus.OffDuty;
                var statusStart = active?.StartUtc ?? startUtc;

                var snap = ComplianceService.Compute(nowUtc, events, status, statusStart);

                var parts = new List<string>();
                if (snap.DriveViolation) parts.Add("11-hour driving limit exceeded");
                if (snap.ShiftViolation) parts.Add("14-hour on-duty window exceeded");
                if (snap.CycleViolation) parts.Add("70-hour cycle limit exceeded");
                if (snap.BreakViolation || snap.IsBreakDue) parts.Add("30-minute break required");

                return string.Join("; ", parts);
            }
            catch
            {
                return "";
            }
        }

        private static DutyEvent? FindActiveEventAt(List<DutyEvent> events, DateTimeOffset utc)
        {
            // An event is active if Start <= t < End (or End is null)
            return events
                .Where(e => e.StartUtc <= utc && (e.EndUtc == null || e.EndUtc > utc))
                .OrderByDescending(e => e.StartUtc)
                .FirstOrDefault();
        }
    }
}
