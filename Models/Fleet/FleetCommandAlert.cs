namespace OverWatchELD.Models.Fleet
{
    public class FleetCommandAlert
    {
        public string TruckId { get; set; } = "";
        public string DriverName { get; set; } = "";
        public string Message { get; set; } = "";
        public string Severity { get; set; } = "Info";
    }
}