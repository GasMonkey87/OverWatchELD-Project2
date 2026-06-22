using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OverWatchELD.Converters
{
    public sealed class IntGreaterThanZeroToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (value is int i && i > 0) return Visibility.Visible;
            }
            catch { }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
