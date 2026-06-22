using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public static class LiveMapDriverRouteTrailService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        private static string DataFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OverWatchELD");

        private static string TrailFile =>
            Path.Combine(DataFolder, "live_map_driver_trails.json");

        public static void RecordTelemetryRows(IEnumerable<object>? rows)
        {
            if (rows == null)
                return;

            try
            {
                Directory.CreateDirectory(DataFolder);

                var existing = LoadTrails();

                foreach (var row in rows)
                {
                    var driver = FirstNonEmpty(
                        Read(row, "DriverName"),
                        Read(row, "Driver"),
                        Read(row, "Name"));

                    if (string.IsNullOrWhiteSpace(driver))
                        continue;

                    var lat = ReadDouble(row, "Latitude", "Lat");
                    var lng = ReadDouble(row, "Longitude", "Lng", "Lon");
                    var worldX = ReadDouble(row, "WorldX", "X", "RawX");
                    var worldZ = ReadDouble(row, "WorldZ", "Z", "RawZ");

                    if (!lat.HasValue || !lng.HasValue)
                        continue;

                    if (!existing.TryGetValue(driver, out var trail))
                    {
                        trail = new LiveMapDriverTrailRow
                        {
                            DriverName = driver
                        };

                        existing[driver] = trail;
                    }

                    var last = trail.Points.LastOrDefault();
                    if (last != null &&
                        Math.Abs(last.Latitude - lat.Value) < 0.00001 &&
                        Math.Abs(last.Longitude - lng.Value) < 0.00001)
                    {
                        continue;
                    }

                    trail.Points.Add(new LiveMapDriverTrailPoint
                    {
                        Latitude = lat.Value,
                        Longitude = lng.Value,
                        WorldX = worldX,
                        WorldZ = worldZ,
                        RecordedUtc = DateTime.UtcNow
                    });

                    trail.Points = trail.Points
                        .OrderByDescending(x => x.RecordedUtc)
                        .Take(150)
                        .OrderBy(x => x.RecordedUtc)
                        .ToList();

                    trail.LastUpdatedUtc = DateTime.UtcNow;
                }

                SaveTrails(existing);
            }
            catch
            {
            }
        }

        public static string BuildTrailsJson()
        {
            return JsonSerializer.Serialize(
                LoadTrails().Values.OrderBy(x => x.DriverName).ToList(),
                JsonOptions);
        }

        public static void ClearOldTrails(TimeSpan maxAge)
        {
            try
            {
                var cutoff = DateTime.UtcNow.Subtract(maxAge);
                var trails = LoadTrails();

                foreach (var trail in trails.Values)
                {
                    trail.Points = trail.Points
                        .Where(x => x.RecordedUtc >= cutoff)
                        .OrderBy(x => x.RecordedUtc)
                        .ToList();

                    trail.LastUpdatedUtc = trail.Points.LastOrDefault()?.RecordedUtc ?? trail.LastUpdatedUtc;
                }

                var cleaned = trails
                    .Where(x => x.Value.Points.Count > 0)
                    .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);

                SaveTrails(cleaned);
            }
            catch
            {
            }
        }

        private static Dictionary<string, LiveMapDriverTrailRow> LoadTrails()
        {
            try
            {
                if (!File.Exists(TrailFile))
                    return new Dictionary<string, LiveMapDriverTrailRow>(StringComparer.OrdinalIgnoreCase);

                var json = File.ReadAllText(TrailFile);
                var rows = JsonSerializer.Deserialize<List<LiveMapDriverTrailRow>>(json, JsonOptions)
                           ?? new List<LiveMapDriverTrailRow>();

                return rows
                    .Where(x => !string.IsNullOrWhiteSpace(x.DriverName))
                    .GroupBy(x => x.DriverName.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.LastUpdatedUtc).First(), StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, LiveMapDriverTrailRow>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static void SaveTrails(Dictionary<string, LiveMapDriverTrailRow> rows)
        {
            try
            {
                Directory.CreateDirectory(DataFolder);

                var list = rows.Values
                    .OrderBy(x => x.DriverName)
                    .ToList();

                File.WriteAllText(TrailFile, JsonSerializer.Serialize(list, JsonOptions));
            }
            catch
            {
            }
        }

        private static string Read(object obj, string propertyName)
        {
            try
            {
                var prop = obj.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                return prop?.GetValue(obj)?.ToString()?.Trim() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static double? ReadDouble(object obj, params string[] names)
        {
            foreach (var name in names)
            {
                var raw = Read(obj, name);
                if (double.TryParse(raw, out var value))
                    return value;
            }

            return null;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }
    }

    public sealed class LiveMapDriverTrailRow
    {
        public string DriverName { get; set; } = "";
        public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
        public List<LiveMapDriverTrailPoint> Points { get; set; } = new();
    }

    public sealed class LiveMapDriverTrailPoint
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double? WorldX { get; set; }
        public double? WorldZ { get; set; }
        public DateTime RecordedUtc { get; set; } = DateTime.UtcNow;
    }
}
