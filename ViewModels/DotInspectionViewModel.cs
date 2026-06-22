using System;
using DutyStatus = OverWatchELD.Models.DutyStatus;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using OverWatchELD.Models;
using OverWatchELD.Services;

namespace OverWatchELD.ViewModels
{
    public sealed class DotInspectionViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private DateTime _endDay = DateTime.Today;
        private int _cycleLimitHours = 70;

        public DateTime EndDay
        {
            get => _endDay;
            set
            {
                if (_endDay.Date == value.Date) return;
                _endDay = value.Date;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EndDayTitle));
                Reload();
            }
        }

        public int CycleLimitHours
        {
            get => _cycleLimitHours;
            set
            {
                if (_cycleLimitHours == value) return;
                _cycleLimitHours = value;
                OnPropertyChanged();
                Reload();
            }
        }

        public string EndDayTitle => $"Ending: {EndDay:dddd, MMM d, yyyy}";

        public ObservableCollection<DotDaySummary> Days { get; } = new();

        public string CycleSummary
        {
            get
            {
                var used = Days.Sum(d => d.OnDutyTotalMinutes);
                var limit = CycleLimitHours * 60L;
                var remaining = Math.Max(0, limit - used);

                var anyViol = Days.Any(d => d.HasViolation);
                var violText = anyViol ? " • Violations present" : "";

                return $"Cycle {CycleLimitHours}h — Used {Fmt(used)}  •  Remaining {Fmt(remaining)}{violText}";
            }
        }

        public DotInspectionViewModel()
        {
            Reload();
        }

        private void Reload()
        {
            Days.Clear();

            // 8 days shown, pull a little extra so split/reset detection works
            var rangeStartLocal = EndDay.Date.AddDays(-12);
            var rangeEndLocal = EndDay.Date.AddDays(1);

            var rangeStartUtc = new DateTimeOffset(rangeStartLocal).ToUniversalTime();
            var rangeEndUtc = new DateTimeOffset(rangeEndLocal).ToUniversalTime();

            var allEvents = DatabaseService.GetDutyEvents(rangeStartUtc, rangeEndUtc)
                                           .OrderBy(e => e.StartUtc)
                                           .ToList();

            for (int i = 7; i >= 0; i--)
            {
                var day = EndDay.Date.AddDays(-i);
                Days.Add(BuildDay(day, allEvents));
            }

            OnPropertyChanged(nameof(CycleSummary));
        }

        private static DotDaySummary BuildDay(DateTime dayLocal, System.Collections.Generic.IEnumerable<DutyEvent> allEvents)
        {
            var startUtc = new DateTimeOffset(dayLocal.Date).ToUniversalTime();
            var endUtc = new DateTimeOffset(dayLocal.Date.AddDays(1)).ToUniversalTime();

            var events = allEvents.Where(e =>
            {
                var eEnd = e.EndUtc ?? endUtc;
                return eEnd > startUtc && e.StartUtc < endUtc;
            }).OrderBy(e => e.StartUtc).ToList();

            long off = 0, sb = 0, drive = 0, on = 0, pc = 0, ym = 0;

            foreach (var ev in events)
            {
                var s = ev.StartUtc < startUtc ? startUtc : ev.StartUtc;
                var e = (ev.EndUtc ?? endUtc) > endUtc ? endUtc : (ev.EndUtc ?? endUtc);
                if (e <= s) continue;

                var mins = (long)Math.Round((e - s).TotalMinutes);

                switch (ev.Status)
                {
                    case DutyStatus.OffDuty: off += mins; break;
                    case DutyStatus.Sleeper: sb += mins; break;
                    case DutyStatus.Driving: drive += mins; break;
                    case DutyStatus.OnDuty: on += mins; break;
                    case DutyStatus.PersonalConveyance: pc += mins; break;
                    case DutyStatus.YardMove: ym += mins; break;
                    default: on += mins; break;
                }
            }

            // Analyzer (includes debug + trace)
            var viol = HosViolationAnalyzer.AnalyzeDayLocal(dayLocal, allEvents);

            string shiftStartLocal = viol.ShiftStartUtc?.LocalDateTime.ToString("MM/dd HH:mm") ?? "-";
            string windowEndLocal = viol.WindowEndUtc?.LocalDateTime.ToString("MM/dd HH:mm") ?? "-";
            string paused = Fmt(viol.Paused14h);
            string effectiveWindowEndLocal = viol.EffectiveWindowEndUtc?.LocalDateTime.ToString("MM/dd HH:mm") ?? "-";

            string driveInShiftText = Fmt(viol.DriveInShift);
            string driveSinceBreakText = Fmt(viol.DriveSinceBreak);

            string triggerText = viol.BuildTriggerText(TimeZoneInfo.Local);
            string traceText = viol.BuildTraceText(TimeZoneInfo.Local);

            return new DotDaySummary(
                dayLocal.Date,
                off, sb, drive, on, pc, ym,
                viol.Drive11Violation,
                viol.Shift14Violation,
                viol.Break30Violation,
                viol.ToText(),
                shiftStartLocal,
                windowEndLocal,
                paused,
                effectiveWindowEndLocal,
                driveInShiftText,
                driveSinceBreakText,
                triggerText,
                traceText
            );
        }

        private static string Fmt(long minutes)
        {
            if (minutes < 0) minutes = 0;
            var h = minutes / 60;
            var m = minutes % 60;
            return $"{h:00}:{m:00}";
        }

        private static string Fmt(TimeSpan ts)
        {
            if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;
            var totalMin = (long)Math.Floor(ts.TotalMinutes);
            var h = totalMin / 60;
            var m = totalMin % 60;
            return $"{h:00}:{m:00}";
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class DotDaySummary
    {
        public DateTime Day { get; }

        public long OffMinutes { get; }
        public long SbMinutes { get; }
        public long DriveMinutes { get; }
        public long OnMinutes { get; }
        public long PcMinutes { get; }
        public long YmMinutes { get; }

        public bool DriveViolation { get; }
        public bool ShiftViolation { get; }
        public bool BreakViolation { get; }

        public bool HasViolation => DriveViolation || ShiftViolation || BreakViolation;

        public string ViolationText { get; }

        public long OnDutyTotalMinutes => OnMinutes + DriveMinutes + YmMinutes;

        public string DayLabel => Day.ToString("ddd  MM/dd");

        public string OffText => Fmt(OffMinutes);
        public string SbText => Fmt(SbMinutes);
        public string DriveText => Fmt(DriveMinutes);
        public string OnText => Fmt(OnMinutes);
        public string PcText => Fmt(PcMinutes);
        public string YmText => Fmt(YmMinutes);

        public string OnDutyTotalText => Fmt(OnDutyTotalMinutes);
        public string TotalText => Fmt(OffMinutes + SbMinutes + DriveMinutes + OnMinutes + PcMinutes + YmMinutes);

        // Debug columns
        public string ShiftStartLocalText { get; }
        public string WindowEndLocalText { get; }
        public string PausedText { get; }
        public string EffectiveWindowEndLocalText { get; }

        // Popup details
        public string DriveInShiftText { get; }
        public string DriveSinceBreakText { get; }
        public string TriggerText { get; }
        public string TraceText { get; }

        public DotDaySummary(
            DateTime day,
            long off, long sb, long drive, long on, long pc, long ym,
            bool driveV, bool shiftV, bool breakV,
            string violationText,
            string shiftStartLocal, string windowEndLocal, string pausedText, string effectiveWindowEndLocal,
            string driveInShiftText, string driveSinceBreakText,
            string triggerText, string traceText)
        {
            Day = day;

            OffMinutes = off;
            SbMinutes = sb;
            DriveMinutes = drive;
            OnMinutes = on;
            PcMinutes = pc;
            YmMinutes = ym;

            DriveViolation = driveV;
            ShiftViolation = shiftV;
            BreakViolation = breakV;

            ViolationText = violationText;

            ShiftStartLocalText = shiftStartLocal;
            WindowEndLocalText = windowEndLocal;
            PausedText = pausedText;
            EffectiveWindowEndLocalText = effectiveWindowEndLocal;

            DriveInShiftText = driveInShiftText;
            DriveSinceBreakText = driveSinceBreakText;

            TriggerText = triggerText;
            TraceText = traceText;
        }

        private static string Fmt(long minutes)
        {
            if (minutes < 0) minutes = 0;
            var h = minutes / 60;
            var m = minutes % 60;
            return $"{h:00}:{m:00}";
        }
    }
}
