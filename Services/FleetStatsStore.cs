using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services
{
    /// <summary>
    /// Simple persistent fleet stats store (per driver + per truck).
    /// Keeps totals so FleetAutoLoggerService can record mileage/fuel/damage events.
    /// Saved under %APPDATA%\OverWatchELD\fleet_stats.json
    /// </summary>
    public static class FleetStatsStore
    {
        private static readonly object _lock = new();

        private sealed class Root
        {
            public Dictionary<string, DriverTotals> Drivers { get; set; } = new();
        }

        private sealed class DriverTotals
        {
            public string DriverId { get; set; } = "";
            public string DriverName { get; set; } = "Driver";

            // per-truck totals
            public Dictionary<string, TruckTotals> Trucks { get; set; } = new();

            // overall totals (all trucks)
            public double TotalMiles { get; set; }
            public double TotalFuelGallons { get; set; }
            public int DamageIncidents { get; set; }
            public double MaxDamagePct { get; set; } // 0..100

            public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
        }

        private sealed class TruckTotals
        {
            public string TruckId { get; set; } = "";
            public string TruckName { get; set; } = "Truck";

            public double Miles { get; set; }
            public double FuelGallons { get; set; }

            public int DamageIncidents { get; set; }
            public double MaxDamagePct { get; set; } // 0..100

            public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
        }

        // Public read model for reports / UI
        public sealed class DriverStatsRow
        {
            public string DriverId { get; init; } = "";
            public string DriverName { get; init; } = "Driver";

            public double Miles { get; init; }
            public double FuelUsed { get; init; }
            public double Mpg { get; init; }

            public int TrucksUsed { get; init; }
            public int DamageIncidents { get; init; }
            public double MaxDamagePct { get; init; } // 0..100

            public DateTimeOffset UpdatedUtc { get; init; }
        }

        public static void AddMileage(string driverId, string driverName, string truckId, string truckName, double miles)
        {
            if (string.IsNullOrWhiteSpace(driverId)) return;
            if (string.IsNullOrWhiteSpace(truckId)) return;
            if (miles <= 0) return;

            lock (_lock)
            {
                var root = LoadUnsafe();

                if (!root.Drivers.TryGetValue(driverId, out var d))
                {
                    d = new DriverTotals { DriverId = driverId, DriverName = SafeName(driverName) };
                    root.Drivers[driverId] = d;
                }
                d.DriverName = SafeName(driverName);

                if (!d.Trucks.TryGetValue(truckId, out var t))
                {
                    t = new TruckTotals { TruckId = truckId, TruckName = SafeName(truckName) };
                    d.Trucks[truckId] = t;
                }
                t.TruckName = SafeName(truckName);

                t.Miles += miles;
                d.TotalMiles += miles;

                t.UpdatedUtc = DateTimeOffset.UtcNow;
                d.UpdatedUtc = DateTimeOffset.UtcNow;

                SaveUnsafe(root);
            }
        }

        public static void AddFuelUsed(string driverId, string truckId, double gallonsUsed)
        {
            if (string.IsNullOrWhiteSpace(driverId)) return;
            if (string.IsNullOrWhiteSpace(truckId)) return;
            if (gallonsUsed <= 0) return;

            lock (_lock)
            {
                var root = LoadUnsafe();

                if (!root.Drivers.TryGetValue(driverId, out var d))
                {
                    d = new DriverTotals { DriverId = driverId, DriverName = "Driver" };
                    root.Drivers[driverId] = d;
                }

                if (!d.Trucks.TryGetValue(truckId, out var t))
                {
                    t = new TruckTotals { TruckId = truckId, TruckName = "Truck" };
                    d.Trucks[truckId] = t;
                }

                t.FuelGallons += gallonsUsed;
                d.TotalFuelGallons += gallonsUsed;

                t.UpdatedUtc = DateTimeOffset.UtcNow;
                d.UpdatedUtc = DateTimeOffset.UtcNow;

                SaveUnsafe(root);
            }
        }

        public static void RecordDamageSpike(string driverId, string truckId, double deltaPct, double newDamagePct)
        {
            if (string.IsNullOrWhiteSpace(driverId)) return;
            if (string.IsNullOrWhiteSpace(truckId)) return;
            if (deltaPct <= 0) return;

            // normalize (if somehow 0..1)
            if (newDamagePct <= 1.001) newDamagePct *= 100.0;
            newDamagePct = Math.Clamp(newDamagePct, 0.0, 100.0);

            lock (_lock)
            {
                var root = LoadUnsafe();

                if (!root.Drivers.TryGetValue(driverId, out var d))
                {
                    d = new DriverTotals { DriverId = driverId, DriverName = "Driver" };
                    root.Drivers[driverId] = d;
                }

                if (!d.Trucks.TryGetValue(truckId, out var t))
                {
                    t = new TruckTotals { TruckId = truckId, TruckName = "Truck" };
                    d.Trucks[truckId] = t;
                }

                t.DamageIncidents++;
                d.DamageIncidents++;

                if (newDamagePct > t.MaxDamagePct) t.MaxDamagePct = newDamagePct;
                if (newDamagePct > d.MaxDamagePct) d.MaxDamagePct = newDamagePct;

                t.UpdatedUtc = DateTimeOffset.UtcNow;
                d.UpdatedUtc = DateTimeOffset.UtcNow;

                SaveUnsafe(root);
            }
        }

        /// <summary>
        /// Used by weekly report service (reflection looks for this name).
        /// Returns totals (not restricted by date range yet).
        /// </summary>
        public static System.Threading.Tasks.Task<List<DriverStatsRow>> GetDriverStatsAsync(DateTime fromUtc, DateTime toUtc)
        {
            // This store is totals-based for now. Date range params are accepted for compatibility.
            lock (_lock)
            {
                var root = LoadUnsafe();

                var rows = root.Drivers.Values
                    .Select(d =>
                    {
                        var miles = d.TotalMiles;
                        var fuel = d.TotalFuelGallons;
                        var mpg = (miles > 0.001 && fuel > 0.001) ? (miles / fuel) : 0.0;

                        return new DriverStatsRow
                        {
                            DriverId = d.DriverId,
                            DriverName = d.DriverName,
                            Miles = miles,
                            FuelUsed = fuel,
                            Mpg = mpg,
                            TrucksUsed = d.Trucks.Count,
                            DamageIncidents = d.DamageIncidents,
                            MaxDamagePct = d.MaxDamagePct,
                            UpdatedUtc = d.UpdatedUtc
                        };
                    })
                    .OrderByDescending(r => r.Miles)
                    .ToList();

                return System.Threading.Tasks.Task.FromResult(rows);
            }
        }

        private static Root LoadUnsafe()
        {
            try
            {
                var path = GetPath();
                if (!File.Exists(path)) return new Root();

                var json = File.ReadAllText(path);
                var obj = JsonSerializer.Deserialize<Root>(json);
                return obj ?? new Root();
            }
            catch
            {
                return new Root();
            }
        }

        private static void SaveUnsafe(Root root)
        {
            try
            {
                var path = GetPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var json = JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch
            {
                // ignore
            }
        }

        private static string GetPath()
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OverWatchELD", "fleet_stats.json");

        private static string SafeName(string? s)
        {
            s = (s ?? "").Trim();
            return string.IsNullOrWhiteSpace(s) ? "Driver" : s;
        }
    }
}