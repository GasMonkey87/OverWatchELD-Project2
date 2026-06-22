using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace OverWatchELD.Controls
{
    public partial class CircularRing : UserControl
    {
        private bool _isUpdating;
        private bool _updateQueued;

        public CircularRing()
        {
            InitializeComponent();
            Loaded += (_, __) => QueueUpdate();
        }

        public static readonly DependencyProperty RingLabelProperty =
            DependencyProperty.Register(nameof(RingLabel), typeof(string), typeof(CircularRing),
                new PropertyMetadata("", OnAnyChanged));

        public string RingLabel
        {
            get => (string)GetValue(RingLabelProperty);
            set => SetValue(RingLabelProperty, value);
        }

        public static readonly DependencyProperty ValueTextProperty =
            DependencyProperty.Register(nameof(ValueText), typeof(string), typeof(CircularRing),
                new PropertyMetadata("", OnAnyChanged));

        public string ValueText
        {
            get => (string)GetValue(ValueTextProperty);
            set => SetValue(ValueTextProperty, value);
        }

        public static readonly DependencyProperty StrokeThicknessProperty =
            DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(CircularRing),
                new PropertyMetadata(10d, OnAnyChanged));

        public double StrokeThickness
        {
            get => (double)GetValue(StrokeThicknessProperty);
            set => SetValue(StrokeThicknessProperty, value);
        }

        public static readonly DependencyProperty IsMutedProperty =
            DependencyProperty.Register(nameof(IsMuted), typeof(bool), typeof(CircularRing),
                new PropertyMetadata(false, OnAnyChanged));

        public bool IsMuted
        {
            get => (bool)GetValue(IsMutedProperty);
            set => SetValue(IsMutedProperty, value);
        }

        // If provided, overrides auto progress calculation.
        public static readonly DependencyProperty ProgressValueProperty =
            DependencyProperty.Register(nameof(ProgressValue), typeof(double), typeof(CircularRing),
                new PropertyMetadata(-1d, OnAnyChanged)); // -1 means auto

        public double ProgressValue
        {
            get => (double)GetValue(ProgressValueProperty);
            set => SetValue(ProgressValueProperty, value);
        }

        public static readonly DependencyProperty CenterTextProperty =
            DependencyProperty.Register(nameof(CenterText), typeof(string), typeof(CircularRing),
                new PropertyMetadata("", OnAnyChanged));

        public string CenterText
        {
            get => (string)GetValue(CenterTextProperty);
            set => SetValue(CenterTextProperty, value);
        }

        public static readonly DependencyProperty MaxMinutesProperty =
            DependencyProperty.Register(nameof(MaxMinutes), typeof(double), typeof(CircularRing),
                new PropertyMetadata(0d, OnAnyChanged));

        public double MaxMinutes
        {
            get => (double)GetValue(MaxMinutesProperty);
            set => SetValue(MaxMinutesProperty, value);
        }

        public static readonly DependencyProperty WarnMinutesProperty =
            DependencyProperty.Register(nameof(WarnMinutes), typeof(double), typeof(CircularRing),
                new PropertyMetadata(0d, OnAnyChanged));

        public double WarnMinutes
        {
            get => (double)GetValue(WarnMinutesProperty);
            set => SetValue(WarnMinutesProperty, value);
        }

        public static readonly DependencyProperty PulseMinutesProperty =
            DependencyProperty.Register(nameof(PulseMinutes), typeof(double), typeof(CircularRing),
                new PropertyMetadata(0d, OnAnyChanged));

        public double PulseMinutes
        {
            get => (double)GetValue(PulseMinutesProperty);
            set => SetValue(PulseMinutesProperty, value);
        }

        public static readonly DependencyProperty SeverityProperty =
            DependencyProperty.Register(nameof(Severity), typeof(int), typeof(CircularRing),
                new PropertyMetadata(0));

        public int Severity
        {
            get => (int)GetValue(SeverityProperty);
            set => SetValue(SeverityProperty, value);
        }

        public static readonly DependencyProperty IsPulsingProperty =
            DependencyProperty.Register(nameof(IsPulsing), typeof(bool), typeof(CircularRing),
                new PropertyMetadata(false));

        public bool IsPulsing
        {
            get => (bool)GetValue(IsPulsingProperty);
            set => SetValue(IsPulsingProperty, value);
        }

        private static void OnAnyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((CircularRing)d).QueueUpdate();
        }

        private void QueueUpdate()
        {
            if (!IsLoaded) return;

            // Prevent deep recursive chains. Update once per dispatcher turn.
            if (_updateQueued) return;
            _updateQueued = true;

            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                _updateQueued = false;
                UpdateVisual();
            }));
        }

        private void UpdateVisual()
        {
            if (!IsLoaded) return;
            if (_isUpdating) return;

            _isUpdating = true;
            try
            {
                // NOTE: TextBlock in XAML is already bound to ValueText,
                // so do NOT set PART_CenterText.Text here (that can cause binding churn).
                // If CenterText is desired, set ValueText from the VM instead.

                double remainingMinutes = TryParseRemainingMinutes(ValueText);

                int sev = 0;
                bool pulse = false;

                if (!double.IsNaN(remainingMinutes))
                {
                    if (remainingMinutes <= 0)
                    {
                        sev = 2;
                        pulse = true;
                    }
                    else if (WarnMinutes > 0 && remainingMinutes <= WarnMinutes)
                    {
                        sev = 1;
                        if (PulseMinutes > 0 && remainingMinutes <= PulseMinutes)
                            pulse = true;
                    }
                }

                Severity = sev;
                IsPulsing = pulse;

                if (IsMuted)
                {
                    IsPulsing = false;
                    Severity = 0;
                    PART_Arc.Opacity = 0.55;
                    PART_Track.Opacity = 0.35;
                    PART_CenterText.Opacity = 0.75;
                }
                else
                {
                    PART_Arc.Opacity = 1.0;
                    PART_Track.Opacity = 1.0;
                    PART_CenterText.Opacity = 1.0;
                }

                double p = ProgressValue;
                if (p < 0)
                    p = TryComputeProgressFromValueText(ValueText, MaxMinutes);

                if (double.IsNaN(p) || double.IsInfinity(p)) p = 0;
                p = Math.Max(0, Math.Min(1, p));

                PART_Arc.Data = BuildArcGeometry(p);
            }
            finally
            {
                _isUpdating = false;
            }
        }

        private static double TryParseRemainingMinutes(string? valueText)
        {
            if (string.IsNullOrWhiteSpace(valueText)) return double.NaN;

            if (TimeSpan.TryParse(valueText, CultureInfo.InvariantCulture, out var ts))
                return ts.TotalMinutes;

            int colon = valueText.IndexOf(':');
            if (colon > 0)
            {
                var a = valueText.Substring(0, colon).Trim();
                var b = valueText.Substring(colon + 1).Trim();
                if (int.TryParse(a, NumberStyles.Integer, CultureInfo.InvariantCulture, out var h) &&
                    int.TryParse(b, NumberStyles.Integer, CultureInfo.InvariantCulture, out var m))
                    return (h * 60.0) + m;
            }

            if (double.TryParse(valueText, NumberStyles.Any, CultureInfo.InvariantCulture, out var mins))
                return mins;

            return double.NaN;
        }

        private static double TryComputeProgressFromValueText(string? valueText, double maxMinutes)
        {
            if (maxMinutes <= 0) return 0;
            var rem = TryParseRemainingMinutes(valueText);
            if (double.IsNaN(rem)) return 0;
            var used = Math.Max(0, maxMinutes - rem);
            return used / maxMinutes;
        }

        private Geometry BuildArcGeometry(double progress01)
        {
            const double size = 110;

            double radius = (size / 2) - (StrokeThickness / 2);
            var center = new Point(size / 2, size / 2);

            if (progress01 <= 0)
                return Geometry.Empty;

            if (progress01 >= 1)
                progress01 = 0.9999;

            double startAngle = -90;
            double endAngle = startAngle + (360 * progress01);

            Point start = PointOnCircle(center, radius, startAngle);
            Point end = PointOnCircle(center, radius, endAngle);

            bool isLargeArc = (endAngle - startAngle) > 180;

            var fig = new PathFigure
            {
                StartPoint = start,
                IsClosed = false,
                IsFilled = false
            };

            fig.Segments.Add(new System.Windows.Media.ArcSegment
            {
                Point = end,
                Size = new Size(radius, radius),
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = isLargeArc
            });

            var geo = new PathGeometry();
            geo.Figures.Add(fig);
            return geo;
        }

        private static Point PointOnCircle(Point center, double radius, double angleDegrees)
        {
            double a = angleDegrees * Math.PI / 180.0;
            return new Point(
                center.X + radius * Math.Cos(a),
                center.Y + radius * Math.Sin(a)
            );
        }
    }
}
