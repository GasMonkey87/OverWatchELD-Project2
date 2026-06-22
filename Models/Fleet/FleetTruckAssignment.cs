namespace OverWatchELD.Models.Fleet
{
    public sealed class FleetTruckAssignment
    {
        public string TruckId { get; set; } = "";
        public string DriverId { get; set; } = "";
        public string DiscordUserId { get; set; } = "";
        public string DriverName { get; set; } = "";
        public string AssignedUtc { get; set; } = "";
    }
}
