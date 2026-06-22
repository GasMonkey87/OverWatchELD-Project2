using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using OverWatchELD.Models.Fleet;

namespace OverWatchELD.Services.Fleet
{
    public sealed class FleetStore
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public string StorePath { get; }

        public FleetStore()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OverWatchELD");

            Directory.CreateDirectory(dir);
            StorePath = Path.Combine(dir, "fleet_store.json");
        }

        public Dictionary<string, FleetTruck> Load()
        {
            try
            {
                if (!File.Exists(StorePath))
                    return new Dictionary<string, FleetTruck>(StringComparer.OrdinalIgnoreCase);

                var json = File.ReadAllText(StorePath);
                var data = JsonSerializer.Deserialize<Dictionary<string, FleetTruck>>(json, JsonOpts)
                           ?? new Dictionary<string, FleetTruck>();

                return new Dictionary<string, FleetTruck>(data, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, FleetTruck>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public void Save(Dictionary<string, FleetTruck> trucks)
        {
            try
            {
                var json = JsonSerializer.Serialize(trucks, JsonOpts);
                File.WriteAllText(StorePath, json);
            }
            catch
            {
                // never crash
            }
        }
    }
}