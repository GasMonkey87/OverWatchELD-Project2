namespace OverWatchELD.Models
{
    public sealed class VehicleAutoSyncResult
    {
        public FleetTruck? Truck { get; set; }
        public bool WasCreated { get; set; }
        public bool WasUpdated { get; set; }
        public bool FinanceAdded { get; set; }
        public bool MaintenanceAdded { get; set; }
    }
}