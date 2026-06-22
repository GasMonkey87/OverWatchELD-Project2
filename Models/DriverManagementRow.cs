using System;

namespace OverWatchELD.Models
{
    public class DriverManagementRow
    {
        public string DriverName { get; set; } = "";
        public string DiscordUsername { get; set; } = "";
        public string DiscordUserId { get; set; } = "";
        public string TruckName { get; set; } = "";
        public string Model { get; set; } = "";
        public string ModName { get; set; } = "";
        public string PlateNumber { get; set; } = "";
        public bool IsActive { get; set; }
    }
}
