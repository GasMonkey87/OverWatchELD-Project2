using OverWatchELD.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OverWatchELD.Services
{
    public sealed class VehicleAutoSyncService
    {
        private readonly FleetService _fleetService = new();
        private readonly FinanceService _financeService = new();
        private readonly DiscordIdentityService _identityService = new();

        public VehicleAutoSyncResult SyncFromTelemetry(
            string truckName,
            string model,
            string modName,
            string plateNumber,
            double? odometerMiles,
            string? dutyStatus,
            decimal? fuelCost = null,
            decimal? tollCost = null,
            decimal? maintenanceCost = null,
            decimal? repairCost = null,
            string? source = "Telemetry")
        {
            var result = new VehicleAutoSyncResult();
            var identity = _identityService.LoadOrDefault();

            var driverId = (identity.DiscordUserId ?? "").Trim();
            var driverName = (identity.DiscordUsername ?? "").Trim();

            var items = _fleetService.LoadAll();

            foreach (var t in items.Where(x =>
                         string.Equals(x.DiscordUserId, driverId, StringComparison.OrdinalIgnoreCase)))
            {
                t.IsActive = false;
            }

            var match = FindExistingTruck(items, driverId, truckName, model, modName, plateNumber);

            if (match == null)
            {
                match = new FleetTruck
                {
                    TruckName = Safe(truckName, "Current Truck"),
                    Model = Safe(model, "Unknown ATS Model"),
                    ModName = modName?.Trim() ?? "",
                    PlateNumber = plateNumber?.Trim() ?? "",
                    DiscordUserId = driverId,
                    DiscordUsername = driverName,
                    DriverName = driverName,
                    LastSeenUtc = DateTime.UtcNow,
                    IsActive = true,
                    OdometerMiles = odometerMiles,
                    LastKnownOdometerMiles = odometerMiles,
                    LastKnownDutyStatus = dutyStatus
                };

                items.Add(match);
                result.WasCreated = true;
            }
            else
            {
                match.TruckName = Safe(truckName, match.TruckName);
                match.Model = Safe(model, match.Model);
                match.ModName = modName?.Trim() ?? match.ModName;
                match.PlateNumber = !string.IsNullOrWhiteSpace(plateNumber) ? plateNumber.Trim() : match.PlateNumber;
                match.DiscordUserId = driverId;
                match.DiscordUsername = driverName;
                match.DriverName = driverName;
                match.LastSeenUtc = DateTime.UtcNow;
                match.IsActive = true;
                match.OdometerMiles = odometerMiles ?? match.OdometerMiles;
                match.LastKnownOdometerMiles = odometerMiles ?? match.LastKnownOdometerMiles;
                match.LastKnownDutyStatus = dutyStatus ?? match.LastKnownDutyStatus;

                result.WasUpdated = true;
            }

            AddFinanceIfNeeded(match, fuelCost, "Expense", "Fuel", source, ref result);
            AddFinanceIfNeeded(match, tollCost, "Expense", "Tolls", source, ref result);
            AddFinanceIfNeeded(match, maintenanceCost, "Expense", "Maintenance", source, ref result);
            AddFinanceIfNeeded(match, repairCost, "Expense", "Repairs", source, ref result);

            if (fuelCost.HasValue && fuelCost.Value > 0)
            {
                match.TotalFuelCost += (double)fuelCost.Value;
                match.LastFuelUtc = DateTimeOffset.UtcNow;
            }

            if (tollCost.HasValue && tollCost.Value > 0)
            {
                match.TotalTollCost += (double)tollCost.Value;
                match.LastTollUtc = DateTimeOffset.UtcNow;
            }

            if (maintenanceCost.HasValue && maintenanceCost.Value > 0)
            {
                match.TotalMaintenanceCost += (double)maintenanceCost.Value;
                match.LastMaintenanceUtc = DateTimeOffset.UtcNow;
                result.MaintenanceAdded = true;
            }

            if (repairCost.HasValue && repairCost.Value > 0)
            {
                match.TotalRepairCost += (double)repairCost.Value;
                match.LastRepairUtc = DateTimeOffset.UtcNow;
                result.MaintenanceAdded = true;
            }

            _fleetService.SaveAll(items);

            result.Truck = match;
            return result;
        }

        private FleetTruck? FindExistingTruck(
            List<FleetTruck> items,
            string driverId,
            string truckName,
            string model,
            string modName,
            string plateNumber)
        {
            var plate = (plateNumber ?? "").Trim();
            var tName = (truckName ?? "").Trim();
            var mdl = (model ?? "").Trim();
            var mod = (modName ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(plate))
            {
                var byPlate = items.FirstOrDefault(x =>
                    string.Equals((x.PlateNumber ?? "").Trim(), plate, StringComparison.OrdinalIgnoreCase));
                if (byPlate != null) return byPlate;
            }

            var bySignature = items.FirstOrDefault(x =>
                string.Equals((x.TruckName ?? "").Trim(), tName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals((x.Model ?? "").Trim(), mdl, StringComparison.OrdinalIgnoreCase) &&
                string.Equals((x.ModName ?? "").Trim(), mod, StringComparison.OrdinalIgnoreCase));

            if (bySignature != null) return bySignature;

            var byDriver = items.FirstOrDefault(x =>
                string.Equals((x.DiscordUserId ?? "").Trim(), driverId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals((x.Model ?? "").Trim(), mdl, StringComparison.OrdinalIgnoreCase) &&
                string.Equals((x.TruckName ?? "").Trim(), tName, StringComparison.OrdinalIgnoreCase));

            return byDriver;
        }

        private void AddFinanceIfNeeded(
            FleetTruck truck,
            decimal? amount,
            string entryType,
            string category,
            string? source,
            ref VehicleAutoSyncResult result)
        {
            if (!amount.HasValue || amount.Value <= 0)
                return;

            var identity = _identityService.LoadOrDefault();

            _financeService.Add(new FinanceEntry
            {
                DateUtc = DateTime.UtcNow,
                EntryType = entryType,
                Category = category,
                Amount = Math.Abs(amount.Value),
                TruckName = truck.TruckName,
                DiscordUserId = identity.DiscordUserId,
                DiscordUsername = identity.DiscordUsername,
                Description = $"{category} auto-imported from telemetry/economy sync",
                EnteredBy = "AutoSync",
                Source = source ?? "Telemetry"
            });

            result.FinanceAdded = true;
        }

        private static string Safe(string? value, string fallback)
        {
            var s = (value ?? "").Trim();
            return string.IsNullOrWhiteSpace(s) ? fallback : s;
        }
    }
}