using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using OverWatchELD.Services;

namespace OverWatchELD.Converters
{
    /// <summary>
    /// Returns the current driver's name.
    /// Priority: App.Session.DriverName -> UserSession.DriverName -> EldDriverIdentityResolver.DriverName() -> "Driver"
    /// </summary>
    public sealed class DriverNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (Application.Current is OverWatchELD.App app)
                {
                    var dn = (app.Session?.DriverName ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(dn)) return dn;
                }
            }
            catch { }

            try
            {
                var dn2 = (UserSession.Instance.DisplayName ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(dn2)) return dn2;
            }
            catch { }

            try
            {
                var u = (EldDriverIdentityResolver.DriverName() ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(u)) return u;
            }
            catch { }

            return "Driver";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value;
    }
}
