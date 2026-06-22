using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public static class DriverHistoryStore
    {
        private static readonly object _gate = new();
        private static readonly string Root =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "DriverHistory");

        public static void AddEvent(DriverHistoryEntry entry)
        {
            try
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.DriverId))
                    return;

                lock (_gate)
                {
                    Directory.CreateDirectory(Root);

                    var file = Path.Combine(Root, $"{Sanitize(entry.DriverId)}.json");
                    List<DriverHistoryEntry> list;

                    if (File.Exists(file))
                    {
                        list = JsonSerializer.Deserialize<List<DriverHistoryEntry>>(File.ReadAllText(file))
                               ?? new List<DriverHistoryEntry>();
                    }
                    else
                    {
                        list = new List<DriverHistoryEntry>();
                    }

                    list.Insert(0, entry);

                    if (list.Count > 500)
                        list = list.Take(500).ToList();

                    File.WriteAllText(file, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
                }
            }
            catch
            {
            }
        }

        public static List<DriverHistoryEntry> GetRecent(string driverId, int take = 25)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(driverId))
                    return new List<DriverHistoryEntry>();

                lock (_gate)
                {
                    var file = Path.Combine(Root, $"{Sanitize(driverId)}.json");
                    if (!File.Exists(file))
                        return new List<DriverHistoryEntry>();

                    var list = JsonSerializer.Deserialize<List<DriverHistoryEntry>>(File.ReadAllText(file))
                               ?? new List<DriverHistoryEntry>();

                    return list
                        .OrderByDescending(x => x.Utc)
                        .Take(Math.Max(1, take))
                        .ToList();
                }
            }
            catch
            {
                return new List<DriverHistoryEntry>();
            }
        }

        private static string Sanitize(string value)
        {
            value ??= "default";
            foreach (var c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');

            value = value.Trim();
            return string.IsNullOrWhiteSpace(value) ? "default" : value;
        }
    }

    public sealed class DriverHistoryEntry
    {
        public DateTime Utc { get; set; }
        public string DriverId { get; set; } = "";
        public string DriverName { get; set; } = "";
        public string EventType { get; set; } = "";
        public string Truck { get; set; } = "";
        public string LoadNumber { get; set; } = "";
        public string Cargo { get; set; } = "";
        public double Miles { get; set; }
        public double CargoWeightLbs { get; set; }
        public string Location { get; set; } = "";
        public string Notes { get; set; } = "";

        public string DisplayLine
        {
            get
            {
                var when = Utc == default ? "" : Utc.ToLocalTime().ToString("g");
                var location = string.IsNullOrWhiteSpace(Location) ? "" : $" • {Location.Trim()}";

                return EventType switch
                {
                    "Mileage" => $"{when} • Mileage • +{Miles:N1} mi{location}",
                    "TruckChanged" => $"{when} • Truck Changed • {Truck}{location}",
                    "LoadPickedUp" => $"{when} • Load Picked Up • {CargoWeightLbs:N0} lbs{location}",
                    "LoadDelivered" => $"{when} • Load Delivered{location}",
                    "LocationChanged" => $"{when} • Location • {Location}",
                    _ => $"{when} • {EventType}{location}"
                };
            }
        }
    }
}