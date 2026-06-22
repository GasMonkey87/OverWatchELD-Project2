using OverWatchELD.Models.Economy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OverWatchELD.Services.Economy
{
    public static class FuelMaintenanceAutomationService
    {
        // Tunables
        public static decimal DieselPricePerGallon { get; set; } = 4.25m;
        public static decimal WearReservePerMile { get; set; } = 0.18m;
        public static decimal RepairCostPerDamagePercent { get; set; } = 95m;

        public static double AssumedTankGallons { get; set; } = 220;
        public static double MinimumMilesDeltaToPostWear { get; set; } = 25;
        public static double MinimumFuelPercentDeltaToPostFuel { get; set; } = 2.0;
        public static double MinimumDamageDeltaToPostRepair { get; set; } = 1.0;

        public static TruckExpenseAutomationResult ProcessTelemetrySnapshot(object? telemetrySnapshot)
        {
            var result = new TruckExpenseAutomationResult();

            if (telemetrySnapshot == null)
            {
                result.Messages.Add("No telemetry snapshot supplied.");
                return result;
            }

            try
            {
                var current = BuildSnapshot(telemetrySnapshot);

                result.TruckKey = current.TruckKey;
                result.TruckNumber = current.TruckNumber;
                result.TruckName = current.TruckName;
                result.DriverName = current.DriverName;

                if (string.IsNullOrWhiteSpace(current.TruckKey))
                {
                    result.Messages.Add("Telemetry snapshot did not contain a usable truck identity.");
                    return result;
                }

                var previous = TruckExpenseAutomationStore.GetSnapshot(current.TruckKey);

                if (previous == null)
                {
                    TruckExpenseAutomationStore.UpsertSnapshot(current);
                    result.Messages.Add("First truck automation snapshot saved. No expenses posted yet.");
                    return result;
                }

                var milesDelta = Delta(current.OdometerMiles, previous.OdometerMiles);
                var fuelDelta = Delta(previous.FuelPercent, current.FuelPercent); // previous - current = consumed
                var damageDelta = CalculateDamageDelta(current, previous);

                result.MilesDelta = milesDelta;
                result.FuelPercentDelta = fuelDelta;
                result.DamagePercentDelta = damageDelta;

                if (milesDelta >= MinimumMilesDeltaToPostWear)
                {
                    var wear = Math.Round((decimal)milesDelta * WearReservePerMile, 2);

                    if (wear > 0)
                    {
                        EconomyStore.AddTransaction(new EconomyTransaction
                        {
                            Type = "AutoWearReserve",
                            Category = "Maintenance",
                            Source = "Fuel/Maintenance Automation",
                            Amount = -wear,
                            DriverName = current.DriverName,
                            TruckNumber = current.TruckNumber,
                            TruckName = current.TruckName,
                            Description = $"Automatic wear reserve for {TruckLabel(current)}",
                            Notes = $"{milesDelta:N0} miles since last snapshot"
                        });

                        result.PostedWearExpense = true;
                        result.WearExpense = wear;
                        result.Messages.Add($"Wear reserve posted: {wear:C}.");
                    }
                }

                if (fuelDelta >= MinimumFuelPercentDeltaToPostFuel)
                {
                    var gallonsUsed = AssumedTankGallons * (fuelDelta / 100.0);
                    var fuelCost = Math.Round((decimal)gallonsUsed * DieselPricePerGallon, 2);

                    if (fuelCost > 0)
                    {
                        EconomyStore.AddTransaction(new EconomyTransaction
                        {
                            Type = "AutoFuelExpense",
                            Category = "Fuel",
                            Source = "Fuel/Maintenance Automation",
                            Amount = -fuelCost,
                            DriverName = current.DriverName,
                            TruckNumber = current.TruckNumber,
                            TruckName = current.TruckName,
                            Description = $"Automatic fuel expense for {TruckLabel(current)}",
                            Notes = $"{fuelDelta:0.##}% fuel used • {gallonsUsed:0.#} gal @ {DieselPricePerGallon:C}/gal"
                        });

                        result.PostedFuelExpense = true;
                        result.FuelExpense = fuelCost;
                        result.Messages.Add($"Fuel expense posted: {fuelCost:C}.");
                    }
                }

                if (damageDelta >= MinimumDamageDeltaToPostRepair)
                {
                    var repair = Math.Round((decimal)damageDelta * RepairCostPerDamagePercent, 2);

                    if (repair > 0)
                    {
                        EconomyStore.AddTransaction(new EconomyTransaction
                        {
                            Type = "AutoRepairReserve",
                            Category = "Maintenance",
                            Source = "Fuel/Maintenance Automation",
                            Amount = -repair,
                            DriverName = current.DriverName,
                            TruckNumber = current.TruckNumber,
                            TruckName = current.TruckName,
                            Description = $"Automatic repair reserve for {TruckLabel(current)}",
                            Notes = $"{damageDelta:0.##}% damage/condition change"
                        });

                        result.PostedRepairExpense = true;
                        result.RepairExpense = repair;
                        result.Messages.Add($"Repair reserve posted: {repair:C}.");
                    }
                }

                TruckExpenseAutomationStore.UpsertSnapshot(current);

                if (result.Messages.Count == 0)
                    result.Messages.Add("Snapshot processed. No expense thresholds were reached.");

                return result;
            }
            catch (Exception ex)
            {
                result.Messages.Add("Automation failed: " + ex.Message);
                return result;
            }
        }

        public static List<TruckExpenseAutomationResult> ProcessTelemetryRows(IEnumerable<object>? telemetryRows)
        {
            var results = new List<TruckExpenseAutomationResult>();

            if (telemetryRows == null)
                return results;

            foreach (var row in telemetryRows)
                results.Add(ProcessTelemetrySnapshot(row));

            return results;
        }

        public static void ProcessCurrentTelemetryIfAvailable()
        {
            try
            {
                // Reflection-based on purpose so this compiles even if TelemetryService shape changes.
                var serviceType = typeof(TelemetryService);
                var candidates = new[]
                {
                    "LastSnapshot",
                    "CurrentSnapshot",
                    "LatestSnapshot",
                    "Snapshot",
                    "CurrentTelemetry",
                    "LastTelemetry"
                };

                foreach (var name in candidates)
                {
                    var prop = serviceType.GetProperty(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (prop == null)
                        continue;

                    object? target = null;

                    if (!prop.GetMethod?.IsStatic ?? false)
                    {
                        var instanceProp = serviceType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
                        target = instanceProp?.GetValue(null);
                    }

                    var value = prop.GetValue(target);
                    if (value != null)
                    {
                        ProcessTelemetrySnapshot(value);
                        return;
                    }
                }
            }
            catch
            {
            }
        }

        public static List<TruckExpenseAutomationSnapshot> LoadSnapshots()
        {
            return TruckExpenseAutomationStore.LoadSnapshots();
        }

        public static TruckExpenseAutomationSnapshot BuildSnapshot(object obj)
        {
            var truckNumber = FirstNonEmpty(
                Read(obj, "TruckNumber"),
                Read(obj, "UnitNumber"),
                Read(obj, "TruckId"),
                Read(obj, "VehicleId"));

            var truckName = FirstNonEmpty(
                Read(obj, "TruckName"),
                Read(obj, "Truck"),
                Read(obj, "VehicleName"),
                Read(obj, "TruckMakeModel"),
                Read(obj, "Model"));

            var driverName = FirstNonEmpty(
                Read(obj, "DriverName"),
                Read(obj, "Driver"),
                Read(obj, "Name"),
                "Unknown Driver");

            var key = BuildTruckKey(truckNumber, truckName);

            var fuel = ReadDouble(obj,
                "FuelPercent",
                "FuelPct",
                "FuelLevelPercent",
                "Fuel",
                "TruckFuelPercent");

            var odometer = ReadDouble(obj,
                "OdometerMiles",
                "Odometer",
                "TruckOdometerMiles",
                "Mileage",
                "Miles");

            var health = ReadDouble(obj,
                "HealthPercent",
                "TruckHealthPercent",
                "ConditionPercent",
                "Condition");

            var damage = ReadDouble(obj,
                "DamagePercent",
                "TruckDamagePercent",
                "Damage",
                "WearPercent");

            if (!damage.HasValue && health.HasValue)
                damage = Math.Max(0, 100.0 - health.Value);

            return new TruckExpenseAutomationSnapshot
            {
                TruckKey = key,
                TruckNumber = truckNumber,
                TruckName = truckName,
                DriverName = driverName,
                FuelPercent = NormalizePercent(fuel),
                OdometerMiles = odometer,
                HealthPercent = NormalizePercent(health),
                DamagePercent = NormalizePercent(damage),
                LastUpdatedUtc = DateTime.UtcNow
            };
        }

        private static double CalculateDamageDelta(
            TruckExpenseAutomationSnapshot current,
            TruckExpenseAutomationSnapshot previous)
        {
            if (current.DamagePercent.HasValue && previous.DamagePercent.HasValue)
                return Math.Max(0, current.DamagePercent.Value - previous.DamagePercent.Value);

            if (current.HealthPercent.HasValue && previous.HealthPercent.HasValue)
                return Math.Max(0, previous.HealthPercent.Value - current.HealthPercent.Value);

            return 0;
        }

        private static double Delta(double? newer, double? older)
        {
            if (!newer.HasValue || !older.HasValue)
                return 0;

            return Math.Max(0, newer.Value - older.Value);
        }

        private static string TruckLabel(TruckExpenseAutomationSnapshot s)
        {
            return FirstNonEmpty(s.TruckNumber, s.TruckName, s.TruckKey);
        }

        private static string BuildTruckKey(string truckNumber, string truckName)
        {
            if (!string.IsNullOrWhiteSpace(truckNumber))
                return "num:" + truckNumber.Trim();

            if (!string.IsNullOrWhiteSpace(truckName))
                return "name:" + truckName.Trim();

            return "";
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
                    .Replace("%", "")
                    .Replace(",", "")
                    .Trim();

                if (double.TryParse(raw, out var value))
                    return value;
            }

            return null;
        }

        private static double? NormalizePercent(double? value)
        {
            if (!value.HasValue)
                return null;

            var v = value.Value;

            if (v <= 1.0 && v >= 0)
                v *= 100.0;

            return Math.Max(0, Math.Min(100, v));
        }

        private static string FirstNonEmpty(params string?[] values)
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
