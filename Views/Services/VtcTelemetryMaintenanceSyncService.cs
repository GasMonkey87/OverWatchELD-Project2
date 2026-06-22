using OverWatchELD.Models;
using OverWatchELD.Services.Fleet;
using OverWatchELD.Stores;
using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace OverWatchELD.Services
{
    public static class VtcTelemetryMaintenanceSyncService
    {
        public static void SyncFromRows(IEnumerable rows)
        {
            var state = VtcMaintenanceStore.Load();
            var fleetStore = new FleetCommandStore();
            var registeredFleet = fleetStore.LoadAll();

            var changed = false;

            foreach (var row in rows)
            {
                var truckName = Read(row, "Truck", "TruckName", "Vehicle", "VehicleName");
                var truckNumber = Read(row, "TruckNumber", "UnitNumber", "Unit", "FleetNumber");
                var plate = Read(row, "Plate", "PlateNumber");
                var driver = Read(row, "Driver", "DriverName", "Name");

                if (string.IsNullOrWhiteSpace(truckName) &&
                    string.IsNullOrWhiteSpace(truckNumber) &&
                    string.IsNullOrWhiteSpace(plate))
                    continue;

                var registered = registeredFleet.FirstOrDefault(t =>
                    Same(t.TruckNumber, truckNumber) ||
                    Same(t.TruckName, truckName) ||
                    Same(t.PlateNumber, plate) ||
                    Same(t.AssignedDriver, driver));

                // IMPORTANT:
                // Do NOT create VTC Maintenance trucks from telemetry.
                // Maintenance only tracks trucks registered in Fleet Command Center.
                if (registered == null)
                    continue;

                var truck = state.Trucks.FirstOrDefault(t =>
                    Same(t.TruckId, registered.Id) ||
                    Same(t.UnitNumber, registered.TruckNumber) ||
                    Same(t.TruckName, registered.TruckName) ||
                    Same(t.PlateNumber, registered.PlateNumber));

                if (truck == null)
                {
                    truck = new VtcMaintenanceTruck
                    {
                        TruckId = registered.Id,
                        UnitNumber = registered.TruckNumber,
                        TruckName = registered.TruckName,
                        PlateNumber = registered.PlateNumber,
                        AssignedDriver = registered.AssignedDriver,
                        Location = registered.Location,
                        FuelPercent = registered.FuelPercent,
                        ConditionPercent = registered.HealthPercent > 0 ? registered.HealthPercent : 100,
                        OdometerMiles = registered.OdometerMiles,
                        LastServiceUtc = registered.LastServiceDate ?? DateTime.UtcNow,
                        LastInspectionUtc = registered.LastInspectionDate ?? DateTime.UtcNow,
                        DotExpirationUtc = registered.InspectionDueDate ?? DateTime.UtcNow.AddMonths(12)
                    };

                    state.Trucks.Add(truck);
                    changed = true;
                }

                changed |= SetIfNotBlank(v => truck.UnitNumber = v, registered.TruckNumber, truck.UnitNumber);
                changed |= SetIfNotBlank(v => truck.TruckName = v, registered.TruckName, truck.TruckName);
                changed |= SetIfNotBlank(v => truck.PlateNumber = v, registered.PlateNumber, truck.PlateNumber);
                changed |= SetIfNotBlank(v => truck.AssignedDriver = v, registered.AssignedDriver, truck.AssignedDriver);
                changed |= SetIfNotBlank(v => truck.Location = v, Read(row, "Location", "City", "CurrentLocation"), truck.Location);

                changed |= SetDouble(v => truck.FuelPercent = v, Read(row, "FuelPercent", "Fuel", "FuelPct"), truck.FuelPercent);
                changed |= SetDouble(v => truck.ConditionPercent = v, Read(row, "ConditionPercent", "Condition", "HealthPercent", "Health"), truck.ConditionPercent);
                changed |= SetDouble(v => truck.OdometerMiles = v, Read(row, "OdometerMiles", "Odometer", "Mileage", "Miles"), truck.OdometerMiles);
            }

            var registeredIds = registeredFleet
                .Select(t => t.Id)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var registeredNumbers = registeredFleet
                .Select(t => t.TruckNumber)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var before = state.Trucks.Count;

            state.Trucks = state.Trucks
                .Where(t =>
                    registeredIds.Contains(t.TruckId ?? "") ||
                    registeredNumbers.Contains(t.UnitNumber ?? ""))
                .ToList();

            if (state.Trucks.Count != before)
                changed = true;

            if (changed)
                VtcMaintenanceStore.Save(state);
        }

        private static bool Same(string? a, string? b)
        {
            return !string.IsNullOrWhiteSpace(a) &&
                   !string.IsNullOrWhiteSpace(b) &&
                   string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static string Read(object obj, params string[] names)
        {
            foreach (var name in names)
            {
                var prop = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop == null) continue;

                var value = prop.GetValue(obj);
                if (value == null) continue;

                return value.ToString()?.Trim() ?? "";
            }

            return "";
        }

        private static bool SetIfNotBlank(Action<string> setter, string value, string current)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "-" || value == current)
                return false;

            setter(value);
            return true;
        }

        private static bool SetDouble(Action<double> setter, string value, double current)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            value = value.Replace("%", "").Replace("mi", "").Replace(",", "").Trim();

            if (!double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                return false;

            if (Math.Abs(parsed - current) < 0.01)
                return false;

            setter(parsed);
            return true;
        }
    }
}