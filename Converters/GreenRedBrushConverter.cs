using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace OverWatchELD.Converters
{
    /// <summary>
    /// Converts either:
    ///  - bool (true = Red, false = Green)
    ///  - double percent remaining (0..1) (<= 0.10 = Red, <= 0.25 = Gold, else Green)
    ///  - TimeSpan remaining (<= 0 = Red, <= 30 min = Gold, else Green)
    /// into a Brush.
    /// </summary>
    public sealed class GreenRedBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Optional thresholds via ConverterParameter: "red=0.1;yellow=0.25"
            double red = 0.10;
            double yellow = 0.25;

            if (parameter is string s && !string.IsNullOrWhiteSpace(s))
            {
                foreach (var part in s.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = part.Split('=', StringSplitOptions.RemoveEmptyEntries);
                    if (kv.Length != 2) continue;

                    if (kv[0].Trim().Equals("red", StringComparison.OrdinalIgnoreCase)
                        && double.TryParse(kv[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var r))
                        red = r;

                    if (kv[0].Trim().Equals("yellow", StringComparison.OrdinalIgnoreCase)
                        && double.TryParse(kv[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var y))
                        yellow = y;
                }
            }

            // bool -> red/green
            if (value is bool b)
                return b ? Brushes.Red : Brushes.LimeGreen;

            // double -> treat as % remaining 0..1
            if (value is double d)
            {
                if (double.IsNaN(d) || double.IsInfinity(d)) d = 0;
                if (d < 0) d = 0;
                if (d > 1) d = 1;

                if (d <= red) return Brushes.Red;
                if (d <= yellow) return Brushes.Gold;
                return Brushes.LimeGreen;
            }

            // TimeSpan -> remaining time
            if (value is TimeSpan ts)
            {
                if (ts <= TimeSpan.Zero) return Brushes.Red;
                if (ts <= TimeSpan.FromMinutes(30)) return Brushes.Gold;
                return Brushes.LimeGreen;
            }

            // string "0.12" etc
            if (value is string str && double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                if (parsed < 0) parsed = 0;
                if (parsed > 1) parsed = 1;

                if (parsed <= red) return Brushes.Red;
                if (parsed <= yellow) return Brushes.Gold;
                return Brushes.LimeGreen;
            }

            return Brushes.LimeGreen;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
 