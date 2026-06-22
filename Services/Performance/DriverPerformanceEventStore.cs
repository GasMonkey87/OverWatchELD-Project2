using OverWatchELD.Models.Performance;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services.Performance
{
    public static class DriverPerformanceEventStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private static string Folder
        {
            get
            {
                var folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "OverWatchELD",
                    "Performance");

                Directory.CreateDirectory(folder);
                return folder;
            }
        }

        private static string FilePath => Path.Combine(Folder, "driver_performance_events.json");

        public static List<DriverPerformanceEvent> Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new List<DriverPerformanceEvent>();

                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<List<DriverPerformanceEvent>>(json, JsonOptions)
                       ?? new List<DriverPerformanceEvent>();
            }
            catch
            {
                return new List<DriverPerformanceEvent>();
            }
        }

        public static void Save(List<DriverPerformanceEvent> rows)
        {
            try
            {
                rows = rows
                    .Where(x => x != null)
                    .OrderByDescending(x => x.CreatedUtc)
                    .Take(25000)
                    .ToList();

                File.WriteAllText(FilePath, JsonSerializer.Serialize(rows, JsonOptions));
            }
            catch
            {
            }
        }

        public static void Add(DriverPerformanceEvent item)
        {
            var rows = Load();
            rows.Insert(0, item);
            Save(rows);
        }

        public static bool HasRecentEvent(string driverName, string eventType, TimeSpan window)
        {
            var cutoff = DateTime.UtcNow.Subtract(window);

            return Load().Any(x =>
                x.CreatedUtc >= cutoff &&
                string.Equals(x.DriverName, driverName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.EventType, eventType, StringComparison.OrdinalIgnoreCase));
        }
    }
}
