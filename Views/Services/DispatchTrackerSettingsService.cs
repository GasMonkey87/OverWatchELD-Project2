using System;
using System.IO;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public sealed class DispatchTrackerSettings
    {
        public bool AutoSetInTransitFromTelemetry { get; set; } = true;
        public bool AutoDeliverAtDestination { get; set; } = true;
        public bool DestinationMatchRequiresStop { get; set; } = true;
        public double MinMovingMph { get; set; } = 5.0;
        public double MaxStoppedMph { get; set; } = 2.0;

        public bool SendDiscordPickupMessage { get; set; } = true;
        public bool SendDiscordInTransitMessage { get; set; } = true;
        public bool SendDiscordDeliveryMessage { get; set; } = true;

        public string LoadPickupWebhookUrl { get; set; } = "";
        public string LoadInTransitWebhookUrl { get; set; } = "";
        public string LoadCompletedWebhookUrl { get; set; } = "";
    }

    public static class DispatchTrackerSettingsService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private static string FolderPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OverWatchELD");

        private static string FilePath =>
            Path.Combine(FolderPath, "dispatch_tracker.settings.json");

        public static DispatchTrackerSettings LoadOrCreate()
        {
            try
            {
                Directory.CreateDirectory(FolderPath);

                if (!File.Exists(FilePath))
                {
                    var defaults = new DispatchTrackerSettings();
                    Save(defaults);
                    return defaults;
                }

                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<DispatchTrackerSettings>(json, JsonOptions);
                return loaded ?? new DispatchTrackerSettings();
            }
            catch
            {
                return new DispatchTrackerSettings();
            }
        }

        public static void Save(DispatchTrackerSettings settings)
        {
            try
            {
                Directory.CreateDirectory(FolderPath);
                var json = JsonSerializer.Serialize(settings ?? new DispatchTrackerSettings(), JsonOptions);
                File.WriteAllText(FilePath, json);
            }
            catch
            {
            }
        }
    }
}