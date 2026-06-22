using System;
using System.Globalization;
using System.Windows.Data;

namespace OverWatchELD.Converters
{
    public class DutyEventTooltipConverter : IMultiValueConverter
    {
        // values[0] = status (object, typically string or enum)
        // values[1] = startUtc (DateTime / DateTimeOffset / string)
        // values[2] = endUtc (DateTime / DateTimeOffset / string)
        // values[3] = notes (string)
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            string status = values.Length > 0 && values[0] != null ? values[0].ToString() : null;
            DateTime? startUtc = ParseToUtc(values, 1);
            DateTime? endUtc = ParseToUtc(values, 2);
            string notes = values.Length > 3 && values[3] != null ? values[3].ToString() : null;

            // Build tooltip parts
            var parts = new System.Text.StringBuilder();

            if (!string.IsNullOrWhiteSpace(status))
            {
                parts.AppendLine(status);
            }

            if (startUtc.HasValue || endUtc.HasValue)
            {
                string startText = startUtc.HasValue ? startUtc.Value.ToLocalTime().ToString("g", culture) : "—";
                string endText = endUtc.HasValue ? endUtc.Value.ToLocalTime().ToString("g", culture) : "—";
                parts.AppendLine($"{startText} — {endText}");

                if (startUtc.HasValue && endUtc.HasValue && endUtc.Value >= startUtc.Value)
                {
                    var duration = endUtc.Value - startUtc.Value;
                    parts.AppendLine(FormatDuration(duration));
                }
            }

            if (!string.IsNullOrWhiteSpace(notes))
            {
                parts.AppendLine();
                parts.Append(notes.Trim());
            }

            var result = parts.ToString().TrimEnd('\r', '\n');
            return string.IsNullOrEmpty(result) ? null : result;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        private static DateTime? ParseToUtc(object[] values, int index)
        {
            if (values == null || index >= values.Length) return null;
            var o = values[index];
            if (o == null) return null;

            if (o is DateTime dt)
            {
                // If DateTime.Kind is Unspecified, assume UTC if value is named "StartUtc"/"EndUtc".
                // Prefer treating as UTC to match naming used in XAML.
                if (dt.Kind == DateTimeKind.Utc) return dt;
                if (dt.Kind == DateTimeKind.Local) return dt.ToUniversalTime();
                // Unspecified -> treat as UTC
                return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            }

            if (o is DateTimeOffset dto)
            {
                return dto.UtcDateTime;
            }

            if (o is string s && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                return parsed;
            }

            return null;
        }

        private static string FormatDuration(TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
            {
                return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            }
            if (ts.TotalMinutes >= 1)
            {
                return $"{(int)ts.TotalMinutes}m";
            }
            return $"{ts.Seconds}s";
        }
    }
}

