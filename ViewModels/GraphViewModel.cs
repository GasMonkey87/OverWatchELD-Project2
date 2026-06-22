using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using DutyStatus = OverWatchELD.Models.DutyStatus;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Threading;
using OverWatchELD.Models;
using OverWatchELD.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OverWatchELD.ViewModels
{
    public sealed class DutyChangeRow
    {
        public long Id { get; set; }
        public string TimeText { get; set; } = "";
        public DutyStatus Status { get; set; }
        public string StatusText { get; set; } = "";
        public bool IsDrivingLocked { get; set; }
        public string DurationText { get; set; } = "";
        public string Notes { get; set; } = "";
        public string LocationText { get; set; } = "";
        public bool IsEdited { get; set; }                 // ✅ shows Edited badge
        public string EditedHint { get; set; } = "";       // optional tooltip text
    }

    public partial class GraphViewModel : ObservableObject
    {
        [ObservableProperty] private string graphDateText = "";
        [ObservableProperty] private string currentStatusText = "";
        [ObservableProperty] private string statusSinceText = "";

        [ObservableProperty] private DateTimeOffset dayStartUtc;
        [ObservableProperty] private DateTimeOffset dayEndUtc;

        [ObservableProperty] private DateTimeOffset viewingLocalDayStart;

        [ObservableProperty] private double graphWidth = 900;

        [ObservableProperty] private double viewportWidth = 900;

        [ObservableProperty] private double graphPixelsPerHour = 60;

        public IReadOnlyList<DutyStatusOption> DutyStatusOptions { get; } = new[]
        {
            new DutyStatusOption(DutyStatus.OffDuty, "OFF DUTY"),
            new DutyStatusOption(DutyStatus.Sleeper, "SLEEPER"),
            new DutyStatusOption(DutyStatus.Driving, "DRIVING"),
            new DutyStatusOption(DutyStatus.OnDuty, "ON DUTY"),
            new DutyStatusOption(DutyStatus.PersonalConveyance, "PERSONAL CONVEYANCE"),
            new DutyStatusOption(DutyStatus.YardMove, "YARD MOVE"),
        };

        [ObservableProperty] private DutyEvent? selectedEvent;

        [ObservableProperty] private bool canGoNextDay;

        // ✅ Inspection mode (read-only, last 8 days)
        [ObservableProperty] private bool isInspectionMode;

        public ObservableCollection<DutyEvent> Events { get; } = new ObservableCollection<DutyEvent>();
        public ObservableCollection<DutyChangeRow> DutyChanges { get; } = new ObservableCollection<DutyChangeRow>();

        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };

        // ✅ Display-only normalized ranges (prevents overlap on graph)
        private readonly Dictionary<long, (DateTimeOffset StartUtc, DateTimeOffset EndUtc)> _displayRanges
            = new Dictionary<long, (DateTimeOffset StartUtc, DateTimeOffset EndUtc)>();

        public GraphViewModel()
        {
            DatabaseService.Initialize();

            Refresh();
            UpdateGraphWidth();

            _timer.Tick += (_, __) =>
            {
                RefreshHeaderOnly();
                ComputeLayout();          // uses normalized display ranges
                RecomputeCanGoNext();
            };
            _timer.Start();
        }

        public void SetViewportWidth(double width)
        {
            if (width <= 0) return;
            ViewportWidth = width;
            UpdateGraphWidth();
        }

        partial void OnGraphPixelsPerHourChanged(double value)
        {
            UpdateGraphWidth();
        }

        private void UpdateGraphWidth()
        {
            // 24-hour view. GraphWidth is the scrollable content width.
            var content = 24.0 * GraphPixelsPerHour;
            GraphWidth = Math.Max(ViewportWidth, content);
            ComputeLayout();
        }

        [RelayCommand] private void PrevDay() => ShiftDay(-1);

        [RelayCommand]
        private void NextDay()
        {
            if (!CanGoNextDay) return;
            ShiftDay(+1);
        }

        private DateTimeOffset TodayLocalStart()
        {
            var nowUtc = EldClock.UtcNow;
            var localNow = nowUtc.ToOffset(EldClock.LocalOffset);
            return new DateTimeOffset(localNow.Year, localNow.Month, localNow.Day, 0, 0, 0, localNow.Offset);
        }

        private DateTimeOffset EarliestAllowedLocalDay()
        {
            var today = TodayLocalStart();
            return IsInspectionMode ? today.AddDays(-7) : today; // last 8 days when inspection
        }

        private void ShiftDay(int deltaDays)
        {
            if (ViewingLocalDayStart == default)
                BuildDayWindow();

            var target = ViewingLocalDayStart.AddDays(deltaDays);

            // Clamp: no future days
            var today = TodayLocalStart();
            if (target > today) target = today;

            // Clamp: inspection mode last 8 days
            var earliest = EarliestAllowedLocalDay();
            if (target < earliest) target = earliest;

            ViewingLocalDayStart = target;

            DayStartUtc = ViewingLocalDayStart.ToUniversalTime();
            DayEndUtc = ViewingLocalDayStart.AddDays(1).ToUniversalTime();

            ReloadEvents();
            RefreshHeaderOnly();
            ComputeLayout();
            BuildDutyChanges();
            RecomputeCanGoNext();
        }

        public void Refresh()
        {
            BuildDayWindow();
            RefreshHeaderOnly();
            ReloadEvents();
            ComputeLayout();
            BuildDutyChanges();
            RecomputeCanGoNext();
        }

        public void SelectEvent(DutyEvent? ev)
        {
            foreach (var x in Events) x.IsSelected = false;

            if (ev != null)
                ev.IsSelected = true;

            SelectedEvent = ev;
        }

        public async Task UpdateDutyStatusAsync(DutyChangeRow row, DutyStatus newStatus)
        {
            if (IsInspectionMode) return;
            if (row == null) return;

            // DOT rule: cannot edit driving segments
            if (row.IsDrivingLocked || newStatus == DutyStatus.Driving)
                return;

            var ev = Events.FirstOrDefault(x => x.Id == row.Id);
            if (ev == null) return;

            ev.Status = newStatus;
            ev.IsEdited = true;
            ev.EditedAtUtc = EldClock.UtcNow;
            ev.EditReason = "Status changed";

            DatabaseService.UpdateDutyEvent(ev);

            // Refresh computed visuals + duty list
            ReloadEvents();
            ComputeLayout();
            BuildDutyChanges();
            RefreshHeaderOnly();
        }

        private void BuildDayWindow()
        {
            var today = TodayLocalStart();
            ViewingLocalDayStart = today;

            DayStartUtc = ViewingLocalDayStart.ToUniversalTime();
            DayEndUtc = ViewingLocalDayStart.AddDays(1).ToUniversalTime();
        }

        private void RefreshHeaderOnly()
        {
            var headerLocal = ViewingLocalDayStart.ToOffset(EldClock.LocalOffset);
            GraphDateText = headerLocal.ToString("ddd, MMM d", CultureInfo.InvariantCulture);

            var status = ELDStateService.CurrentStatus;
            CurrentStatusText = status.ToString();

            var sinceUtc = ELDStateService.CurrentStatusStartUtc;
            var sinceLocal = sinceUtc.ToOffset(EldClock.LocalOffset);
            StatusSinceText = $"Since {sinceLocal:HH:mm}";
        }

        private void ReloadEvents()
        {
            Events.Clear();

            var rows = DatabaseService
                .GetDutyEvents(DayStartUtc, DayEndUtc)
                .OrderBy(x => x.StartUtc)
                .ToList();

            foreach (var ev in rows)
                Events.Add(ev);

            BuildNormalizedDisplayRanges();   // ✅ critical: prevents overlap on the graph
        }

        /// <summary>
        /// Build a display-only timeline:
        /// - clamps segments to [DayStartUtc, DayEndUtc]
        /// - trims overlaps (each segment starts at/after previous segment end)
        /// - guarantees a minimum visible width (so you always "see" the status)
        /// This does NOT change DB times; only affects graph rendering + duty list durations.
        /// </summary>
        private void BuildNormalizedDisplayRanges()
        {
            _displayRanges.Clear();

            var prevEnd = DayStartUtc;

            foreach (var ev in Events.OrderBy(e => e.StartUtc))
            {
                var start = ev.StartUtc;
                var end = ev.EffectiveEndUtc;

                // Clamp to day bounds
                if (start < DayStartUtc) start = DayStartUtc;
                if (end > DayEndUtc) end = DayEndUtc;

                // Fix inverted / zero ranges
                if (end < start) end = start;

                // Trim overlap: push start forward
                if (start < prevEnd) start = prevEnd;

                // Ensure at least a tiny visible segment (30 seconds) if possible
                if (end <= start)
                {
                    var minEnd = start.AddSeconds(30);
                    end = minEnd <= DayEndUtc ? minEnd : start;
                }

                // Final clamp
                if (end > DayEndUtc) end = DayEndUtc;

                _displayRanges[ev.Id] = (start, end);
                prevEnd = end;
            }
        }

        private void BuildDutyChanges()
        {
            DutyChanges.Clear();

            foreach (var ev in Events.OrderBy(e => e.StartUtc))
            {
                // Use normalized display window so duration matches what you SEE on graph
                var (dispStartUtc, dispEndUtc) = _displayRanges.TryGetValue(ev.Id, out var r)
                    ? r
                    : (ev.StartUtc, ev.EffectiveEndUtc);

                var startLocal = dispStartUtc.ToOffset(EldClock.LocalOffset);
                var endLocal = dispEndUtc.ToOffset(EldClock.LocalOffset);

                var dur = endLocal - startLocal;
                if (dur < TimeSpan.Zero) dur = TimeSpan.Zero;

                var editedHint = ev.IsEdited
                    ? $"Edited {ev.EditedAtUtc?.ToLocalTime():MM/dd HH:mm} {ev.EditReason}".Trim()
                    : "";

                DutyChanges.Add(new DutyChangeRow
                {
                    Id = ev.Id,
                    TimeText = startLocal.ToString("HH:mm", CultureInfo.InvariantCulture),
                    Status = ev.Status,
                    StatusText = StatusLabel(ev.Status),
                    IsDrivingLocked = ev.Status == DutyStatus.Driving,
                    DurationText = $"{(int)dur.TotalHours:00}:{dur.Minutes:00}",
                    LocationText = string.IsNullOrWhiteSpace(ev.LocationText) ? "" : ev.LocationText,
                    Notes = string.IsNullOrWhiteSpace(ev.Notes) ? "" : ev.Notes,
                    IsEdited = ev.IsEdited,
                    EditedHint = editedHint
                });
            }
        }

        private void RecomputeCanGoNext()
        {
            var today = TodayLocalStart();
            CanGoNextDay = ViewingLocalDayStart < today;
        }

        private static string StatusLabel(DutyStatus s) =>
            s switch
            {
                DutyStatus.OffDuty => "OFF DUTY",
                DutyStatus.Sleeper => "SLEEPER",
                DutyStatus.Driving => "DRIVING",
                DutyStatus.OnDuty => "ON DUTY",
                DutyStatus.PersonalConveyance => "PERSONAL CONVEYANCE",
                DutyStatus.YardMove => "YARD MOVE",
                _ => s.ToString()
            };

        // Graph rows: OFF, SB, D, ON (PC/YM treated as ON for graph row)
        private static double RowTop(DutyStatus s) =>
            s switch
            {
                DutyStatus.OffDuty => 0,
                DutyStatus.Sleeper => 24,
                DutyStatus.Driving => 48,
                _ => 72
            };

        private void ComputeLayout()
        {
            if (GraphWidth <= 10) return;

            var totalSeconds = (DayEndUtc - DayStartUtc).TotalSeconds;
            if (totalSeconds <= 0) return;

            double X(DateTimeOffset tUtc)
            {
                var clamped = tUtc < DayStartUtc ? DayStartUtc : (tUtc > DayEndUtc ? DayEndUtc : tUtc);
                var sec = (clamped - DayStartUtc).TotalSeconds;
                return (sec / totalSeconds) * GraphWidth;
            }

            foreach (var ev in Events)
            {
                // ✅ Use normalized display times (no overlap)
                var (start, end) = _displayRanges.TryGetValue(ev.Id, out var r)
                    ? r
                    : (ev.StartUtc, ev.EffectiveEndUtc);

                var left = X(start);
                var right = X(end);

                ev.GraphLeft = left;
                ev.GraphWidth = Math.Max(2, right - left); // always visible

                ev.GraphTop = RowTop(ev.Status);
                ev.GraphHeight = 20;

                ev.DotLeft = ev.GraphLeft;
                ev.DotTop = ev.GraphTop + 8;
            }
        }
    }
}
