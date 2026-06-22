using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public static class DriverPerformanceStore
    {
        private static readonly object _gate = new();

        private static readonly string PathRoot =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "Performance");

        private static Dictionary<string, DriverPerf> _data = Load();

        public static void AddMiles(string id, double miles)
        {
            if (string.IsNullOrWhiteSpace(id) || miles <= 0)
                return;

            lock (_gate)
            {
                var d = GetInternal(id);
                d.MilesTotal += miles;
                d.MilesWeek += miles;
                d.MilesToday += miles;
                d.LastSeenUtc = DateTime.UtcNow;
                Save();
            }
        }

        public static void AddLoad(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return;

            lock (_gate)
            {
                var d = GetInternal(id);
                d.LoadsTotal++;
                d.LoadsWeek++;
                d.LoadsToday++;
                d.LastSeenUtc = DateTime.UtcNow;
                Save();
            }
        }

        public static void UpdateLastSeen(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return;

            lock (_gate)
            {
                var d = GetInternal(id);
                d.LastSeenUtc = DateTime.UtcNow;
                Save();
            }
        }

        public static void UpdateBehaviorMetrics(
            string id,
            int hardBrakes,
            int overspeedEvents,
            int speedingMinutes,
            int idleMinutes,
            double idlePercent,
            int hosViolations)
        {
            if (string.IsNullOrWhiteSpace(id))
                return;

            lock (_gate)
            {
                var d = GetInternal(id);
                d.HardBrakes = hardBrakes;
                d.OverspeedEvents = overspeedEvents;
                d.SpeedingMinutes = speedingMinutes;
                d.IdleMinutes = idleMinutes;
                d.IdlePercent = idlePercent;
                d.HosViolations = hosViolations;
                d.LastSeenUtc = DateTime.UtcNow;
                Save();
            }
        }

        public static DriverPerf Get(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return new DriverPerf();

            lock (_gate)
            {
                return GetInternal(id).Clone();
            }
        }

        private static DriverPerf GetInternal(string id)
        {
            id = (id ?? "").Trim();

            if (!_data.TryGetValue(id, out var d))
            {
                d = new DriverPerf();
                _data[id] = d;
            }

            return d;
        }

        private static Dictionary<string, DriverPerf> Load()
        {
            try
            {
                var file = Path.Combine(PathRoot, "performance.json");
                if (!File.Exists(file))
                    return new Dictionary<string, DriverPerf>(StringComparer.OrdinalIgnoreCase);

                return JsonSerializer.Deserialize<Dictionary<string, DriverPerf>>(File.ReadAllText(file))
                       ?? new Dictionary<string, DriverPerf>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, DriverPerf>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(PathRoot);
                var file = Path.Combine(PathRoot, "performance.json");
                File.WriteAllText(
                    file,
                    JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
            }
        }

        public sealed class DriverPerf
        {
            public double MilesToday { get; set; }
            public double MilesWeek { get; set; }
            public double MilesTotal { get; set; }

            public int LoadsToday { get; set; }
            public int LoadsWeek { get; set; }
            public int LoadsTotal { get; set; }

            public int HardBrakes { get; set; }
            public int OverspeedEvents { get; set; }
            public int SpeedingMinutes { get; set; }
            public int IdleMinutes { get; set; }
            public double IdlePercent { get; set; }
            public int HosViolations { get; set; }

            public DateTime LastSeenUtc { get; set; }

            public double Score =>
                (MilesWeek * 1.0) +
                (LoadsWeek * 250.0) -
                (HardBrakes * 25.0) -
                (OverspeedEvents * 20.0) -
                (HosViolations * 100.0);

            public DriverPerf Clone()
            {
                return new DriverPerf
                {
                    MilesToday = MilesToday,
                    MilesWeek = MilesWeek,
                    MilesTotal = MilesTotal,
                    LoadsToday = LoadsToday,
                    LoadsWeek = LoadsWeek,
                    LoadsTotal = LoadsTotal,
                    HardBrakes = HardBrakes,
                    OverspeedEvents = OverspeedEvents,
                    SpeedingMinutes = SpeedingMinutes,
                    IdleMinutes = IdleMinutes,
                    IdlePercent = IdlePercent,
                    HosViolations = HosViolations,
                    LastSeenUtc = LastSeenUtc
                };
            }
        }
    }
}