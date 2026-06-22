using System;

namespace OverWatchELD.Services.Fleet
{
    public static class FleetAlertHub
    {
        // Toast-like alerts for the app shell (MainWindow can subscribe)
        public sealed class FleetAlert
        {
            public DateTimeOffset Utc { get; init; } = DateTimeOffset.UtcNow;
            public string Plate { get; init; } = "";
            public string Message { get; init; } = "";
        }

        public static event Action<FleetAlert>? OnAlert;

        public static void Raise(string plate, string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message)) return;
                OnAlert?.Invoke(new FleetAlert
                {
                    Plate = (plate ?? "").Trim(),
                    Message = message.Trim()
                });
            }
            catch { }
        }
    }
}