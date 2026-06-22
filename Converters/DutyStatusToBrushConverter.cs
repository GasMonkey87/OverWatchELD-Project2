using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace OverWatchELD.Converters
{
    /// <summary>
    /// Returns a Brush for a duty status string. NEVER returns DependencyProperty.UnsetValue
    /// because that can crash when used for Foreground/Background bindings.
    /// </summary>
    public sealed class DutyStatusToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = value?.ToString()?.Trim();

            if (string.IsNullOrWhiteSpace(s))
                return Brushes.Gray;

            switch (s.ToUpperInvariant())
            {
                case "OFF":
                case "OFF DUTY":
                    return new SolidColorBrush(Color.FromRgb(120, 130, 140));

                case "SB":
                case "SLEEPER":
                case "SLEEPER BERTH":
                    return new SolidColorBrush(Color.FromRgb(140, 120, 200));

                case "D":
                case "DRIVING":
                    return new SolidColorBrush(Color.FromRgb(40, 170, 90));

                case "ON":
                case "ON DUTY":
                case "ON DUTY (NOT DRIVING)":
                    return new SolidColorBrush(Color.FromRgb(60, 140, 220));

                default:
                    return Brushes.Gray;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
