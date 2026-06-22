namespace OverWatchELD.Models.Fleet
{
    public class FleetCommandRow
    {
        public string TruckId { get; set; } = "";
        public string DriverId { get; set; } = "";
        public string DriverName { get; set; } = "";
        public string CurrentLoadNumber { get; set; } = "";
        public string CurrentLocation { get; set; } = "";
        public string Status { get; set; } = "Offline";

        public double ConditionPercent { get; set; }
        public double FuelPercent { get; set; }
        public double OdometerMiles { get; set; }

        public bool NeedsService { get; set; }

        public string ConditionText => $"{ConditionPercent:0}%";
        public string FuelText => $"{FuelPercent:0}%";
        public string MileageText => $"{OdometerMiles:N0}";
    }
}