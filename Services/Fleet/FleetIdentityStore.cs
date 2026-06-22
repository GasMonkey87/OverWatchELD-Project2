using System;
using System.IO;
using System.Text.Json;

namespace OverWatchELD.Services.Fleet
{
    public sealed class FleetIdentityStore
    {
        private sealed class FleetIdentity
        {
            public string ActivePlate { get; set; } = "";
        }

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        private static string PathOnDisk
        {
            get
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OverWatchELD");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "fleet_identity.json");
            }
        }

        public string LoadActivePlate()
        {
            try
            {
                if (!File.Exists(PathOnDisk)) return "";
                var json = File.ReadAllText(PathOnDisk);
                var obj = JsonSerializer.Deserialize<FleetIdentity>(json, JsonOpts);
                return (obj?.ActivePlate ?? "").Trim();
            }
            catch { return ""; }
        }

        public void SaveActivePlate(string plate)
        {
            try
            {
                var obj = new FleetIdentity { ActivePlate = (plate ?? "").Trim() };
                File.WriteAllText(PathOnDisk, JsonSerializer.Serialize(obj, JsonOpts));
            }
            catch { }
        }
    }
}