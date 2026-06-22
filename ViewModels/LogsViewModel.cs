using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using OverWatchELD.Models;
using OverWatchELD.Services;

namespace OverWatchELD.ViewModels
{
    public sealed class LogsViewModel : INotifyPropertyChanged
    {
        private const double SegmentHeight = 24;
        private const double TopPadding = 8;
        private const double RowGap = 44;
        private const double GridHeight = 176;

        public LogsViewModel()
        {
            try { DatabaseService.Initialize(); } catch { }

            _selectedDate = EldClock.LocalNow.Date;

            RebuildHourTicksAndLabels();
            Refresh();
        }

        private double _graphWidth = 820;
        public double GraphWidth
        {
            get => _graphWidth;
            set
            {
                if (Math.Abs(_graphWidth - value) < 0.1) return;
                _graphWidth = Math.Max(200, value);
                OnPropertyChanged();
                RebuildHourTicksAndLabels();
                RebuildGraphSegments(_lastEventsForDay);
            }
        }

        private DateTime _selectedDate;
        public DateTime SelectedDate
        {
            get => _selectedDate;
            set
            {
                var d = value.Date;
                if (_selectedDate.Date == d) return;
                _selectedDate = d;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HeaderDateText));
                OnPropertyChanged(nameof(IsSelectedDayCertified));
                Refresh();
            }
        }

        public string HeaderDateText =>
            SelectedDate.ToString("dddd, MMM d, yyyy", CultureInfo.CurrentCulture);

        private string _debugSourceText = "";
        public string DebugSourceText
        {
            get => _debugSourceText;
            private set { _debugSourceText = value; OnPropertyChanged(); }
        }

        public ObservableCollection<HourTickVm> HourTicks { get; } = new();
        public ObservableCollection<HourLabelVm> HourLabels { get; } = new();
        public ObservableCollection<GraphSegmentVm> GraphSegments { get; } = new();
        public ObservableCollection<DutyEventRowVm> DutyEvents { get; } = new();

        private List<DutyEvent> _lastEventsForDay = new();

        // Keep the property name for compatibility with existing XAML,
        // but it now contains ALL days with activity, each with signed/certified status.
        public ObservableCollection<UnsignedDayVm> UnsignedDays { get; } = new();

        public string UnsignedLogsButtonText
        {
            get
            {
                var unsigned = UnsignedDays.Count(x => !x.IsCertified);
                return unsigned > 0 ? $"Unsigned logs ({unsigned})" : "Unsigned logs";
            }
        }

        public bool IsSelectedDayCertified =>
            DatabaseService.GetLogCertification(SelectedDate)?.Certified == true;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public void PrevDay() => SelectedDate = SelectedDate.AddDays(-1);
        public void NextDay() => SelectedDate = SelectedDate.AddDays(1);

        public void CertifySelectedDay(string driverName, string signature)
        {
            try
            {
                DatabaseService.Initialize();

                var date = SelectedDate.Date;

                var cert = new DailyLogCertification
                {
                    LogDateLocal = date.ToString("yyyy-MM-dd"),
                    SignedAtUtc = DateTimeOffset.UtcNow,
                    DriverName = string.IsNullOrWhiteSpace(driverName) ? "Driver" : driverName.Trim(),
                    Signature = string.IsNullOrWhiteSpace(signature) ? driverName?.Trim() ?? "Driver" : signature.Trim(),
                    Certified = true,
                    CertificationText = "I hereby certify that my data entries and my record of duty status for this 24-hour period are true and correct."
                };

                DatabaseService.UpsertLogCertification(cert);

                LoadUnsignedDaysBadge();
                OnPropertyChanged(nameof(IsSelectedDayCertified));
                Refresh();
            }
            catch (Exception ex)
            {
                DebugSourceText = "Certification failed: " + ex.GetBaseException().Message;
            }
        }

        public void Refresh()
        {
            try
            {
                DatabaseService.Initialize();

                var gameTodayLocal = EldClock.LocalNow.Date;

                if (SelectedDate.Date > gameTodayLocal)
                {
                    _lastEventsForDay = new List<DutyEvent>();
                    DutyEvents.Clear();
                    GraphSegments.Clear();
                    RebuildHourTicksAndLabels();
                    DebugSourceText = $"FMCSA: Future day selected ({SelectedDate:yyyy-MM-dd}) – no logs shown.";
                    OnPropertyChanged(nameof(IsSelectedDayCertified));
                    return;
                }

                LoadUnsignedDaysBadge();

                var dayStartLocal = SelectedDate.Date;
                var dayEndLocal = dayStartLocal.AddDays(1);

                var startUtc = new DateTimeOffset(dayStartLocal, TimeZoneInfo.Local.GetUtcOffset(dayStartLocal)).ToUniversalTime();
                var endUtc = new DateTimeOffset(dayEndLocal, TimeZoneInfo.Local.GetUtcOffset(dayEndLocal)).ToUniversalTime();

                var events = DatabaseService.GetDutyEvents(startUtc, endUtc)
                    .OrderBy(e => e.StartUtc)
                    .ToList();

                if (SelectedDate.Date == gameTodayLocal)
                {
                    var nowUtc = EldClock.UtcNow;

                    events = events
                        .Where(e => e.StartUtc <= nowUtc.AddSeconds(5))
                        .ToList();

                    foreach (var e in events)
                    {
                        if (e.EndUtc == null || e.EndUtc > nowUtc)
                            e.EndUtc = nowUtc;
                    }
                }

                _lastEventsForDay = events;

                DutyEvents.Clear();
                foreach (var e in events)
                    DutyEvents.Add(new DutyEventRowVm(e));

                DebugSourceText = $"DB events: {events.Count} (local day {SelectedDate:yyyy-MM-dd})";

                RebuildHourTicksAndLabels();
                RebuildGraphSegments(events);

                OnPropertyChanged(nameof(UnsignedLogsButtonText));
                OnPropertyChanged(nameof(IsSelectedDayCertified));
            }
            catch (Exception ex)
            {
                DebugSourceText = "Logs load failed: " + ex.GetBaseException().Message;
                _lastEventsForDay = new List<DutyEvent>();
                DutyEvents.Clear();
                GraphSegments.Clear();
                OnPropertyChanged(nameof(IsSelectedDayCertified));
            }
        }

        private void LoadUnsignedDaysBadge()
        {
            try
            {
                UnsignedDays.Clear();

                var today = EldClock.LocalNow.Date;
                var start = today.AddDays(-14);
                var end = today;

                // Use ANY activity so inspections-only days are included too.
                var daysWithActivity = DatabaseService.GetLocalDatesWithAnyActivity(start, end);

                foreach (var d in daysWithActivity.OrderByDescending(x => x))
                {
                    var cert = DatabaseService.GetLogCertification(d);

                    UnsignedDays.Add(new UnsignedDayVm(
                        d,
                        cert?.Certified == true,
                        cert?.DriverName,
                        cert?.SignedAtUtc));
                }
            }
            catch
            {
            }

            OnPropertyChanged(nameof(UnsignedLogsButtonText));
        }

        private void RebuildHourTicksAndLabels()
        {
            HourTicks.Clear();
            HourLabels.Clear();

            for (int h = 0; h <= 24; h++)
            {
                var x = (GraphWidth / 24.0) * h;
                HourTicks.Add(new HourTickVm { Left = x, GridHeight = GridHeight });

                if (h < 24)
                {
                    HourLabels.Add(new HourLabelVm
                    {
                        Left = x,
                        Text = h.ToString(CultureInfo.InvariantCulture)
                    });
                }
            }
        }

        private void RebuildGraphSegments(List<DutyEvent> events)
        {
            GraphSegments.Clear();

            var dayStartLocal = SelectedDate.Date;
            var fullDayEndLocal = dayStartLocal.AddDays(1);

            var gameNowLocal = EldClock.LocalNow;
            var gameTodayLocal = gameNowLocal.Date;

            var visibleEndLocal = (SelectedDate.Date == gameTodayLocal) ? gameNowLocal : fullDayEndLocal;

            if (visibleEndLocal <= dayStartLocal)
                visibleEndLocal = fullDayEndLocal;

            var ordered = events
                .OrderBy(e => e.StartUtc)
                .Select(e =>
                {
                    var s = e.StartUtc.ToLocalTime().DateTime;
                    var en = (e.EndUtc?.ToLocalTime().DateTime) ?? visibleEndLocal;

                    if (s < dayStartLocal) s = dayStartLocal;
                    if (en > visibleEndLocal) en = visibleEndLocal;

                    if (SelectedDate.Date == gameTodayLocal)
                    {
                        if (s > gameNowLocal) return null;
                        if (en > gameNowLocal) en = gameNowLocal;
                    }

                    if (en <= s) return null;

                    return new
                    {
                        ModelId = e.Id,
                        Status = e.Status,
                        Start = s,
                        End = en
                    };
                })
                .Where(x => x != null)
                .ToList()!;

            if (ordered.Count == 0)
            {
                AddSegment(0, DutyStatus.OffDuty, dayStartLocal, visibleEndLocal, dayStartLocal, visibleEndLocal);
                return;
            }

            var timeline = new List<(long id, DutyStatus status, DateTime start, DateTime end, bool isFiller)>();
            DateTime cursor = dayStartLocal;

            foreach (var ev in ordered)
            {
                var s = ev.Start;
                var en = ev.End;

                if (s < cursor) s = cursor;

                if (s > cursor)
                    timeline.Add((0, DutyStatus.OffDuty, cursor, s, true));

                if (en > s)
                {
                    timeline.Add((ev.ModelId, ev.Status, s, en, false));
                    cursor = en;
                }

                if (cursor >= visibleEndLocal) break;
            }

            if (cursor < visibleEndLocal)
                timeline.Add((0, DutyStatus.OffDuty, cursor, visibleEndLocal, true));

            foreach (var t in timeline)
                AddSegment(t.id, t.status, t.start, t.end, dayStartLocal, visibleEndLocal);
        }

        private void AddSegment(long id, DutyStatus status, DateTime start, DateTime end, DateTime dayStartLocal, DateTime visibleEndLocal)
        {
            if (start < dayStartLocal) start = dayStartLocal;
            if (end > visibleEndLocal) end = visibleEndLocal;
            if (end <= start) return;

            var startSeconds = (start - dayStartLocal).TotalSeconds;
            var endSeconds = (end - dayStartLocal).TotalSeconds;

            var left = startSeconds / 86400d * GraphWidth;
            var width = Math.Max(1, (endSeconds - startSeconds) / 86400d * GraphWidth);

            GraphSegments.Add(new GraphSegmentVm
            {
                EventId = id,
                Left = left,
                Width = width,
                Top = StatusTop(status),
                Height = SegmentHeight,
                Fill = StatusBrush(status),
                Label = StatusLabel(status)
            });
        }

        private static string StatusLabel(DutyStatus s) => s switch
        {
            DutyStatus.OnDuty => "ON",
            DutyStatus.Driving => "D",
            DutyStatus.Sleeper => "SB",
            DutyStatus.OffDuty => "OFF",
            DutyStatus.PersonalConveyance => "PC",
            DutyStatus.YardMove => "YM",
            _ => "OFF"
        };

        private static double StatusTop(DutyStatus s) => s switch
        {
            DutyStatus.OnDuty => TopPadding + 0 * RowGap,
            DutyStatus.Driving => TopPadding + 1 * RowGap,
            DutyStatus.Sleeper => TopPadding + 2 * RowGap,
            DutyStatus.OffDuty => TopPadding + 3 * RowGap,
            DutyStatus.PersonalConveyance => TopPadding + 1 * RowGap,
            DutyStatus.YardMove => TopPadding + 1 * RowGap,
            _ => TopPadding + 3 * RowGap
        };

        private static Brush StatusBrush(DutyStatus s) => s switch
        {
            DutyStatus.Driving => new SolidColorBrush(Color.FromRgb(16, 185, 129)),
            DutyStatus.OnDuty => new SolidColorBrush(Color.FromRgb(59, 130, 246)),
            DutyStatus.Sleeper => new SolidColorBrush(Color.FromRgb(168, 85, 247)),
            DutyStatus.OffDuty => new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            _ => new SolidColorBrush(Color.FromRgb(148, 163, 184))
        };
    }

    public sealed class UnsignedDayVm : INotifyPropertyChanged
    {
        public DateTime DateLocal { get; }

        public string DateText => DateLocal.ToString("ddd, MMM d, yyyy", CultureInfo.CurrentCulture);

        public bool IsCertified { get; }

        public string? DriverName { get; }

        public DateTimeOffset? SignedAtUtc { get; }

        public string StatusText
        {
            get
            {
                if (!IsCertified)
                    return "Unsigned";

                if (SignedAtUtc.HasValue)
                    return $"Certified • {SignedAtUtc.Value.LocalDateTime:g}";

                return "Certified";
            }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }

        public UnsignedDayVm(DateTime d, bool isCertified, string? driverName, DateTimeOffset? signedAtUtc)
        {
            DateLocal = d.Date;
            IsCertified = isCertified;
            DriverName = driverName;
            SignedAtUtc = signedAtUtc;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public sealed class HourTickVm
    {
        public double Left { get; set; }
        public double GridHeight { get; set; }
    }

    public sealed class HourLabelVm
    {
        public double Left { get; set; }
        public string Text { get; set; } = "";
    }

    public sealed class GraphSegmentVm
    {
        public long EventId { get; set; }
        public double Left { get; set; }
        public double Width { get; set; }
        public double Top { get; set; }
        public double Height { get; set; }

        public Brush Fill { get; set; } = Brushes.White;
        public Brush Brush => Fill;

        public string Label { get; set; } = "";
        public double LabelOpacity => string.IsNullOrWhiteSpace(Label) ? 0.0 : 0.95;
    }

    public sealed class DutyEventRowVm
    {
        public DutyEvent Model { get; }

        public string StartLocal => Model.StartUtc.ToLocalTime().ToString("HH:mm");
        public string EndLocal => (Model.EndUtc?.ToLocalTime() ?? EldClock.UtcNow).ToString("HH:mm");
        public string Status => Model.Status.ToString();
        public string Note => Model.Notes ?? "";

        public DutyEventRowVm(DutyEvent model) => Model = model;
    }
}