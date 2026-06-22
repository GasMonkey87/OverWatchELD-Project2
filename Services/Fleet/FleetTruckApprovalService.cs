using OverWatchELD.Models.Fleet;
using OverWatchELD.Services;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OverWatchELD.Services.Fleet
{
    public static class FleetTruckApprovalService
    {
        public static PendingFleetTruckApproval BuildPendingFromTelemetry(object? telemetrySnapshot = null)
        {
            telemetrySnapshot ??= TryGetCurrentTelemetrySnapshot();

            var pending = new PendingFleetTruckApproval();

            if (telemetrySnapshot == null)
            {
                pending.TruckName = "Telemetry Truck";
                pending.AssignedDriver = "Unknown Driver";
                pending.HealthPercent = 100;
                pending.Notes = "No live telemetry snapshot was available. Fill truck details manually before submitting.";
                return pending;
            }

            pending.TruckNumber = FirstNonEmpty(Read(telemetrySnapshot, "TruckNumber"), Read(telemetrySnapshot, "UnitNumber"), Read(telemetrySnapshot, "TruckId"));
            pending.TruckName = FirstNonEmpty(Read(telemetrySnapshot, "TruckName"), Read(telemetrySnapshot, "Truck"), Read(telemetrySnapshot, "VehicleName"), Read(telemetrySnapshot, "Model"), "Telemetry Truck");
            pending.MakeModel = FirstNonEmpty(Read(telemetrySnapshot, "MakeModel"), Read(telemetrySnapshot, "TruckMakeModel"), Read(telemetrySnapshot, "Model"));
            pending.PlateNumber = FirstNonEmpty(Read(telemetrySnapshot, "PlateNumber"), Read(telemetrySnapshot, "Plate"));
            pending.AssignedDriver = FirstNonEmpty(Read(telemetrySnapshot, "DriverName"), Read(telemetrySnapshot, "Driver"), Read(telemetrySnapshot, "Name"), "Unknown Driver");
            pending.DriverDiscordId = FirstNonEmpty(Read(telemetrySnapshot, "DriverDiscordId"), Read(telemetrySnapshot, "DiscordUserId"), Read(telemetrySnapshot, "UserId"));
            pending.CurrentLocation = FirstNonEmpty(Read(telemetrySnapshot, "Location"), Read(telemetrySnapshot, "LocationText"), Read(telemetrySnapshot, "City"));
            pending.OdometerMiles = ReadDouble(telemetrySnapshot, "OdometerMiles", "Odometer", "TruckOdometerMiles", "Mileage", "Miles") ?? 0;
            pending.FuelPercent = NormalizePercent(ReadDouble(telemetrySnapshot, "FuelPercent", "FuelPct", "FuelLevelPercent", "Fuel", "TruckFuelPercent")) ?? 0;
            pending.HealthPercent = NormalizePercent(ReadDouble(telemetrySnapshot, "HealthPercent", "TruckHealthPercent", "ConditionPercent", "Condition")) ?? 100;

            var damage = NormalizePercent(ReadDouble(telemetrySnapshot, "DamagePercent", "TruckDamagePercent", "Damage", "WearPercent"));
            pending.DamagePercent = damage ?? Math.Max(0, 100 - pending.HealthPercent);
            pending.Notes = "Imported from live telemetry. Review/fix details before approval.";

            return pending;
        }

        public static List<FleetAnalyticsTruckRow> BuildFleetTruckRows(string? search = null)
        {
            var rows = new List<FleetAnalyticsTruckRow>();

            try
            {
                var store = new FleetCommandStore();
                var trucks = store.LoadAll();

                foreach (var t in trucks)
                {
                    var status = FirstNonEmpty(Get(t, "Status"), "Registered");
                    var truckNumber = FirstNonEmpty(Get(t, "TruckNumber"), Get(t, "UnitNumber"), Get(t, "Id"));

                    rows.Add(new FleetAnalyticsTruckRow
                    {
                        TruckNumber = FleetTruckNumberLockStore.Normalize(truckNumber),
                        TruckName = FirstNonEmpty(Get(t, "TruckName"), Get(t, "Name"), Get(t, "Model")),
                        MakeModel = FirstNonEmpty(Get(t, "MakeModel"), Get(t, "Model"), Get(t, "Make")),
                        PlateNumber = FirstNonEmpty(Get(t, "PlateNumber"), Get(t, "Plate")),
                        AssignedDriver = FirstNonEmpty(Get(t, "AssignedDriver"), Get(t, "DriverName")),
                        Status = status,
                        ApprovalBadge = "Approved",
                        OdometerMiles = GetDouble(t, "OdometerMiles", "Mileage") ?? 0,
                        FuelPercent = GetDouble(t, "FuelPercent", "Fuel") ?? 0,
                        HealthPercent = GetDouble(t, "HealthPercent", "ConditionPercent") ?? 0,
                        DamagePercent = GetDouble(t, "DamagePercent") ?? 0,
                        CurrentLocation = FirstNonEmpty(Get(t, "CurrentLocation"), Get(t, "Location")),
                        IsActive = IsActiveStatus(status)
                    });
                }
            }
            catch
            {
            }

            foreach (var pending in PendingFleetTruckApprovalStore.Load()
                         .Where(x => string.Equals(x.Status, "Pending", StringComparison.OrdinalIgnoreCase)))
            {
                rows.Add(new FleetAnalyticsTruckRow
                {
                    TruckNumber = FleetTruckNumberLockStore.Normalize(pending.TruckNumber),
                    TruckName = pending.TruckName,
                    MakeModel = pending.MakeModel,
                    PlateNumber = pending.PlateNumber,
                    AssignedDriver = pending.AssignedDriver,
                    Status = "Pending Approval",
                    ApprovalBadge = "Pending",
                    OdometerMiles = pending.OdometerMiles,
                    FuelPercent = pending.FuelPercent,
                    HealthPercent = pending.HealthPercent,
                    DamagePercent = pending.DamagePercent,
                    CurrentLocation = pending.CurrentLocation,
                    IsActive = false
                });
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var q = search.Trim();

                rows = rows.Where(x =>
                    Contains(x.TruckNumber, q) ||
                    Contains(x.TruckName, q) ||
                    Contains(x.MakeModel, q) ||
                    Contains(x.PlateNumber, q) ||
                    Contains(x.AssignedDriver, q) ||
                    Contains(x.Status, q) ||
                    Contains(x.ApprovalBadge, q))
                    .ToList();
            }

            return rows
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.ApprovalBadge == "Pending" ? 0 : 1)
                .ThenBy(x => x.TruckNumber)
                .ThenBy(x => x.TruckName)
                .ToList();
        }

        public static void Approve(string pendingId, string managerAssignedTruckNumber, string reviewer = "Management")
        {
            var assignedNumber = FleetTruckNumberLockStore.Normalize(managerAssignedTruckNumber);

            if (string.IsNullOrWhiteSpace(assignedNumber))
                throw new InvalidOperationException("Manager must assign a truck number before approval.");

            if (FleetTruckNumberLockStore.IsLocked(assignedNumber, pendingId))
                throw new InvalidOperationException($"Truck number {assignedNumber} is already assigned and cannot be reused.");

            if (TruckNumberExistsInFleet(assignedNumber, pendingId))
                throw new InvalidOperationException($"Truck number {assignedNumber} already exists in the fleet and cannot be reused.");

            var rows = PendingFleetTruckApprovalStore.Load();
            var pending = rows.FirstOrDefault(x => string.Equals(x.Id, pendingId, StringComparison.OrdinalIgnoreCase));

            if (pending == null)
                throw new InvalidOperationException("Pending truck approval was not found.");

            pending.TruckNumber = assignedNumber;
            pending.Status = "Approved";
            pending.ReviewedUtc = DateTime.UtcNow;
            pending.ReviewedBy = reviewer;

            FleetTruckNumberLockStore.LockNumber(
                assignedNumber,
                pending.Id,
                pending.TruckName,
                pending.AssignedDriver,
                reviewer,
                "Approved fleet truck number. This number is locked and cannot be reused.");

            AddApprovedTruckToFleet(pending);

            DriverProfileMasterStore.LinkTruck(
                pending.DriverDiscordId,
                pending.AssignedDriver,
                pending.AssignedDriver,
                pending.TruckNumber,
                pending.TruckName,
                pending.PlateNumber,
                "",
                "Truck Approval",
                current: true);

            PendingFleetTruckApprovalStore.Save(rows);
        }

        public static void Deny(string pendingId, string reviewer = "Management")
        {
            var rows = PendingFleetTruckApprovalStore.Load();
            var pending = rows.FirstOrDefault(x => string.Equals(x.Id, pendingId, StringComparison.OrdinalIgnoreCase));

            if (pending == null)
                return;

            pending.Status = "Denied";
            pending.ReviewedUtc = DateTime.UtcNow;
            pending.ReviewedBy = reviewer;
            PendingFleetTruckApprovalStore.Save(rows);
        }

        private static bool TruckNumberExistsInFleet(string truckNumber, string pendingId)
        {
            try
            {
                var number = FleetTruckNumberLockStore.Normalize(truckNumber);
                var store = new FleetCommandStore();
                var trucks = store.LoadAll();

                foreach (var t in trucks)
                {
                    var existing = FleetTruckNumberLockStore.Normalize(
                        FirstNonEmpty(Get(t, "TruckNumber"), Get(t, "UnitNumber"), Get(t, "Id")));

                    if (string.Equals(existing, number, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static void AddApprovedTruckToFleet(PendingFleetTruckApproval pending)
        {
            try
            {
                var store = new FleetCommandStore();
                var assignedNumber = FleetTruckNumberLockStore.Normalize(pending.TruckNumber);

                if (string.IsNullOrWhiteSpace(assignedNumber))
                    return;

                var existing = store.GetByTruckNumber(assignedNumber);

                if (existing == null)
                {
                    existing = store.FindByIdentity(
                        pending.PlateNumber,
                        pending.TruckName,
                        pending.MakeModel,
                        pending.AssignedDriver);
                }

                existing ??= new OverWatchELD.Models.Fleet.FleetCommandTruck
                {
                    Id = string.IsNullOrWhiteSpace(pending.Id)
                        ? Guid.NewGuid().ToString("N")
                        : pending.Id
                };

                // Locked/persistent truck number - always write this same value.
                existing.TruckNumber = assignedNumber;
                existing.TruckName = FirstNonEmpty(pending.TruckName, assignedNumber);
                existing.Model = pending.MakeModel ?? "";
                existing.PlateNumber = pending.PlateNumber ?? "";
                existing.AssignedDriver = pending.AssignedDriver ?? "";
                existing.DriverDiscordId = pending.DriverDiscordId ?? "";
                existing.Location = pending.CurrentLocation ?? "";
                existing.OdometerMiles = Math.Max(0, pending.OdometerMiles);
                existing.FuelPercent = Math.Max(0, Math.Min(100, pending.FuelPercent));
                existing.HealthPercent = (int)Math.Round(Math.Max(0, Math.Min(100, pending.HealthPercent)));
                existing.IsActive = true;
                existing.IsOnline = true;
                existing.Status = "Active";
                existing.UpdatedUtc = DateTimeOffset.UtcNow;

                store.Save(existing);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Truck was approved, but failed to save into the main ELD fleet list: " + ex.Message, ex);
            }
        }

        private static object? TryGetCurrentTelemetrySnapshot()
        {
            try
            {
                var serviceType = typeof(TelemetryService);
                var candidates = new[] { "LastSnapshot", "CurrentSnapshot", "LatestSnapshot", "Snapshot", "CurrentTelemetry", "LastTelemetry" };

                foreach (var name in candidates)
                {
                    var prop = serviceType.GetProperty(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.IgnoreCase);

                    if (prop == null)
                        continue;

                    object? target = null;

                    if (!(prop.GetMethod?.IsStatic ?? false))
                    {
                        var instanceProp = serviceType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
                        target = instanceProp?.GetValue(null);
                    }

                    var value = prop.GetValue(target);

                    if (value != null)
                        return value;
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool IsActiveStatus(string? status)
        {
            var s = (status ?? "").Trim().ToLowerInvariant();
            return s.Contains("active") || s.Contains("online") || s.Contains("driving") || s.Contains("on duty") || s.Contains("available");
        }

        private static bool Contains(string? text, string q) =>
            (text ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

        private static string Get(object obj, string name)
        {
            try
            {
                return obj.GetType()
                    .GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                    ?.GetValue(obj)
                    ?.ToString()
                    ?.Trim() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static string Read(object obj, string name) => Get(obj, name);

        private static double? GetDouble(object obj, params string[] names)
        {
            foreach (var name in names)
            {
                var raw = Get(obj, name).Replace("%", "").Replace(",", "").Trim();

                if (double.TryParse(raw, out var value))
                    return NormalizePercentIfNeeded(name, value);
            }

            return null;
        }

        private static double? ReadDouble(object obj, params string[] names)
        {
            foreach (var name in names)
            {
                var raw = Read(obj, name).Replace("%", "").Replace(",", "").Trim();

                if (double.TryParse(raw, out var value))
                    return value;
            }

            return null;
        }

        private static void Set(object obj, string name, object? value)
        {
            try
            {
                var prop = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                if (prop == null || !prop.CanWrite)
                    return;

                var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

                if (targetType == typeof(string))
                    prop.SetValue(obj, value?.ToString() ?? "");
                else if (targetType == typeof(double))
                    prop.SetValue(obj, Convert.ToDouble(value));
                else if (targetType == typeof(decimal))
                    prop.SetValue(obj, Convert.ToDecimal(value));
                else if (targetType == typeof(int))
                    prop.SetValue(obj, Convert.ToInt32(value));
                else if (targetType == typeof(bool))
                    prop.SetValue(obj, Convert.ToBoolean(value));
                else if (targetType == typeof(DateTime))
                    prop.SetValue(obj, value is DateTime dt ? dt : Convert.ToDateTime(value));
                else
                    prop.SetValue(obj, value);
            }
            catch
            {
            }
        }

        private static double? NormalizePercent(double? value)
        {
            if (!value.HasValue)
                return null;

            var v = value.Value;

            if (v >= 0 && v <= 1)
                v *= 100;

            return Math.Max(0, Math.Min(100, v));
        }

        private static double NormalizePercentIfNeeded(string name, double value)
        {
            if (name.Contains("Percent", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Fuel", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Health", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Damage", StringComparison.OrdinalIgnoreCase))
            {
                if (value >= 0 && value <= 1)
                    value *= 100;

                return Math.Max(0, Math.Min(100, value));
            }

            return value;
        }

        private static bool Same(string? a, string? b) =>
            !string.IsNullOrWhiteSpace(a) &&
            !string.IsNullOrWhiteSpace(b) &&
            string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

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
