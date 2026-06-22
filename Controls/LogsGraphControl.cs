using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OverWatchELD.Controls
{
    public sealed class LogsGraphControl : FrameworkElement
    {
        public static readonly DependencyProperty EventsProperty =
            DependencyProperty.Register(nameof(Events), typeof(IEnumerable<GraphEvent>), typeof(LogsGraphControl),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty StartUtcProperty =
            DependencyProperty.Register(nameof(StartUtc), typeof(DateTimeOffset), typeof(LogsGraphControl),
                new FrameworkPropertyMetadata(default(DateTimeOffset), FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty EndUtcProperty =
            DependencyProperty.Register(nameof(EndUtc), typeof(DateTimeOffset), typeof(LogsGraphControl),
                new FrameworkPropertyMetadata(default(DateTimeOffset), FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty PixelsPerHourProperty =
            DependencyProperty.Register(nameof(PixelsPerHour), typeof(double), typeof(LogsGraphControl),
                new FrameworkPropertyMetadata(60d, FrameworkPropertyMetadataOptions.AffectsRender));

        public IEnumerable<GraphEvent>? Events
        {
            get => (IEnumerable<GraphEvent>?)GetValue(EventsProperty);
            set => SetValue(EventsProperty, value);
        }

        public DateTimeOffset StartUtc
        {
            get => (DateTimeOffset)GetValue(StartUtcProperty);
            set => SetValue(StartUtcProperty, value);
        }

        public DateTimeOffset EndUtc
        {
            get => (DateTimeOffset)GetValue(EndUtcProperty);
            set => SetValue(EndUtcProperty, value);
        }

        public double PixelsPerHour
        {
            get => (double)GetValue(PixelsPerHourProperty);
            set => SetValue(PixelsPerHourProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            // NEVER let WPF silently skip render due to exceptions
            try
            {
                var w = ActualWidth;
                var h = ActualHeight;

                if (double.IsNaN(w) || double.IsInfinity(w) || w < 10 ||
                    double.IsNaN(h) || double.IsInfinity(h) || h < 10)
                    return;

                var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

                // Background (dark)
                var bg = new SolidColorBrush(Color.FromRgb(13, 23, 38));
                dc.DrawRoundedRectangle(bg, null, new Rect(0, 0, w, h), 10, 10);

                // Row layout
                var rowH = h / 4.0;

                // Horizontal row separators (VERY visible)
                var rowPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 1);
                for (int r = 1; r <= 3; r++)
                {
                    var y = r * rowH;
                    dc.DrawLine(rowPen, new Point(0, y), new Point(w, y));
                }

                // Labels (VERY visible)
                DrawLabel(dc, "OFF", 8, 6 + (0 * rowH), dpi);
                DrawLabel(dc, "SB", 8, 6 + (1 * rowH), dpi);
                DrawLabel(dc, "DRIVE", 8, 6 + (2 * rowH), dpi);
                DrawLabel(dc, "ON", 8, 6 + (3 * rowH), dpi);

                // Grid vertical lines each hour, thicker each 4 hours
                var pxPerHr = PixelsPerHour;
                if (double.IsNaN(pxPerHr) || double.IsInfinity(pxPerHr) || pxPerHr <= 0)
                    pxPerHr = 60;

                var thinPen = new Pen(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), 1);
                var thickPen = new Pen(new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)), 1.5);

                for (int hr = 0; hr <= 24; hr++)
                {
                    var x = hr * pxPerHr;
                    if (x > w) break;

                    dc.DrawLine((hr % 4 == 0) ? thickPen : thinPen, new Point(x, 0), new Point(x, h));
                }

                // If no usable window, still show “renderer active”
                if (EndUtc <= StartUtc)
                {
                    DrawCenterText(dc, "Graph renderer active (time window invalid)", w, h, dpi);
                    return;
                }

                // Draw events as bars
                var evs = Events?.ToList() ?? new List<GraphEvent>();
                foreach (var ev in evs)
                {
                    var s = ev.StartUtc;
                    var e = ev.EndUtc;
                    if (e <= s) continue;

                    // Clamp to selected day window
                    if (e < StartUtc || s > EndUtc) continue;
                    if (s < StartUtc) s = StartUtc;
                    if (e > EndUtc) e = EndUtc;

                    var startHr = (s - StartUtc).TotalHours;
                    var endHr = (e - StartUtc).TotalHours;

                    var x1 = startHr * pxPerHr;
                    var x2 = endHr * pxPerHr;

                    if (x2 < 0 || x1 > w) continue;

                    var width = Math.Max(2, x2 - x1);
                    var row = StatusToRow(ev.StatusText);
                    var y = row * rowH;

                    var rect = new Rect(x1, y + 2, width, rowH - 4);
                    dc.DrawRoundedRectangle(StatusToBrush(ev.StatusText), null, rect, 4, 4);
                }

                // If empty, show hint (still proves render is working)
                if (evs.Count == 0)
                    DrawCenterText(dc, "No log data to display", w, h, dpi);
            }
            catch (Exception ex)
            {
                // Render-safe: draw a visible error message on the graph itself
                try
                {
                    var w = ActualWidth;
                    var h = ActualHeight;
                    var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

                    dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(80, 0, 0)), null, new Rect(0, 0, w, h));
                    DrawCenterText(dc, "Graph render failed: " + ex.Message, w, h, dpi);
                }
                catch { /* give up */ }
            }
        }

        private static void DrawLabel(DrawingContext dc, string text, double x, double y, double dpi)
        {
            var ft = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                12,
                new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)),
                dpi);

            dc.DrawText(ft, new Point(x, y));
        }

        private static void DrawCenterText(DrawingContext dc, string text, double w, double h, double dpi)
        {
            var ft = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                14,
                new SolidColorBrush(Color.FromArgb(220, 220, 220, 220)),
                dpi);

            dc.DrawText(ft, new Point((w - ft.Width) / 2, (h - ft.Height) / 2));
        }

        private static int StatusToRow(string? status)
        {
            var s = (status ?? "").ToUpperInvariant();
            if (s.Contains("OFF")) return 0;
            if (s.Contains("SB") || s.Contains("SLEEP")) return 1;
            if (s.Contains("DRIV")) return 2;
            return 3;
        }

        private static Brush StatusToBrush(string? status)
        {
            var s = (status ?? "").ToUpperInvariant();

            if (s.Contains("OFF")) return new SolidColorBrush(Color.FromRgb(75, 95, 120));
            if (s.Contains("SB") || s.Contains("SLEEP")) return new SolidColorBrush(Color.FromRgb(90, 120, 160));
            if (s.Contains("DRIV")) return new SolidColorBrush(Color.FromRgb(70, 160, 120));
            return new SolidColorBrush(Color.FromRgb(170, 125, 65));
        }

        public sealed class GraphEvent
        {
            public DateTimeOffset StartUtc { get; set; }
            public DateTimeOffset EndUtc { get; set; }
            public string StatusText { get; set; } = "Unknown";
        }
    }
}
