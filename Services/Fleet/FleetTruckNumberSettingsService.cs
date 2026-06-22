using System;
using System.IO;
using System.Text.Json;

namespace OverWatchELD.Services.Fleet
{
    public sealed class FleetTruckNumberSettings
    {
        public int StartingTruckNumber { get; set; } = 100;
    }

    public static class FleetTruckNumberSettingsService
    {
        private static readonly string FilePath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "OverWatchELD",
                "fleet_truck_number_settings.json");

        public static FleetTruckNumberSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new FleetTruckNumberSettings();

                return JsonSerializer.Deserialize<FleetTruckNumberSettings>(
                    File.ReadAllText(FilePath)) ?? new FleetTruckNumberSettings();
            }
            catch
            {
                return new FleetTruckNumberSettings();
            }
        }

        public static void Save(FleetTruckNumberSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

            File.WriteAllText(
                FilePath,
                JsonSerializer.Serialize(settings, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
        }

        public static int GetStartingNumber() =>
            Math.Max(1, Load().StartingTruckNumber);

        public static void SetStartingNumber(int number)
        {
            Save(new FleetTruckNumberSettings
            {
                StartingTruckNumber = Math.Max(1, number)
            });
        }
    }
}