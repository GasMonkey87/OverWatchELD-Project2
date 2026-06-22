namespace OverWatchELD.Models
{
    public sealed class DriverEconomySyncData
    {
        public string TruckName { get; set; } = "";
        public string Model { get; set; } = "";
        public string ModName { get; set; } = "";
        public string PlateNumber { get; set; } = "";
        public double? OdometerMiles { get; set; }
        public string? DutyStatus { get; set; }

        public decimal? FuelCost { get; set; }
        public decimal? TollCost { get; set; }
        public decimal? MaintenanceCost { get; set; }
        public decimal? RepairCost { get; set; }

        public bool IsTruckPurchase { get; set; }
        public bool IsTruckUpgrade { get; set; }
    }
}