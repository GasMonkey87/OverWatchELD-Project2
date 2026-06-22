using System;
using DutyStatus = OverWatchELD.Models.DutyStatus;
using System.Globalization;
using System.Windows.Data;
using OverWatchELD.Models;

namespace OverWatchELD.Converters
{
    public sealed class DutyStatusEqualsConverter : IValueConverter
    {
        // value = current DutyStatus
        // parameter = enum name (string), e.g. "OffDuty", "Sleeper", "Driving", "OnDuty", "PersonalConveyance", "YardMove"
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not DutyStatus current) return false;
            if (parameter == null) return false;

            if (parameter is DutyStatus ds) return current == ds;

            var s = parameter.ToString();
            if (string.IsNullOrWhiteSpace(s)) return false;

            if (Enum.TryParse<DutyStatus>(s, ignoreCase: true, out var desired))
                return current == desired;

            return false;
        }

        // If user clicks a ToggleButton, ConvertBack returns the desired DutyStatus
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter == null) return Binding.DoNothing;

            if (parameter is DutyStatus ds) return ds;

            var s = parameter.ToString();
            if (string.IsNullOrWhiteSpace(s)) return Binding.DoNothing;

            if (Enum.TryParse<DutyStatus>(s, ignoreCase: true, out var desired))
                return desired;

            return Binding.DoNothing;
        }
    }
}
