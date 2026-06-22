using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OverWatchELD.Models.Fleet;

namespace OverWatchELD.Services.Fleet
{
    public sealed class FleetTruckAssignmentRepository
    {
        private static readonly JsonSerializerOptions _json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public List<FleetTruckAssignment> LoadAll()
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

                    var items = JsonSerializer.Deserialize<List<FleetTruckAssignment>>(json, _json);
                    if (items != null)
                        return items;
                }
                catch
                {
                    // ignore and continue to next candidate
                }
            }

            return new List<FleetTruckAssignment>();
        }

        public void SaveAll(IEnumerable<FleetTruckAssignment> assignments)
        {
            var list = assignments?.ToList() ?? new List<FleetTruckAssignment>();
            var path = GetPrimaryPath();

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, JsonSerializer.Serialize(list, _json));
        }

        private static string GetPrimaryPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(baseDir, "Data", "Fleet", "fleet_assignments.json");
        }

        private static IEnumerable<string> GetCandidatePaths()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;

            yield return Path.Combine(baseDir, "Data", "Fleet", "fleet_assignments.json");
            yield return Path.Combine(baseDir, "Data", "fleet_assignments.json");
            yield return Path.Combine(baseDir, "fleet_assignments.json");

            yield return Path.Combine(baseDir, "Data", "Fleet", "assignments.json");
            yield return Path.Combine(baseDir, "Data", "assignments.json");
            yield return Path.Combine(baseDir, "assignments.json");

            yield return Path.Combine(baseDir, "Data", "Fleet", "truck_assignments.json");
            yield return Path.Combine(baseDir, "Data", "truck_assignments.json");
            yield return Path.Combine(baseDir, "truck_assignments.json");
        }
    }
}
