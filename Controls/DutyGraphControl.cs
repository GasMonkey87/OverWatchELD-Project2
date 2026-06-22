using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;

namespace OverWatchELD.Controls
{
    public sealed class DutyGraphControl : FrameworkElement
    {
        public static readonly DependencyProperty DutyEventsProperty =
            DependencyProperty.Register(nameof(DutyEvents), typeof(IEnumerable), typeof(DutyGraphControl),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public IEnumerable? DutyEvents
        {
            get => (IEnumerable?)GetValue(DutyEventsProperty);
            set => SetValue(DutyEventsProperty, value);
        }

        public static readonly DependencyProperty SelectedDateProperty =
            DependencyProperty.Register(nameof(SelectedDate), typeof(DateTime), typeof(DutyGraphControl),
                new FrameworkPropertyMetadata(DateTime.Today, FrameworkPropertyMetadataOptions.AffectsRender));

        public DateTime SelectedDate
        {
            get => (DateTime)GetValue(SelectedDateProperty);
            set => SetValue(SelectedDateProperty, value);
        }

        private sealed record Seg(DateTime Start, DateTime End, string Status);

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            var w = ActualWidth;
            var h = ActualHeight;
            if (w <= 10 || h <= 10) return;

            // background card
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x12, 0x1A, 0x28)), null, new Rect(0, 0, w, h), 12, 12);

            var padL = 44.0;
            var padR = 12.0;
            var padT = 10.0;
            var padB = 18.0;

            var plot = new Rect(padL, padT, Math.Max(1, w - padL - padR), Math.Max(1, h - padT - padB));

            var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(0x2A, 0x3B, 0x55)), 1);
            var textBrush = new SolidColorBrush(Color.FromRgb(0xAF, 0xC0, 0xD7));
            var typeface = new Typeface("Segoe UI");

            // hour grid
            for (int hr = 0; hr <= 24; hr++)
            {
                var x = plot.Left + plot.Width * (hr / 24.0);
                dc.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));

                if (hr % 4 == 0 && hr < 24)
                {
                    var ft = new FormattedText(hr.ToString(CultureInfo.InvariantCulture),
                        CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 11, textBrush, 1.25);
                    dc.DrawText(ft, new Point(x - ft.Width / 2, plot.Bottom + 2));
                }
            }

            // duty levels (top->bottom): ON, D, SB, OFF
            double LevelY(int level) => plot.Top + (plot.Height * (level / 4.0));

            dc.DrawLine(gridPen, new Point(plot.Left, LevelY(1)), new Point(plot.Right, LevelY(1)));
            dc.DrawLine(gridPen, new Point(plot.Left, LevelY(2)), new Point(plot.Right, LevelY(2)));
            dc.DrawLine(gridPen, new Point(plot.Left, LevelY(3)), new Point(plot.Right, LevelY(3)));

            DrawLeftLabel(dc, "ON", plot.Left - 34, LevelY(0) - 6, typeface, textBrush);
            DrawLeftLabel(dc, "D", plot.Left - 34, LevelY(1) - 6, typeface, textBrush);
            DrawLeftLabel(dc, "SB", plot.Left - 34, LevelY(2) - 6, typeface, textBrush);
            DrawLeftLabel(dc, "OFF", plot.Left - 34, LevelY(3) - 6, typeface, textBrush);

            var dayStart = SelectedDate.Date;
            var dayEnd = dayStart.AddDays(1);

            if (DutyEvents == null) return;

            var raw = DutyEvents.Cast<object>().ToList();
            if (raw.Count == 0) return;

            // Build + normalize segments
            var segs = new List<Seg>();

            foreach (var ev in raw)
            {
                var st = ReadDate(ev, "StartLocal", "Start", "StartTime", "StartUtc", "StartTimestamp");
                var en = ReadDate(ev, "EndLocal", "End", "EndTime", "EndUtc", "EndTimestamp");

                if (st == null || en == null) continue;

                var s = st.Value;
                var e = en.Value;

                // If bad ordering, swap
                if (e < s)
                {
                    (s, e) = (e, s);
                }

                // Clamp to selected day
                if (e <= dayStart || s >= dayEnd) continue;
                if (s < dayStart) s = dayStart;
                if (e > dayEnd) e = dayEnd;

                if (e <= s) continue;

                var status = ReadStatus(ev);
                segs.Add(new Seg(s, e, status));
            }

            if (segs.Count == 0) return;

            // Sort
            segs = segs.OrderBy(x => x.Start).ThenBy(x => x.End).ToList();

            // Trim overlaps + merge adjacent identical statuses
            var normalized = new List<Seg>();
            foreach (var seg in segs)
            {
                var s = seg.Start;
                var e = seg.End;

                if (normalized.Count > 0)
                {
                    var prev = normalized[^1];

                    // If overlaps, push start forward to prevent overlap
                    if (s < prev.End) s = prev.End;

                    // If adjacent same status, merge
                    if (s <= prev.End && string.Equals(prev.Status, seg.Status, StringComparison.OrdinalIgnoreCase))
                    {
                        var mergedEnd = e > prev.End ? e : prev.End;
                        normalized[^1] = prev with { End = mergedEnd };
                        continue;
                    }

                    // If start equals prev.End and same status, merge
                    if (s == prev.End && string.Equals(prev.Status, seg.Status, StringComparison.OrdinalIgnoreCase))
                    {
                        normalized[^1] = prev with { End = e };
                        continue;
                    }
                }

                if (e > s)
                    normalized.Add(seg with { Start = s, End = e });
            }

            double X(DateTime t) => plot.Left + plot.Width * ((t - dayStart).TotalMinutes / (24 * 60.0));

            // Draw
            foreach (var seg in normalized)
            {
                var level = StatusToLevel(seg.Status);
                var yTop = LevelY(level);
                var yBottom = LevelY(level + 1);

                var x1 = X(seg.Start);
                var x2 = X(seg.End);

                if (x2 <= x1) continue;

                var fill = StatusToBrush(seg.Status);
                var rect = new Rect(new Point(x1, yTop + 2), new Point(x2, yBottom - 2));
                dc.DrawRoundedRectangle(fill, null, rect, 4, 4);
            }
        }

        private static void DrawLeftLabel(DrawingContext dc, string text, double x, double y, Typeface tf, Brush brush)
        {
            var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 11, brush, 1.25);
            dc.DrawText(ft, new Point(x, y));
        }

        private static string ReadStatus(object ev)
        {
            // Try many common variants
            var s =
                ReadString(ev, "Status")
                ?? ReadString(ev, "DutyStatusText")
                ?? ReadString(ev, "StatusText")
                ?? ReadString(ev, "DutyStatus")
                ?? ReadString(ev, "Duty")
                ?? ReadString(ev, "Type")
                ?? "";

            s = s.Trim();

            // If it's an enum-like value, normalize some common ones
            var lower = s.ToLowerInvariant();
            if (lower.Contains("off")) return "OFF";
            if (lower.Contains("sleeper") || lower == "sb") return "SB";
            if (lower.Contains("drive") || lower == "d") return "D";
            if (lower.Contains("on")) return "ON";

            // Unknown -> ON (safe default for graph)
            return string.IsNullOrWhiteSpace(s) ? "ON" : s;
        }

        private static int StatusToLevel(string status)
        {
            var s = status.ToLowerInvariant();
            if (s.Contains("off")) return 3;
            if (s.Contains("sleeper") || s == "sb") return 2;
            if (s.Contains("drive") || s == "d") return 1;
            return 0; // ON/default
        }

        private static Brush StatusToBrush(string status)
        {
            var s = status.ToLowerInvariant();
            if (s.Contains("off")) return new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));                 // blue
            if (s.Contains("sleeper") || s == "sb") return new SolidColorBrush(Color.FromRgb(0xA8, 0x55, 0xF7)); // purple
            if (s.Contains("drive") || s == "d") return new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));    // green
            return new SolidColorBrush(Color.FromRgb(0xF5, 0xC5, 0x42));                                          // amber (ON)
        }

        private static DateTime? ReadDate(object obj, params string[] props)
        {
            foreach (var p in props)
            {
                var v = ReadValue(obj, p);
                if (v is DateTime dt) return dt;
            }
            return null;
        }

        private static string? ReadString(object obj, string prop)
        {
            var v = ReadValue(obj, prop);
            return v?.ToString();
        }

        private static object? ReadValue(object obj, string prop)
        {
            try
            {
                var t = obj.GetType();
                var pi = t.GetProperty(prop, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pi != null) return pi.GetValue(obj);

                // one-level nesting helpers
                var nested = t.GetProperty("Event", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj)
                             ?? t.GetProperty("Data", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj);

                if (nested != null)
                {
                    var nt = nested.GetType();
                    var npi = nt.GetProperty(prop, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (npi != null) return npi.GetValue(nested);
                }
            }
            catch { }
            return null;
        }
    }
}
