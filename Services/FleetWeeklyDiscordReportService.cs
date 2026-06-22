using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OverWatchELD.Services
{
    /// <summary>
    /// Posts a weekly fleet summary to a Discord webhook.
    /// Safe: will NOT crash your app if fleet DB/services aren't available yet.
    /// Prevents duplicates by storing a week-key in:
    /// %APPDATA%\OverWatchELD\fleet_report_state.txt
    /// </summary>
    public static class FleetWeeklyDiscordReportService
    {
        private static readonly object _lock = new();
        private static System.Timers.Timer? _timer;
        private static bool _started;

        // Default schedule: Sunday @ 09:00 local time
        public static DayOfWeek ReportDay { get; set; } = DayOfWeek.Sunday;
        public static int ReportHourLocal { get; set; } = 9;
        public static int ReportMinuteLocal { get; set; } = 0;

        // Check cadence (kept small so it will catch the hour even if app starts late)
        public static TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(5);

        public static void Start()
        {
            lock (_lock)
            {
                if (_started) return;
                _started = true;

                _timer = new System.Timers.Timer(CheckInterval.TotalMilliseconds);
                _timer.AutoReset = true;
                _timer.Elapsed += async (_, __) => await TickAsync().ConfigureAwait(false);
                _timer.Start();
            }

            // fire once at startup too
            _ = TickAsync();
        }

        public static void Stop()
        {
            lock (_lock)
            {
                try
                {
                    _timer?.Stop();
                    _timer?.Dispose();
                }
                catch { }
                finally
                {
                    _timer = null;
                    _started = false;
                }
            }
        }

        /// <summary>
        /// Saves the webhook URL used for weekly reports.
        /// Stored at %APPDATA%\OverWatchELD\fleet_report.json
        /// </summary>
        public static void SaveWebhookUrl(string webhookUrl)
        {
            try
            {
                var cfg = new FleetReportConfig { WeeklyFleetWebhookUrl = (webhookUrl ?? "").Trim() };
                var path = GetConfigPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        private static async Task TickAsync()
        {
            try
            {
                var cfg = LoadConfig();
                var webhook = (cfg.WeeklyFleetWebhookUrl ?? "").Trim();
                if (string.IsNullOrWhiteSpace(webhook))
                    return;

                var now = DateTime.Now;

                // Only run on schedule day/time (within the check interval window)
                if (now.DayOfWeek != ReportDay) return;
                if (now.Hour != ReportHourLocal) return;

                // allow timer drift
                var minuteDelta = Math.Abs(now.Minute - ReportMinuteLocal);
                if (minuteDelta > Math.Max(2, (int)Math.Ceiling(CheckInterval.TotalMinutes)))
                    return;

                // Gate: only post once per ISO week
                var thisWeekKey = GetIsoWeekKey(DateTime.UtcNow);
                var statePath = GetStatePath();

                try
                {
                    if (File.Exists(statePath))
                    {
                        var lastKey = (await File.ReadAllTextAsync(statePath).ConfigureAwait(false)).Trim();
                        if (string.Equals(lastKey, thisWeekKey, StringComparison.OrdinalIgnoreCase))
                            return;
                    }
                }
                catch { }

                // Build report for last 7 days
                var toUtc = DateTime.UtcNow;
                var fromUtc = toUtc.AddDays(-7);

                var reportText = await BuildReportTextAsync(fromUtc, toUtc).ConfigureAwait(false);

                bool ok = false;
                try
                {
                    ok = await DiscordWebhookService.SendTextAsync(webhook, reportText).ConfigureAwait(false);
                }
                catch { ok = false; }

                if (ok)
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
                        await File.WriteAllTextAsync(statePath, thisWeekKey).ConfigureAwait(false);
                    }
                    catch { }
                }
            }
            catch
            {
                // Never throw from scheduler
            }
        }

        private static async Task<string> BuildReportTextAsync(DateTime fromUtc, DateTime toUtc)
        {
            try
            {
                var rows = await TryGetFleetStatsRowsAsync(fromUtc, toUtc).ConfigureAwait(false);
                return FormatDiscordReport(rows, fromUtc, toUtc);
            }
            catch
            {
                return FormatDiscordReport(new List<object>(), fromUtc, toUtc);
            }
        }

        private static async Task<List<object>> TryGetFleetStatsRowsAsync(DateTime fromUtc, DateTime toUtc)
        {
            // Try to find a metrics service without hard compile-time dependency.
            var candidates = new[]
            {
                "OverWatchELD.Services.FleetMetricsService, OverWatchELD",
                "OverWatchELD.Services.FleetMetricsService",
                "OverWatchELD.Services.FleetManagerMetricsService, OverWatchELD",
                "OverWatchELD.Services.FleetManagerMetricsService",
                "OverWatchELD.Services.FleetStatsStore, OverWatchELD",
                "OverWatchELD.Services.FleetStatsStore"
            };

            foreach (var name in candidates)
            {
                var t = Type.GetType(name, throwOnError: false);
                if (t == null) continue;

                var m = t.GetMethod("GetDriverStatsAsync", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (m == null) continue;

                var ps = m.GetParameters();
                if (ps.Length != 2) continue;

                object? taskObj = null;

                try
                {
                    if (ps[0].ParameterType == typeof(DateTime) && ps[1].ParameterType == typeof(DateTime))
                        taskObj = m.Invoke(null, new object[] { fromUtc, toUtc });

                    if (taskObj == null &&
                        ps[0].ParameterType == typeof(DateTimeOffset) && ps[1].ParameterType == typeof(DateTimeOffset))
                        taskObj = m.Invoke(null, new object[] { new DateTimeOffset(fromUtc, TimeSpan.Zero), new DateTimeOffset(toUtc, TimeSpan.Zero) });
                }
                catch { taskObj = null; }

                if (taskObj == null) continue;

                try
                {
                    var awaited = await AwaitUnknownTask(taskObj).ConfigureAwait(false);
                    var list = new List<object>();

                    if (awaited is System.Collections.IEnumerable en)
                    {
                        foreach (var row in en)
                        {
                            if (row != null) list.Add(row);
                        }
                    }

                    return list;
                }
                catch { }
            }

            return new List<object>();
        }

        private static async Task<object?> AwaitUnknownTask(object taskObj)
        {
            dynamic d = taskObj;
            await d;
            try { return d.GetAwaiter().GetResult(); }
            catch { return null; }
        }

        private static string FormatDiscordReport(List<object> rows, DateTime fromUtc, DateTime toUtc)
        {
            rows.Sort((a, b) =>
            {
                var ma = ReadDouble(a, "Miles", "TotalMiles", "Mileage", "DistanceMiles");
                var mb = ReadDouble(b, "Miles", "TotalMiles", "Mileage", "DistanceMiles");
                return mb.CompareTo(ma);
            });

            var sb = new StringBuilder();
            sb.AppendLine("**Weekly Fleet Report**");
            sb.AppendLine($"Range: **{fromUtc:yyyy-MM-dd} → {toUtc:yyyy-MM-dd}** (UTC)");
            sb.AppendLine();

            if (rows.Count == 0)
            {
                sb.AppendLine("_No fleet data recorded in this period._");
                return TrimForDiscord(sb.ToString());
            }

            sb.AppendLine("**Top Drivers**");

            var count = Math.Min(10, rows.Count);
            for (int i = 0; i < count; i++)
            {
                var r = rows[i];

                var driverName = ReadString(r, "DriverName", "Name", "DisplayName") ?? "(unknown)";
                var miles = ReadDouble(r, "Miles", "TotalMiles", "Mileage", "DistanceMiles");
                var gallons = ReadDouble(r, "Gallons", "FuelUsed", "GallonsUsed", "FuelGallonsUsed");
                var mpg = ReadDouble(r, "Mpg", "MPG");

                var trucks = (int)ReadDouble(r, "TrucksUsed", "TruckCount", "UniqueTrucks");
                var maxDmg = ReadDouble(r, "MaxDamagePercent", "MaxDamagePct", "DamageMax", "MaxDamage");
                var incidents = (int)ReadDouble(r, "Incidents", "DamageIncidents", "DamageEvents");

                if (maxDmg > 0 && maxDmg <= 1.001) maxDmg *= 100.0;

                if (mpg <= 0.0001 && miles > 0.0001 && gallons > 0.0001)
                    mpg = miles / gallons;

                sb.Append($"{i + 1}. **{driverName}** — {miles:N1} mi");
                if (gallons > 0.0001) sb.Append($" | {gallons:N1} gal");
                if (mpg > 0.0001) sb.Append($" | {mpg:N2} mpg");
                if (trucks > 0) sb.Append($" | trucks {trucks}");
                if (maxDmg > 0.0001) sb.Append($" | dmg {maxDmg:N1}%");
                if (incidents > 0) sb.Append($" | incidents {incidents}");
                sb.AppendLine();
            }

            return TrimForDiscord(sb.ToString());
        }

        private static string? ReadString(object o, params string[] props)
        {
            try
            {
                var t = o.GetType();
                foreach (var p in props)
                {
                    var pi = t.GetProperty(p);
                    if (pi == null) continue;
                    var v = pi.GetValue(o);
                    if (v is string s && !string.IsNullOrWhiteSpace(s)) return s.Trim();
                }
            }
            catch { }
            return null;
        }

        private static double ReadDouble(object o, params string[] props)
        {
            try
            {
                var t = o.GetType();
                foreach (var p in props)
                {
                    var pi = t.GetProperty(p);
                    if (pi == null) continue;
                    var v = pi.GetValue(o);
                    if (v == null) continue;

                    if (v is double d) return d;
                    if (v is float f) return f;
                    if (v is int i) return i;
                    if (v is long l) return l;
                    if (v is decimal m) return (double)m;

                    if (v is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                        return parsed;
                }
            }
            catch { }
            return 0.0;
        }

        private static string TrimForDiscord(string s)
        {
            const int max = 1800;
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= max ? s : (s.Substring(0, max) + "…");
        }

        private static string GetIsoWeekKey(DateTime utc)
        {
            var week = ISOWeek.GetWeekOfYear(utc);
            var year = ISOWeek.GetYear(utc);
            return $"{year:D4}-W{week:D2}";
        }

        private sealed class FleetReportConfig
        {
            public string WeeklyFleetWebhookUrl { get; set; } = "";
        }

        private static FleetReportConfig LoadConfig()
        {
            try
            {
                var path = GetConfigPath();
                if (!File.Exists(path)) return new FleetReportConfig();
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<FleetReportConfig>(json) ?? new FleetReportConfig();
            }
            catch
            {
                return new FleetReportConfig();
            }
        }

        private static string GetConfigPath()
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OverWatchELD", "fleet_report.json");

        private static string GetStatePath()
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OverWatchELD", "fleet_report_state.txt");
    }
}