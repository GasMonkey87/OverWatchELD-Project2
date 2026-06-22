using System;
using DutyStatus = OverWatchELD.Models.DutyStatus;
using System.Collections.Generic;
using System.Linq;
using OverWatchELD.Models;

namespace OverWatchELD.Services
{
    public sealed class HosSegmentTrace
    {
        public DateTimeOffset StartUtc { get; set; }
        public DateTimeOffset EndUtc { get; set; }
        public DutyStatus Status { get; set; }

        public TimeSpan DriveInShift { get; set; }
        public TimeSpan DriveSinceBreak { get; set; }
        public TimeSpan Paused14h { get; set; }

        public string? Marker { get; set; } // e.g. "TRIP: 11H", "TRIP: 14H", "TRIP: 8H BREAK"
    }

    public sealed class HosViolationResult
    {
        public bool Drive11Violation { get; set; }
        public bool Shift14Violation { get; set; }
        public bool Break30Violation { get; set; }

        public DateTimeOffset? ShiftStartUtc { get; set; }
        public DateTimeOffset? WindowEndUtc { get; set; }
        public TimeSpan Paused14h { get; set; }
        public DateTimeOffset? EffectiveWindowEndUtc { get; set; }

        public TimeSpan DriveInShift { get; set; }
        public TimeSpan DriveSinceBreak { get; set; }

        public DateTimeOffset? FirstDrive11AtUtc { get; set; }
        public DateTimeOffset? FirstShift14AtUtc { get; set; }
        public DateTimeOffset? FirstBreak8AtUtc { get; set; }

        public List<HosSegmentTrace> Trace { get; } = new();

        public bool HasAny => Drive11Violation || Shift14Violation || Break30Violation;

        public string ToText()
        {
            var parts = new List<string>();
            if (Drive11Violation) parts.Add("DRIVE > 11h");
            if (Shift14Violation) parts.Add("DRIVE after 14h window");
            if (Break30Violation) parts.Add("No 30-min break (8h rule)");
            return parts.Count == 0 ? "OK" : string.Join(" • ", parts);
        }

        public string BuildTriggerText(TimeZoneInfo? tz = null)
        {
            tz ??= TimeZoneInfo.Local;

            var lines = new List<string>();

            if (ShiftStartUtc != null)
                lines.Add($"Shift start: {ToLocal(tz, ShiftStartUtc.Value):MM/dd HH:mm}");

            if (FirstDrive11AtUtc != null)
                lines.Add($"First DRIVE>11: {ToLocal(tz, FirstDrive11AtUtc.Value):MM/dd HH:mm}");

            if (FirstBreak8AtUtc != null)
                lines.Add($"First 8h-break violation: {ToLocal(tz, FirstBreak8AtUtc.Value):MM/dd HH:mm}");

            if (FirstShift14AtUtc != null)
                lines.Add($"First 14h-window violation: {ToLocal(tz, FirstShift14AtUtc.Value):MM/dd HH:mm}");

            if (EffectiveWindowEndUtc != null)
                lines.Add($"14h deadline (effective): {ToLocal(tz, EffectiveWindowEndUtc.Value):MM/dd HH:mm} (paused {Fmt(Paused14h)})");

            lines.Add($"Drive in shift: {Fmt(DriveInShift)}");
            lines.Add($"Drive since last 30m break: {Fmt(DriveSinceBreak)}");

            return string.Join(Environment.NewLine, lines);
        }

        public string BuildTraceText(TimeZoneInfo? tz = null)
        {
            tz ??= TimeZoneInfo.Local;
            if (Trace.Count == 0) return "(no trace)";

            // Keep it compact but useful
            var lines = new List<string>();
            foreach (var t in Trace)
            {
                var s = ToLocal(tz, t.StartUtc);
                var e = ToLocal(tz, t.EndUtc);

                var marker = string.IsNullOrWhiteSpace(t.Marker) ? "" : $"  [{t.Marker}]";

                lines.Add(
                    $"{s:MM/dd HH:mm} - {e:MM/dd HH:mm}  {t.Status,-16}  " +
                    $"DShift {Fmt(t.DriveInShift)}  DBreak {Fmt(t.DriveSinceBreak)}  Pause {Fmt(t.Paused14h)}{marker}"
                );
            }
            return string.Join(Environment.NewLine, lines);
        }

        private static DateTime ToLocal(TimeZoneInfo tz, DateTimeOffset utc)
            => TimeZoneInfo.ConvertTimeFromUtc(utc.UtcDateTime, tz);

        private static string Fmt(TimeSpan ts)
        {
            if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;
            var totalMin = (long)Math.Floor(ts.TotalMinutes);
            var h = totalMin / 60;
            var m = totalMin % 60;
            return $"{h:00}:{m:00}";
        }
    }

    public static class HosViolationAnalyzer
    {
        // PC counts as OFF for rest; YM counts as ON
        private static bool IsRest(DutyStatus s) =>
            s == DutyStatus.OffDuty ||
            s == DutyStatus.Sleeper ||
            s == DutyStatus.PersonalConveyance;

        private static bool IsSleeper(DutyStatus s) => s == DutyStatus.Sleeper;
        private static bool IsDriving(DutyStatus s) => s == DutyStatus.Driving;

        private static bool IsOnDutyNotDriving(DutyStatus s) =>
            s == DutyStatus.OnDuty ||
            s == DutyStatus.YardMove;

        private static bool IsNonDriving(DutyStatus s) => !IsDriving(s);

        private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
        private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

        public static HosViolationResult AnalyzeDayLocal(DateTime dayLocal, IEnumerable<DutyEvent> allEvents)
        {
            var tz = TimeZoneInfo.Local;

            var dayStartLocal = dayLocal.Date;
            var dayEndLocal = dayStartLocal.AddDays(1);

            var dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(dayStartLocal, tz);
            var dayEndUtc = TimeZoneInfo.ConvertTimeToUtc(dayEndLocal, tz);

            var windowStartUtc = dayStartUtc.AddDays(-3);
            var windowEndUtc = dayEndUtc;

            var events = allEvents
                .Where(e =>
                {
                    var end = e.EndUtc ?? windowEndUtc;
                    return end > windowStartUtc && e.StartUtc < windowEndUtc;
                })
                .OrderBy(e => e.StartUtc)
                .ToList();

            return AnalyzeInternal(dayStartUtc, dayEndUtc, windowEndUtc, events);
        }

        private static HosViolationResult AnalyzeInternal(
            DateTimeOffset dayStartUtc,
            DateTimeOffset dayEndUtc,
            DateTimeOffset nowUtc,
            List<DutyEvent> events)
        {
            var res = new HosViolationResult();
            if (events.Count == 0) return res;

            DateTimeOffset? shiftStartUtc = null;
            TimeSpan driveInShift = TimeSpan.Zero;
            TimeSpan paused14h = TimeSpan.Zero;

            TimeSpan driveSinceBreak = TimeSpan.Zero;
            TimeSpan nonDrivingRun = TimeSpan.Zero;

            TimeSpan consecutiveRest = TimeSpan.Zero;
            bool readyForNewShift = false;

            bool hasSleeper8 = false;
            bool hasRest2 = false;

            TimeSpan currentSleeperRun = TimeSpan.Zero;
            TimeSpan currentAnyRestRun = TimeSpan.Zero;

            DateTimeOffset? EffectiveWindowEnd(DateTimeOffset? ss, TimeSpan paused)
                => ss == null ? null : ss.Value.AddHours(14) + paused;

            foreach (var ev in events)
            {
                var status = ev.Status;
                var segStart = ev.StartUtc;
                var segEnd = ev.EndUtc ?? nowUtc;
                if (segEnd <= segStart) continue;

                var segLen = segEnd - segStart;

                // Runs
                if (IsSleeper(status)) currentSleeperRun += segLen; else currentSleeperRun = TimeSpan.Zero;
                if (IsRest(status)) currentAnyRestRun += segLen; else currentAnyRestRun = TimeSpan.Zero;

                // 10h reset
                if (IsRest(status))
                {
                    consecutiveRest += segLen;
                    if (consecutiveRest >= TimeSpan.FromHours(10))
                        readyForNewShift = true;
                }
                else
                {
                    consecutiveRest = TimeSpan.Zero;
                }

                // Split detection (practical)
                if (!hasSleeper8 && currentSleeperRun >= TimeSpan.FromHours(8))
                {
                    hasSleeper8 = true;
                    if (hasRest2) readyForNewShift = true;
                }

                if (!hasRest2 && currentAnyRestRun >= TimeSpan.FromHours(2))
                {
                    hasRest2 = true;
                    if (hasSleeper8) readyForNewShift = true;
                }

                bool isDuty = IsDriving(status) || IsOnDutyNotDriving(status);

                // Start new shift on return to duty after reset/split
                if (isDuty && readyForNewShift)
                {
                    shiftStartUtc = segStart;

                    driveInShift = TimeSpan.Zero;
                    paused14h = TimeSpan.Zero;

                    driveSinceBreak = TimeSpan.Zero;
                    nonDrivingRun = TimeSpan.Zero;

                    hasSleeper8 = false;
                    hasRest2 = false;
                    currentSleeperRun = TimeSpan.Zero;
                    currentAnyRestRun = TimeSpan.Zero;

                    readyForNewShift = false;
                }

                if (shiftStartUtc == null && isDuty)
                    shiftStartUtc = segStart;

                // Pause 14h while in sleeper after SB>=8 achieved
                if (shiftStartUtc != null && IsSleeper(status) && hasSleeper8)
                    paused14h += segLen;

                // Break rule
                if (IsNonDriving(status))
                {
                    nonDrivingRun += segLen;
                    if (nonDrivingRun >= TimeSpan.FromMinutes(30))
                        driveSinceBreak = TimeSpan.Zero;
                }
                else
                {
                    nonDrivingRun = TimeSpan.Zero;
                }

                // Driving accum + find exact trip instants
                if (IsDriving(status))
                {
                    var beforeShift = driveInShift;
                    var beforeBreak = driveSinceBreak;

                    driveInShift += segLen;
                    driveSinceBreak += segLen;

                    if (res.FirstDrive11AtUtc == null && beforeShift < TimeSpan.FromHours(11) && driveInShift > TimeSpan.FromHours(11))
                        res.FirstDrive11AtUtc = segStart + (TimeSpan.FromHours(11) - beforeShift);

                    if (res.FirstBreak8AtUtc == null && beforeBreak < TimeSpan.FromHours(8) && driveSinceBreak > TimeSpan.FromHours(8))
                        res.FirstBreak8AtUtc = segStart + (TimeSpan.FromHours(8) - beforeBreak);
                }

                // Evaluate violations only if driving overlaps target day
                string? marker = null;

                if (IsDriving(status))
                {
                    var overlapStart = Max(segStart, dayStartUtc);
                    var overlapEnd = Min(segEnd, dayEndUtc);

                    if (overlapEnd > overlapStart)
                    {
                        if (driveInShift > TimeSpan.FromHours(11))
                        {
                            res.Drive11Violation = true;
                            marker ??= "TRIP: 11H";
                        }

                        if (driveSinceBreak > TimeSpan.FromHours(8))
                        {
                            res.Break30Violation = true;
                            marker ??= "TRIP: 8H BREAK";
                        }

                        if (shiftStartUtc != null)
                        {
                            var rawEnd = shiftStartUtc.Value.AddHours(14);
                            var effEnd = rawEnd + paused14h;

                            if (res.FirstShift14AtUtc == null && overlapEnd > effEnd)
                                res.FirstShift14AtUtc = overlapStart >= effEnd ? overlapStart : effEnd;

                            if (overlapEnd > effEnd)
                            {
                                res.Shift14Violation = true;
                                marker ??= "TRIP: 14H";
                            }

                            res.ShiftStartUtc = shiftStartUtc;
                            res.WindowEndUtc = rawEnd;
                            res.Paused14h = paused14h;
                            res.EffectiveWindowEndUtc = effEnd;
                        }
                    }
                }

                // Always keep debug counters updated
                res.ShiftStartUtc ??= shiftStartUtc;
                if (shiftStartUtc != null)
                {
                    res.WindowEndUtc = shiftStartUtc.Value.AddHours(14);
                    res.Paused14h = paused14h;
                    res.EffectiveWindowEndUtc = EffectiveWindowEnd(shiftStartUtc, paused14h);
                }

                res.DriveInShift = driveInShift;
                res.DriveSinceBreak = driveSinceBreak;

                // Append trace row (clamped to analysis window; full segment still fine)
                res.Trace.Add(new HosSegmentTrace
                {
                    StartUtc = segStart,
                    EndUtc = segEnd,
                    Status = status,
                    DriveInShift = driveInShift,
                    DriveSinceBreak = driveSinceBreak,
                    Paused14h = paused14h,
                    Marker = marker
                });
            }

            return res;
        }
    }
}
