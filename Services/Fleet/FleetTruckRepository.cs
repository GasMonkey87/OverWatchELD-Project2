using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OverWatchELD.Models.Fleet;

namespace OverWatchELD.Services.Fleet
{
    public sealed class FleetTruckRepository
    {
        private static readonly JsonSerializerOptions _json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public List<FleetTruck> LoadAll()
        {
            foreach (var path in GetCandidatePaths())
            {
                try
                {
                    if (!File.Exists(path))
                        continue;

                    var json = File.ReadAllText(path);
                    if (string.IsNullOrWhiteSpace(json))
                        continue;

                    var items = JsonSerializer.Deserialize<List<FleetTruck>>(json, _json);
                    if (items != null)
                        return items;
                }
                catch
                {
                    // ignore and continue to next candidate
                }
            }

            return new List<FleetTruck>();
        }

        public void SaveAll(IEnumerable<FleetTruck> trucks)
        {
            var list = trucks?.ToList() ?? new List<FleetTruck>();
            var path = GetPrimaryPath();

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, JsonSerializer.Serialize(list, _json));

            // Website portal fleet sync: keep portal fleet tab updated whenever local fleet trucks are saved.
            try
            {
                FleetPortalSyncService.Shared.QueueSync(list);
            }
            catch
            {
                // Never let website sync break local fleet saving.
            }
        }

        private static string GetPrimaryPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(baseDir, "Data", "Fleet", "fleet_trucks.json");
        }

        private static IEnumerable<string> GetCandidatePaths()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;

            yield return Path.Combine(baseDir, "Data", "Fleet", "fleet_trucks.json");
            yield return Path.Combine(baseDir, "Data", "fleet_trucks.json");
            yield return Path.Combine(baseDir, "fleet_trucks.json");

            yield return Path.Combine(baseDir, "Data", "Fleet", "trucks.json");
            yield return Path.Combine(baseDir, "Data", "trucks.json");
            yield return Path.Combine(baseDir, "trucks.json");

            yield return Path.Combine(baseDir, "Data", "Fleet", "fleet.json");
            yield return Path.Combine(baseDir, "Data", "fleet.json");
            yield return Path.Combine(baseDir, "fleet.json");
        }
    }
}
