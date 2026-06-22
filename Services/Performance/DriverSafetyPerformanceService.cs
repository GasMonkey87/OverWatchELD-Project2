using OverWatchELD.Models.Performance;
using OverWatchELD.Services.Economy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OverWatchELD.Services.Performance
{
    public static class DriverSafetyPerformanceService
    {
        public static List<DriverSafetyPerformanceRow> BuildLeaderboard()
        {
            try
            {
                RealDriverEconomyPayrollService.SyncDeliveredLoadsAndPayroll();
            }
            catch
            {
            }

            var economyRows = RealDriverEconomyPayrollService.BuildDriverSummaries();
            var events = DriverPerformanceEventStore.Load();

            var rows = economyRows
                .Where(x => !string.IsNullOrWhiteSpace(x.DriverName))
                .Select(e =>
                {
                    var driverEvents = events
                        .Where(ev => Same(ev.DriverName, e.DriverName) ||
                                     (!string.IsNullOrWhiteSpace(e.DriverDiscordId) && Same(ev.DriverDiscordId, e.DriverDiscordId)))
                        .ToList();

                    var speeding = Count(driverEvents, "Speeding");
                    var harshBrake = Count(driverEvents, "HarshBrake");
                    var idle = Count(driverEvents, "Idle");
                    var damage = Count(driverEvents, "Damage");
                    var late = Count(driverEvents, "LateDelivery");

                    var safety = CalculateSafetyScore(e.MilesDriven, speeding, harshBrake, damage);
                    var performance = CalculatePerformanceScore(e.LoadsDelivered, e.MilesDriven, late);
                    var economy = CalculateEconomyScore(e.GrossRevenue, e.CompanyProfit, e.MilesDriven, idle);

                    var overall = Math.Round((safety * 0.45) + (performance * 0.35) + (economy * 0.20), 1);

                    return new DriverSafetyPerformanceRow
                    {
                        DriverName = e.DriverName,
                        DriverDiscordId = e.DriverDiscordId,
                        TruckName = e.TruckName,
                        TruckNumber = e.TruckNumber,
                        LoadsDelivered = e.LoadsDelivered,
                        LoadsPickedUp = e.LoadsPickedUp,
                        MilesDriven = e.MilesDriven,
                        GrossRevenue = e.GrossRevenue,
                        PayrollPaid = e.PayrollPaid,
                        CompanyProfit = e.CompanyProfit,
                        SpeedingEvents = speeding,
                        HarshBrakeEvents = harshBrake,
                        IdleEvents = idle,
                        DamageEvents = damage,
                        LateDeliveries = late,
                        SafetyScore = safety,
                        PerformanceScore = performance,
                        EconomyScore = economy,
                        OverallScore = overall,
                        Grade = Grade(overall),
                        LastActivityUtc = e.LastDeliveryUtc,
                        GeneratedUtc = DateTime.UtcNow
                    };
                })
                .OrderByDescending(x => x.OverallScore)
                .ThenByDescending(x => x.CompanyProfit)
                .ThenByDescending(x => x.LoadsDelivered)
                .ToList();

            for (var i = 0; i < rows.Count; i++)
                rows[i].Rank = i + 1;

            return rows;
        }

        public static void ScanTelemetrySnapshot(object? telemetrySnapshot)
        {
            if (telemetrySnapshot == null)
                return;

            try
            {
                var driver = FirstNonEmpty(
                    Read(telemetrySnapshot, "DriverName"),
                    Read(telemetrySnapshot, "Driver"),
                    Read(telemetrySnapshot, "Name"));

                if (string.IsNullOrWhiteSpace(driver))
                    return;

                var truckName = FirstNonEmpty(
                    Read(telemetrySnapshot, "TruckName"),
                    Read(telemetrySnapshot, "Truck"),
                    Read(telemetrySnapshot, "VehicleName"));

                var speed = ReadDouble(telemetrySnapshot, "SpeedMph", "Speed", "Mph") ?? 0;
                var damage = ReadDouble(telemetrySnapshot, "DamagePercent", "TruckDamage", "WearPercent") ?? 0;
                var fuel = ReadDouble(telemetrySnapshot, "FuelPercent", "Fuel") ?? 0;
                var status = FirstNonEmpty(Read(telemetrySnapshot, "Status"), Read(telemetrySnapshot, "DutyStatus"));

                if (speed > 80 && !DriverPerformanceEventStore.HasRecentEvent(driver, "Speeding", TimeSpan.FromMinutes(5)))
                {
                    DriverPerformanceEventStore.Add(new DriverPerformanceEvent
                    {
                        DriverName = driver,
                        TruckName = truckName,
                        EventType = "Speeding",
                        Severity = speed >= 90 ? "High" : "Medium",
                        Value = speed,
                        Description = $"Speeding event detected at {speed:0} mph.",
                        Source = "Telemetry"
                    });
                }

                if (damage >= 8 && !DriverPerformanceEventStore.HasRecentEvent(driver, "Damage", TimeSpan.FromMinutes(15)))
                {
                    DriverPerformanceEventStore.Add(new DriverPerformanceEvent
                    {
                        DriverName = driver,
                        TruckName = truckName,
                        EventType = "Damage",
                        Severity = damage >= 20 ? "High" : "Medium",
                        Value = damage,
                        Description = $"Truck damage/wear event detected: {damage:0.#}%.",
                        Source = "Telemetry"
                    });
                }

                if (status.Contains("On", StringComparison.OrdinalIgnoreCase) &&
                    speed < 1 &&
                    fuel > 0 &&
                    !DriverPerformanceEventStore.HasRecentEvent(driver, "Idle", TimeSpan.FromMinutes(30)))
                {
                    DriverPerformanceEventStore.Add(new DriverPerformanceEvent
                    {
                        DriverName = driver,
                        TruckName = truckName,
                        EventType = "Idle",
                        Severity = "Low",
                        Value = speed,
                        Description = "Possible idle event detected while on duty.",
                        Source = "Telemetry"
                    });
                }
            }
            catch
            {
            }
        }

        public static void RecordLateDelivery(string driverName, string truckName, string loadNumber)
        {
            DriverPerformanceEventStore.Add(new DriverPerformanceEvent
            {
                DriverName = driverName ?? "",
                TruckName = truckName ?? "",
                EventType = "LateDelivery",
                Severity = "Medium",
                Value = 1,
                Description = $"Late delivery recorded for load {loadNumber}.",
                Source = "Dispatch"
            });
        }

        public static void RecordHarshBrake(string driverName, string truckName, double value = 1)
        {
            DriverPerformanceEventStore.Add(new DriverPerformanceEvent
            {
                DriverName = driverName ?? "",
                TruckName = truckName ?? "",
                EventType = "HarshBrake",
                Severity = value >= 2 ? "High" : "Medium",
                Value = value,
                Description = "Harsh braking event recorded.",
                Source = "Telemetry"
            });
        }

        private static double CalculateSafetyScore(double miles, int speeding, int harshBrake, int damage)
        {
            var score = 100.0;

            var mileageFactor = Math.Max(1, miles / 1000.0);

            score -= speeding * (6.0 / mileageFactor);
            score -= harshBrake * (7.5 / mileageFactor);
            score -= damage * (10.0 / mileageFactor);

            return Clamp(score);
        }

        private static double CalculatePerformanceScore(int delivered, double miles, int late)
        {
            var score = 75.0;

            score += Math.Min(15, delivered * 1.5);

            if (miles > 0)
                score += Math.Min(10, miles / 2500.0);

            score -= late * 8.0;

            return Clamp(score);
        }

        private static double CalculateEconomyScore(decimal gross, decimal profit, double miles, int idleEvents)
        {
            var score = 80.0;

            if (gross > 0)
            {
                var margin = (double)(profit / gross);
                score += margin * 20.0;
            }

            if (miles > 0 && gross > 0)
            {
                var rpm = (double)(gross / (decimal)miles);
                if (rpm >= 4.0) score += 5;
                if (rpm < 2.5) score -= 8;
            }

            score -= idleEvents * 3.0;

            return Clamp(score);
        }

        private static double Clamp(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return 0;

            return Math.Max(0, Math.Min(100, Math.Round(value, 1)));
        }

        private static string Grade(double score)
        {
            return score switch
            {
                >= 97 => "S+",
                >= 93 => "S",
                >= 90 => "A",
                >= 80 => "B",
                >= 70 => "C",
                >= 60 => "D",
                _ => "F"
            };
        }

        private static int Count(List<DriverPerformanceEvent> events, string type)
        {
            return events.Count(x => string.Equals(x.EventType, type, StringComparison.OrdinalIgnoreCase));
        }

        private static bool Same(string? a, string? b)
        {
            return !string.IsNullOrWhiteSpace(a) &&
                   !string.IsNullOrWhiteSpace(b) &&
                   string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
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
                var raw = Read(obj, name)
                    .Replace(",", "")
                    .Replace("%", "")
                    .Trim();

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
}
