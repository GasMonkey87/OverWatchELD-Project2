using System;
using System.Linq;
using System.Reflection;
using OverWatchELD.Models;

namespace OverWatchELD.Services
{
    public static class DashboardSnapshotProvider
    {
        public sealed class DashboardSnapshot
        {
            public DateTimeOffset GeneratedUtc { get; init; }
            public DateTimeOffset GeneratedLocal { get; init; }

            public TimeSpan ShiftRemaining { get; init; }
            public TimeSpan DriveRemaining { get; init; }
            public TimeSpan BreakRemaining { get; init; }
            public TimeSpan CycleRemaining { get; init; }

            public bool ShiftViolation { get; init; }
            public bool DriveViolation { get; init; }
            public bool BreakViolation { get; init; }
            public bool CycleViolation { get; init; }

            public double ShiftUsedPct { get; init; }
            public double DriveUsedPct { get; init; }
            public double BreakUsedPct { get; init; }
            public double CycleUsedPct { get; init; }

            public string DutyStatusLabel { get; init; } = "Unknown";
            public bool ShouldPulse { get; init; }
            public string Notes { get; init; } = "";

            public string DriveTime => FormatClock(DriveRemaining);
            public string ShiftTime => FormatClock(ShiftRemaining);
            public string BreakTime => FormatClock(BreakRemaining);
            public string CycleTime => FormatClock(CycleRemaining);

            public int UnsignedLogsCount { get; init; }
            public string GreetingHeader { get; init; } = "";

            private static string FormatClock(TimeSpan ts)
            {
                if (ts < TimeSpan.Zero)
                    ts = TimeSpan.Zero;

                var totalHours = (int)ts.TotalHours;
                return $"{totalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
            }
        }

        private sealed class DirectClockResult
        {
            public TimeSpan ShiftRemaining { get; set; } = TimeSpan.FromHours(14);
            public TimeSpan DriveRemaining { get; set; } = TimeSpan.FromHours(11);
            public TimeSpan BreakRemaining { get; set; } = TimeSpan.FromHours(8);
            public TimeSpan CycleRemaining { get; set; } = TimeSpan.FromHours(70);
            public DutyStatus CurrentStatus { get; set; } = DutyStatus.OffDuty;
        }

        private static TimeSpan ClampNonNegative(TimeSpan t)
            => t < TimeSpan.Zero ? TimeSpan.Zero : t;

        private static double PctUsed(TimeSpan remaining, TimeSpan limit)
        {
            var lim = limit.TotalSeconds;
            if (lim <= 0)
                return 0;

            var used = 1.0 - (remaining.TotalSeconds / lim);

            if (double.IsNaN(used) || double.IsInfinity(used))
                return 0;

            return Math.Clamp(used, 0, 1);
        }

        private static string ToDutyLabel(DutyStatus s) => s switch
        {
            DutyStatus.OffDuty => "Off Duty",
            DutyStatus.Sleeper => "Sleeper",
            DutyStatus.Driving => "Driving",
            DutyStatus.OnDuty => "On Duty",
            DutyStatus.PersonalConveyance => "Personal Conveyance",
            DutyStatus.YardMove => "Yard Move",
            _ => "Unknown"
        };

        private static bool IsDriving(DutyStatus status)
            => status == DutyStatus.Driving;

        private static bool IsOnDutyForHos(DutyStatus status)
            => status == DutyStatus.Driving ||
               status == DutyStatus.OnDuty ||
               status == DutyStatus.YardMove;

        private static DirectClockResult BuildDirectClockResult(DateTimeOffset nowUtc)
        {
            var cycleStart = nowUtc.AddDays(-8);
            var shiftStart = nowUtc.AddHours(-14);
            var events = DatabaseService.GetDutyEvents(cycleStart, nowUtc.AddSeconds(1));
            var ordered = events.OrderBy(e => e.StartUtc).ToList();
            var open = ordered.LastOrDefault(e => e.EndUtc == null) ?? ordered.LastOrDefault();

            var result = new DirectClockResult();
            if (open != null)
                result.CurrentStatus = open.Status;

            TimeSpan driveUsed = TimeSpan.Zero;
            TimeSpan shiftUsed = TimeSpan.Zero;
            TimeSpan cycleUsed = TimeSpan.Zero;
            TimeSpan currentDrivingElapsed = TimeSpan.Zero;

            foreach (var e in ordered)
            {
                var start = e.StartUtc < cycleStart ? cycleStart : e.StartUtc;
                var end = e.EndUtc ?? nowUtc;

                if (end > nowUtc)
                    end = nowUtc;

                if (end <= start)
                    continue;

                var span = end - start;

                if (IsDriving(e.Status))
                    driveUsed += span;

                if (IsOnDutyForHos(e.Status))
                    cycleUsed += span;

                var shiftSpanStart = start < shiftStart ? shiftStart : start;
                if (end > shiftSpanStart && IsOnDutyForHos(e.Status))
                    shiftUsed += end - shiftSpanStart;
            }

            if (open != null && open.EndUtc == null && IsDriving(open.Status))
            {
                currentDrivingElapsed = nowUtc - open.StartUtc;
                if (currentDrivingElapsed < TimeSpan.Zero)
                    currentDrivingElapsed = TimeSpan.Zero;
            }

            result.DriveRemaining = ClampNonNegative(TimeSpan.FromHours(11) - driveUsed);
            result.ShiftRemaining = ClampNonNegative(TimeSpan.FromHours(14) - shiftUsed);
            result.CycleRemaining = ClampNonNegative(TimeSpan.FromHours(70) - cycleUsed);
            result.BreakRemaining = IsDriving(result.CurrentStatus)
                ? ClampNonNegative(TimeSpan.FromHours(8) - currentDrivingElapsed)
                : TimeSpan.FromHours(8);

            return result;
        }

        private static string TryGetDriverFirstName(object? driverProfileObj)
        {
            try
            {
                if (driverProfileObj == null)
                    return "";

                var p = driverProfileObj.GetType().GetProperty(
                    "FirstName",
                    BindingFlags.Public | BindingFlags.Instance);

                var v = p?.GetValue(driverProfileObj, null)?.ToString();

                return string.IsNullOrWhiteSpace(v) ? "" : v!;
            }
            catch
            {
                return "";
            }
        }

        public static DashboardSnapshot BuildSnapshot()
        {
            var nowUtc = EldClock.UtcNow;
            var nowLocal = EldClock.LocalNow;

            try
            {
                var clock = BuildDirectClockResult(nowUtc);

                var breakV = clock.BreakRemaining <= TimeSpan.Zero;
                var driveV = clock.DriveRemaining <= TimeSpan.Zero;
                var shiftV = clock.ShiftRemaining <= TimeSpan.Zero;
                var cycleV = clock.CycleRemaining <= TimeSpan.Zero;

                int unsignedCount = 0;

                try
                {
                    var startLocal = DateTime.Today.AddDays(-13);
                    var endLocal = DateTime.Today;
                    unsignedCount = DatabaseService.GetUnsignedLocalLogDates(startLocal, endLocal).Count;
                }
                catch { }

                string greeting;

                try
                {
                    var hr = DateTime.Now.Hour;
                    greeting = hr < 12 ? "Good morning" : hr < 18 ? "Good afternoon" : "Good evening";
                }
                catch
                {
                    greeting = "Welcome";
                }

                string driverName = "";

                try { driverName = TryGetDriverFirstName(App.DriverProfile); }
                catch { }

                var greetingHeader = string.IsNullOrWhiteSpace(driverName)
                    ? greeting
                    : $"{greeting}, {driverName}";

                return new DashboardSnapshot
                {
                    GeneratedUtc = nowUtc,
                    GeneratedLocal = nowLocal,

                    BreakRemaining = clock.BreakRemaining,
                    DriveRemaining = clock.DriveRemaining,
                    ShiftRemaining = clock.ShiftRemaining,
                    CycleRemaining = clock.CycleRemaining,

                    BreakViolation = breakV,
                    DriveViolation = driveV,
                    ShiftViolation = shiftV,
                    CycleViolation = cycleV,

                    BreakUsedPct = PctUsed(clock.BreakRemaining, TimeSpan.FromHours(8)),
                    DriveUsedPct = PctUsed(clock.DriveRemaining, TimeSpan.FromHours(11)),
                    ShiftUsedPct = PctUsed(clock.ShiftRemaining, TimeSpan.FromHours(14)),
                    CycleUsedPct = PctUsed(clock.CycleRemaining, TimeSpan.FromHours(70)),

                    DutyStatusLabel = ToDutyLabel(clock.CurrentStatus),

                    ShouldPulse =
                        breakV ||
                        driveV ||
                        shiftV ||
                        cycleV ||
                        clock.DriveRemaining <= TimeSpan.FromMinutes(15) ||
                        clock.ShiftRemaining <= TimeSpan.FromMinutes(15) ||
                        clock.BreakRemaining <= TimeSpan.FromMinutes(15) ||
                        clock.CycleRemaining <= TimeSpan.FromMinutes(30),

                    Notes = "",
                    UnsignedLogsCount = unsignedCount,
                    GreetingHeader = greetingHeader
                };
            }
            catch
            {
                return BuildFallbackSnapshot(nowUtc, nowLocal);
            }
        }

        private static DashboardSnapshot BuildFallbackSnapshot(DateTimeOffset nowUtc, DateTimeOffset nowLocal)
        {
            return new DashboardSnapshot
            {
                GeneratedUtc = nowUtc,
                GeneratedLocal = nowLocal,

                BreakRemaining = TimeSpan.FromHours(8),
                DriveRemaining = TimeSpan.FromHours(11),
                ShiftRemaining = TimeSpan.FromHours(14),
                CycleRemaining = TimeSpan.FromHours(70),

                BreakUsedPct = 0,
                DriveUsedPct = 0,
                ShiftUsedPct = 0,
                CycleUsedPct = 0,

                DutyStatusLabel = "Unknown",
                ShouldPulse = false,
                Notes = "",
                UnsignedLogsCount = 0,
                GreetingHeader = ""
            };
        }
    }
}
