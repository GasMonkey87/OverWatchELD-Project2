namespace OverWatchELD.Models.Fleet
{
    public sealed class FleetAnalyticsTruckRow
    {
        public string TruckNumber { get; set; } = "";
        public string TruckName { get; set; } = "";
        public string MakeModel { get; set; } = "";
        public string PlateNumber { get; set; } = "";
        public string AssignedDriver { get; set; } = "";
        public string Status { get; set; } = "";
        public string ApprovalBadge { get; set; } = "Approved";
        public double OdometerMiles { get; set; }
        public double FuelPercent { get; set; }
        public double HealthPercent { get; set; }
        public double DamagePercent { get; set; }
        public bool IsActive { get; set; }
        public string ActiveLight => IsActive ? "🟢 Active" : "🔴 Inactive";
        public string CurrentLocation { get; set; } = "";
    }
}
