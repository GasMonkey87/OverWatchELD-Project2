using System;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public sealed class TelemetryTruckCapture
    {
        public string TruckName { get; set; } = "";
        public string DriverName { get; set; } = "";
        public string Plate { get; set; } = "";
        public double FuelPct { get; set; }
        public double ConditionPct { get; set; }
        public double Odometer { get; set; }
    }

    public static class TelemetryTruckCaptureService
    {
        public static object? LastSnapshot { get; set; }

        public static TelemetryTruckCapture Capture()
        {
            var result = new TelemetryTruckCapture();

            if (LastSnapshot == null)
                return result;

            try
            {
                var json = JsonSerializer.Serialize(LastSnapshot);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                result.TruckName =
                    TryGet(root, "truckName") ??
                    TryGet(root, "truck", "name") ??
                    TryGet(root, "name") ??
                    "";

                result.DriverName =
                    TryGet(root, "driverName") ??
                    TryGet(root, "driver", "name") ??
                    TryGet(root, "driver") ??
                    "";

                result.Plate =
                    TryGet(root, "plate") ??
                    TryGet(root, "licensePlate") ??
                    TryGet(root, "plateNumber") ??
                    TryGet(root, "truck", "plate") ??
                    "";

                if (TryGetDouble(root, out var fuel, "fuelPct", "fuelPercentage", "fuel_percent", "fuel"))
                    result.FuelPct = Clamp(fuel, 0, 100);

                if (TryGetDouble(root, out var damage, "damagePct", "truckDamage", "damage_percent", "damage"))
                    result.ConditionPct = Clamp(100 - damage, 0, 100);
                else
                    result.ConditionPct = 100;

                if (TryGetDouble(root, out var odo, "odometer", "mileage", "odometerMiles"))
                    result.Odometer = Math.Max(0, odo);
            }
            catch
            {
                // Keep capture safe/fail-quiet
            }

            return result;
        }

        private static string? TryGet(JsonElement root, params string[] path)
        {
            JsonElement current = root;

            foreach (var segment in path)
            {
                if (current.ValueKind != JsonValueKind.Object ||
                    !current.TryGetProperty(segment, out current))
                    return null;
            }

            if (current.ValueKind == JsonValueKind.String)
                return current.GetString();

            if (current.ValueKind == JsonValueKind.Null || current.ValueKind == JsonValueKind.Undefined)
                return null;

            return current.ToString();
        }

        private static bool TryGetDouble(JsonElement root, out double value, params string[] keys)
        {
            value = 0;

            foreach (var key in keys)
            {
                if (!root.TryGetProperty(key, out var el))
                    continue;

                if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out value))
                    return true;

                if (double.TryParse(el.ToString(), out value))
                    return true;
            }

            return false;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}