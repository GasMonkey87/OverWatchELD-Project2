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
                

                var hos = HosCalculator2.ComputeSnapshot();

                var breakV = hos.BreakRemaining < TimeSpan.Zero;
                var driveV = hos.DriveRemaining < TimeSpan.Zero;
                var shiftV = hos.ShiftRemaining < TimeSpan.Zero;
                var cycleV = hos.CycleRemaining < TimeSpan.Zero;

                var dutyLabel = "Unknown";

                try
                {
                    var events = DatabaseService.GetDutyEvents(nowUtc.AddDays(-14), nowUtc.AddMinutes(1));
                    var last = events?.OrderBy(e => e.StartUtc).LastOrDefault();

                    if (last != null)
                        dutyLabel = ToDutyLabel(last.Status);
                }
                catch { }

                var breakRemNN = ClampNonNegative(hos.BreakRemaining);
                var driveRemNN = ClampNonNegative(hos.DriveRemaining);
                var shiftRemNN = ClampNonNegative(hos.ShiftRemaining);
                var cycleRemNN = ClampNonNegative(hos.CycleRemaining);

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

                    BreakRemaining = breakRemNN,
                    DriveRemaining = driveRemNN,
                    ShiftRemaining = shiftRemNN,
                    CycleRemaining = cycleRemNN,

                    BreakViolation = breakV,
                    DriveViolation = driveV,
                    ShiftViolation = shiftV,
                    CycleViolation = cycleV,

                    BreakUsedPct = PctUsed(breakRemNN, hos.BreakLimit),
                    DriveUsedPct = PctUsed(driveRemNN, hos.DriveLimit),
                    ShiftUsedPct = PctUsed(shiftRemNN, hos.ShiftLimit),
                    CycleUsedPct = PctUsed(cycleRemNN, hos.CycleLimit),

                    DutyStatusLabel = dutyLabel,

                    ShouldPulse =
                        breakV ||
                        driveV ||
                        shiftV ||
                        cycleV ||
                        driveRemNN <= TimeSpan.FromMinutes(15) ||
                        shiftRemNN <= TimeSpan.FromMinutes(15) ||
                        breakRemNN <= TimeSpan.FromMinutes(15) ||
                        cycleRemNN <= TimeSpan.FromMinutes(30),

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