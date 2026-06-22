using System;

namespace OverWatchELD.Services
{
    public sealed class MaintenanceMalfunctionAlert
    {
        public string UnitNumber { get; set; } = "";
        public string TruckName { get; set; } = "";
        public string Issue { get; set; } = "";
        public string Severity { get; set; } = "";
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }

    public static class MaintenanceMalfunctionAlertService
    {
        public static event Action<MaintenanceMalfunctionAlert>? MalfunctionRaised;

        public static void Raise(string unitNumber, string truckName, string issue, string severity)
        {
            MalfunctionRaised?.Invoke(new MaintenanceMalfunctionAlert
            {
                UnitNumber = unitNumber ?? "",
                TruckName = truckName ?? "",
                Issue = issue ?? "",
                Severity = severity ?? "",
                CreatedUtc = DateTime.UtcNow
            });
        }
    }
}