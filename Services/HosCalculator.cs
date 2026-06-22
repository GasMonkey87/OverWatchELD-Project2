using System;
using System.Collections.Generic;
using System.Linq;
using OverWatchELD.Models;

namespace OverWatchELD.Services
{
    /// <summary>
    /// Thin wrapper so older parts of the app can keep calling HosCalculator,
    /// while the actual FMCSA logic lives in ComplianceService.
    /// </summary>
    public static class HosCalculator
    {
        // Manual override reset (UI button) – does NOT change the duty log.
        private static DateTimeOffset? _manualResetUtc;

        public static void ManualResetAllClocks()
        {
            _manualResetUtc = DateTimeOffset.UtcNow;
        }

        public static void ClearManualReset()
        {
            _manualResetUtc = null;
        }

        /// <summary>
        /// Current clocks snapshot (FMCSA-ish) based on duty events + current open status.
        /// </summary>
        public static HosSnapshot GetCurrentClocks(IReadOnlyList<DutyEvent> events, DateTimeOffset nowUtc)
        {
            nowUtc = nowUtc.ToUniversalTime();

            var list = (events ?? Array.Empty<DutyEvent>())
                .Where(e => e != null)
                .Select(e => new DutyEvent
                {
                    Id = e.Id,
                    StartUtc = e.StartUtc.ToUniversalTime(),
                    EndUtc = e.EndUtc?.ToUniversalTime(),
                    Status = e.Status,
                    LocationText = e.LocationText,
                    Notes = e.Notes,
                    Source = e.Source,
                    IsEdited = e.IsEdited,
                    EditedAtUtc = e.EditedAtUtc?.ToUniversalTime(),
                    EditReason = e.EditReason,
                    Lat = e.Lat,
                    Lon = e.Lon
                })
                .OrderBy(e => e.StartUtc)
                .ToList();

            // Derive current status + start
            DutyStatus curStatus;
            DateTimeOffset curStart;

            var open = list.LastOrDefault(e => e.EndUtc == null);
            if (open != null)
            {
                curStatus = open.Status;
                curStart = open.StartUtc;
            }
            else if (list.Count > 0)
            {
                var last = list[^1];
                curStatus = last.Status;
                curStart = last.EndUtc ?? last.StartUtc;
            }
            else
            {
                // No events at all: assume OFF now
                curStatus = DutyStatus.OffDuty;
                curStart = nowUtc;
            }

            // Apply manual reset override (display-only)
            if (_manualResetUtc.HasValue)
            {
                var mr = _manualResetUtc.Value.ToUniversalTime();
                if (mr > curStart) curStart = mr;
            }

            // Let ComplianceService do the heavy lifting
            var snapshot = ComplianceService.Compute(nowUtc, list, curStatus, curStart);

            // If manual reset is active, optionally force the user-facing clocks "full"
            // (still display-only)
            if (_manualResetUtc.HasValue)
            {
                snapshot = new HosSnapshot
                {
                    ShiftRemaining = TimeSpan.FromHours(14),
                    DriveRemaining = TimeSpan.FromHours(11),
                    BreakRemaining = TimeSpan.FromHours(8),
                    CycleRemaining = TimeSpan.FromHours(70),

                    BreakRequired = false,
                    IsBreakDue = false,

                    DriveViolation = false,
                    ShiftViolation = false,
                    BreakViolation = false,
                    CycleViolation = false,

                    ShouldPulse = false,
                    Notes = "Manual reset applied (display-only).",

                    DriveUsedPct = 0,
                    ShiftUsedPct = 0,
                    BreakUsedPct = 0,
                    CycleUsedPct = 0
                };
            }

            return snapshot;
        }
    }
}
