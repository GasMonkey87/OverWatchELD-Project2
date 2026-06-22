using OverWatchELD.Models.Fleet;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace OverWatchELD.Services.Fleet
{
    public sealed class FleetTelemetryAutoSyncService
    {
        private readonly FleetTruckRepository _truckRepo = new FleetTruckRepository();

        private DateTime _lastWriteUtc = DateTime.MinValue;
        private string _lastSignature = "";

        public void SyncFromTelemetry(object? telemetrySnapshot, string? driverName)
        {
            if (telemetrySnapshot == null)
                return;

            var isOwnedTruck =
                ReadBool(telemetrySnapshot, "IsOwnedTruck") ||
                ReadBool(telemetrySnapshot, "OwnedTruck") ||
                ReadBool(telemetrySnapshot, "TruckOwned");

            if (!isOwnedTruck)
                return;

            var nowUtc = DateTime.UtcNow;

            var driver = Clean(driverName);
            if (string.IsNullOrWhiteSpace(driver))
            {
                driver = FirstNonEmpty(
                    ReadString(telemetrySnapshot, "DriverName"),
                    ReadString(telemetrySnapshot, "ProfileName"),
                    ReadString(telemetrySnapshot, "PlayerName"),
                    "Driver");
            }

            var plate = FirstNonEmpty(
                ReadString(telemetrySnapshot, "TruckLicensePlate"),
                ReadString(telemetrySnapshot, "LicensePlate"),
                ReadString(telemetrySnapshot, "Plate"),
                ReadString(telemetrySnapshot, "PlateNumber"));

            var truckName = FirstNonEmpty(
                ReadString(telemetrySnapshot, "TruckName"),
                ReadString(telemetrySnapshot, "DisplayName"),
                ReadString(telemetrySnapshot, "TruckDisplayName"));

            var make = FirstNonEmpty(
                ReadString(telemetrySnapshot, "TruckMake"),
                ReadString(telemetrySnapshot, "Make"),
                ReadString(telemetrySnapshot, "Brand"));

            var model = FirstNonEmpty(
                ReadString(telemetrySnapshot, "TruckModel"),
                ReadString(telemetrySnapshot, "Model"));

            var location = BuildLocation(telemetrySnapshot);

            var odometer = ReadDouble(
                telemetrySnapshot,
                "OdometerMiles",
                "TruckOdometerMiles",
                "Odometer");

            var fuel = ReadDouble(
                telemetrySnapshot,
                "FuelPercent",
                "TruckFuelPercent",
                "Fuel");

            var health = ReadInt(
                telemetrySnapshot,
                "TruckHealthPercent",
                "HealthPercent",
                "ConditionPercent");

            var fullTruckName = FirstNonEmpty(
                truckName,
                $"{make} {model}".Trim(),
                model,
                make,
                "Truck");

            // Throttle writes so every telemetry tick does not hit disk
            var signature = $"{driver}|{plate}|{fullTruckName}|{Math.Round(odometer, 0, MidpointRounding.AwayFromZero)}|{location}";
            if (string.Equals(signature, _lastSignature, StringComparison.OrdinalIgnoreCase) &&
                (nowUtc - _lastWriteUtc).TotalSeconds < 5)
            {
                return;
            }

            var trucks = _truckRepo.LoadAll();

            // Rule:
            // - Same plate = same truck
            // - Same driver in a DIFFERENT owned truck = create second truck
            // So we only match existing by plate first, then by strong same-driver/same-truck identity
            var existing = FindExistingTruck(trucks, plate, fullTruckName, model, driver);

            if (existing == null)
            {
                existing = new FleetTruck();
                ApplyTruckNumber(existing, GetNextTruckNumber(trucks));
                trucks.Add(existing);
            }

            ApplyString(existing, new[] { "TruckName", "Name", "DisplayName" }, fullTruckName);
            ApplyString(existing, new[] { "Model" }, model);
            ApplyString(existing, new[] { "PlateNumber", "Plate", "LicensePlate" }, plate);
            ApplyString(existing, new[] { "AssignedDriver", "DriverName" }, driver);
            ApplyString(existing, new[] { "Location", "CurrentLocation" }, location);

            ApplyDouble(existing, new[] { "OdometerMiles", "Odometer" }, Math.Max(0, odometer));
            ApplyDouble(existing, new[] { "FuelPercent", "Fuel" }, Clamp(fuel, 0, 100));
            ApplyInt(existing, new[] { "HealthPercent", "ConditionPercent" }, Math.Max(0, Math.Min(100, health)));

            ApplyDateTime(existing, new[] { "LastUpdatedUtc", "UpdatedUtc" }, nowUtc);
            ApplyString(existing, new[] { "LastUpdated" }, DateTime.Now.ToString("g", CultureInfo.InvariantCulture));

            MarkDriverActiveTruck(trucks, existing, driver);

            _truckRepo.SaveAll(trucks);

            _lastSignature = signature;
            _lastWriteUtc = nowUtc;
        }

        private static FleetTruck? FindExistingTruck(List<FleetTruck> trucks, string plate, string truckName, string model, string driver)
        {
            // Strongest identity: same physical truck by plate
            if (!string.IsNullOrWhiteSpace(plate))
            {
                var byPlate = trucks.FirstOrDefault(t =>
                    Same(ReadTruckString(t, "PlateNumber", "Plate", "LicensePlate"), plate));

                if (byPlate != null)
                    return byPlate;
            }

            // Same driver + same truck name + same model = probably same owned truck
            var byDriverTruckModel = trucks.FirstOrDefault(t =>
                Same(ReadTruckString(t, "AssignedDriver", "DriverName"), driver) &&
                Same(ReadTruckString(t, "TruckName", "Name", "DisplayName"), truckName) &&
                Same(ReadTruckString(t, "Model"), model));

            if (byDriverTruckModel != null)
                return byDriverTruckModel;

            // Same driver + same truck name fallback
            var byDriverTruck = trucks.FirstOrDefault(t =>
                Same(ReadTruckString(t, "AssignedDriver", "DriverName"), driver) &&
                Same(ReadTruckString(t, "TruckName", "Name", "DisplayName"), truckName));

            return byDriverTruck;
        }

        private static void MarkDriverActiveTruck(List<FleetTruck> trucks, FleetTruck currentTruck, string driver)
        {
            var currentTruckNumber = ReadTruckString(currentTruck, "TruckNumber", "UnitNumber", "Number", "Id");
            var currentPlate = ReadTruckString(currentTruck, "PlateNumber", "Plate", "LicensePlate");

            foreach (var truck in trucks)
            {
                var assignedDriver = ReadTruckString(truck, "AssignedDriver", "DriverName");
                if (!Same(assignedDriver, driver))
                    continue;

                var truckNumber = ReadTruckString(truck, "TruckNumber", "UnitNumber", "Number", "Id");
                var plate = ReadTruckString(truck, "PlateNumber", "Plate", "LicensePlate");

                var isCurrent =
                    (!string.IsNullOrWhiteSpace(currentPlate) && Same(plate, currentPlate)) ||
                    Same(truckNumber, currentTruckNumber);

                ApplyBool(truck, new[] { "IsActive", "Active", "IsCurrent", "Current" }, isCurrent);
                ApplyString(truck, new[] { "Status" }, isCurrent ? "Active" : "Inactive");
            }
        }

        private static string BuildLocation(object telemetrySnapshot)
        {
            var city = FirstNonEmpty(
                ReadString(telemetrySnapshot, "City"),
                ReadString(telemetrySnapshot, "CurrentCity"));

            var state = FirstNonEmpty(
                ReadString(telemetrySnapshot, "State"),
                ReadString(telemetrySnapshot, "CurrentState"));

            var location = FirstNonEmpty(
                ReadString(telemetrySnapshot, "Location"),
                ReadString(telemetrySnapshot, "NearestLocation"));

            if (!string.IsNullOrWhiteSpace(city) && !string.IsNullOrWhiteSpace(state))
                return $"{city}, {state}";

            return FirstNonEmpty(location, city, state);
        }

        private static string GetNextTruckNumber(List<FleetTruck> trucks)
        {
            var max = 0;

            foreach (var truck in trucks)
            {
                var current = ReadTruckString(truck, "TruckNumber", "UnitNumber", "Number", "Id");
                if (string.IsNullOrWhiteSpace(current))
                    continue;

                var digits = new string(current.Where(char.IsDigit).ToArray());
                if (int.TryParse(digits, out var n) && n > max)
                    max = n;
            }

            return $"TRK-{(max + 1):000}";
        }

        private static void ApplyTruckNumber(FleetTruck truck, string value)
        {
            ApplyString(truck, new[] { "TruckNumber", "UnitNumber", "Number", "Id" }, value);
        }

        private static string ReadTruckString(FleetTruck truck, params string[] names)
        {
            foreach (var name in names)
            {
                var val = ReadString(truck, name);
                if (!string.IsNullOrWhiteSpace(val))
                    return val;
            }
            return "";
        }

        private static void ApplyString(object target, string[] names, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            foreach (var name in names)
            {
                var prop = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop == null || !prop.CanWrite || prop.PropertyType != typeof(string))
                    continue;

                prop.SetValue(target, value.Trim());
                return;
            }
        }

        private static void ApplyBool(object target, string[] names, bool value)
        {
            foreach (var name in names)
            {
                var prop = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop == null || !prop.CanWrite)
                    continue;

                if (prop.PropertyType == typeof(bool) || prop.PropertyType == typeof(bool?))
                {
                    prop.SetValue(target, value);
                    return;
                }
            }
        }

        private static void ApplyDouble(object target, string[] names, double value)
        {
            foreach (var name in names)
            {
                var prop = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop == null || !prop.CanWrite)
                    continue;

                if (prop.PropertyType == typeof(double) || prop.PropertyType == typeof(double?))
                {
                    prop.SetValue(target, value);
                    return;
                }

                if (prop.PropertyType == typeof(decimal) || prop.PropertyType == typeof(decimal?))
                {
                    prop.SetValue(target, Convert.ToDecimal(value));
                    return;
                }

                if (prop.PropertyType == typeof(int) || prop.PropertyType == typeof(int?))
                {
                    prop.SetValue(target, Convert.ToInt32(Math.Round(value)));
                    return;
                }
            }
        }

        private static void ApplyInt(object target, string[] names, int value)
        {
            foreach (var name in names)
            {
                var prop = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop == null || !prop.CanWrite)
                    continue;

                if (prop.PropertyType == typeof(int) || prop.PropertyType == typeof(int?))
                {
                    prop.SetValue(target, value);
                    return;
                }

                if (prop.PropertyType == typeof(double) || prop.PropertyType == typeof(double?))
                {
                    prop.SetValue(target, Convert.ToDouble(value));
                    return;
                }
            }
        }

        private static void ApplyDateTime(object target, string[] names, DateTime value)
        {
            foreach (var name in names)
            {
                var prop = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop == null || !prop.CanWrite)
                    continue;

                if (prop.PropertyType == typeof(DateTime) || prop.PropertyType == typeof(DateTime?))
                {
                    prop.SetValue(target, value);
                    return;
                }
            }
        }

        private static string ReadString(object obj, string propertyName)
        {
            try
            {
                var prop = obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                var value = prop?.GetValue(obj);
                return value?.ToString()?.Trim() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static bool ReadBool(object obj, string propertyName)
        {
            try
            {
                var prop = obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                var value = prop?.GetValue(obj);
                if (value is bool b) return b;
                if (value == null) return false;
                return bool.TryParse(value.ToString(), out var parsed) && parsed;
            }
            catch
            {
                return false;
            }
        }

        private static double ReadDouble(object obj, params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                try
                {
                    var prop = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    var value = prop?.GetValue(obj);
                    if (value == null) continue;

                    if (double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                        return parsed;
                }
                catch
                {
                }
            }

            return 0;
        }

        private static int ReadInt(object obj, params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                try
                {
                    var prop = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    var value = prop?.GetValue(obj);
                    if (value == null) continue;

                    if (int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                        return parsed;

                    if (double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                        return Convert.ToInt32(Math.Round(d));
                }
                catch
                {
                }
            }

            return 100;
        }

        private static string Clean(string? value) => value?.Trim() ?? "";

        private static bool Same(string? a, string? b) =>
            string.Equals(Clean(a), Clean(b), StringComparison.OrdinalIgnoreCase);

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
            return "";
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        internal void OnTelemetry(TelemetrySnapshot snapshot)
        {
            throw new NotImplementedException();
        }
    }
}
