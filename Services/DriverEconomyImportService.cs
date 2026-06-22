using OverWatchELD.Models;

namespace OverWatchELD.Services
{
    public sealed class DriverEconomyImportService
    {
        private readonly VehicleAutoSyncService _autoSync = new();

        public VehicleAutoSyncResult Import(DriverEconomySyncData data)
        {
            if (data.IsTruckPurchase || data.IsTruckUpgrade)
            {
                return new VehicleAutoSyncResult();
            }

            return _autoSync.SyncFromTelemetry(
                truckName: data.TruckName,
                model: data.Model,
                modName: data.ModName,
                plateNumber: data.PlateNumber,
                odometerMiles: data.OdometerMiles,
                dutyStatus: data.DutyStatus,
                fuelCost: data.FuelCost,
                tollCost: data.TollCost,
                maintenanceCost: data.MaintenanceCost,
                repairCost: data.RepairCost,
                source: "AutoSync");
        }
    }
}