using OverWatchELD.Models;
using System;

namespace OverWatchELD.Services
{
    public static class DispatchLaunchContextService
    {
        public class DispatchTruckContext
        {
            public string Recipient { get; set; } = "";
            public string DiscordUserId { get; set; } = "";
            public string DiscordUsername { get; set; } = "";
            public string DriverName { get; set; } = "";

            public string TruckId { get; set; } = "";
            public string TruckName { get; set; } = "";
            public string Model { get; set; } = "";
            public string ModName { get; set; } = "";
            public string PlateNumber { get; set; } = "";

            public bool IsActive { get; set; }

            public double? OdometerMiles { get; set; }

            public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

            public string LastSeenText
            {
                get
                {
                    var span = DateTime.UtcNow - LastSeenUtc;

                    if (span.TotalSeconds < 60) return "Just now";
                    if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
                    if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";

                    return LastSeenUtc.ToLocalTime().ToString("g");
                }
            }

            public string TruckDisplay =>
                string.IsNullOrWhiteSpace(PlateNumber)
                    ? TruckName
                    : $"{TruckName} • {PlateNumber}";

            public string StatusChipText =>
                IsActive ? "ACTIVE TRUCK" : "SAVED TRUCK";
        }

        public static DispatchTruckContext? PendingTruckContext { get; set; }

        public static void LoadFromFleetTruck(FleetTruck truck)
        {
            if (truck == null) return;

            var recipient =
                !string.IsNullOrWhiteSpace(truck.DiscordUsername)
                ? truck.DiscordUsername
                : truck.DriverName;

            PendingTruckContext = new DispatchTruckContext
            {
                Recipient = recipient,
                DiscordUserId = truck.DiscordUserId,
                DiscordUsername = truck.DiscordUsername,
                DriverName = truck.DriverName,

                TruckId = truck.Id,
                TruckName = truck.TruckName,
                Model = truck.Model,
                ModName = truck.ModName,
                PlateNumber = truck.PlateNumber,

                IsActive = truck.IsActive,
                OdometerMiles = truck.OdometerMiles,
                LastSeenUtc = truck.LastSeenUtc
            };
        }

        public static void Clear()
        {
            PendingTruckContext = null;
        }
    }
}