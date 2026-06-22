using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace OverWatchELD.Converters
{
    public class ProgressArcConverter : IValueConverter
    {
        // value = progress (0..1)
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double p = 0;
            if (value is double d) p = d;
            if (p < 0) p = 0;
            if (p > 1) p = 1;

            // geometry for an arc in a 100x100 box (center 50,50)
            double radius = 36;
            Point center = new Point(50, 50);

            // start at top (-90deg)
            double startAngle = -90;
            double endAngle = startAngle + (360 * p);

            Point start = PointOnCircle(center, radius, startAngle);
            Point end = PointOnCircle(center, radius, endAngle);

            bool largeArc = (endAngle - startAngle) > 180;

            var fig = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
            fig.Segments.Add(new ArcSegment
            {
                Point = end,
                Size = new Size(radius, radius),
                IsLargeArc = largeArc,
                SweepDirection = SweepDirection.Clockwise
            });

            var geo = new PathGeometry();
            geo.Figures.Add(fig);
            return geo;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;

        private static Point PointOnCircle(Point c, double r, double degrees)
        {
            double rad = degrees * Math.PI / 180.0;
            return new Point(c.X + (r * Math.Cos(rad)), c.Y + (r * Math.Sin(rad)));
        }
    }
}
