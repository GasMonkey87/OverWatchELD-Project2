using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OverWatchELD.Models.Fleet;

namespace OverWatchELD.Services.Fleet
{
    /// <summary>
    /// Stores which fleet truck a driver has selected as their active/current truck.
    /// This is intentionally small and local because telemetry needs a stable truck choice
    /// before it can safely update location/status without guessing by model/name.
    /// </summary>
    public sealed class ActiveTruckSelectionStore
    {
        private sealed class ActiveTruckSelection
        {
            public string DriverKey { get; set; } = "";
            public string DriverName { get; set; } = "";
            public string DriverDiscordId { get; set; } = "";
            public string TruckId { get; set; } = "";
            public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
        }

        private static readonly JsonSerializerOptions ReadOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly JsonSerializerOptions WriteOpts = new()
        {
            WriteIndented = true
        };

        private readonly string _path;
        private readonly object _lock = new();

        public ActiveTruckSelectionStore()
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OverWatchELD");

            Directory.CreateDirectory(folder);
            _path = Path.Combine(folder, "fleet_active_trucks.json");
        }

        public string GetActiveTruckId(string? driverName, string? driverDiscordId)
        {
            var key = BuildDriverKey(driverName, driverDiscordId);
            if (string.IsNullOrWhiteSpace(key))
                return "";

            lock (_lock)
            {
                return Load()
                    .Where(x => Same(x.DriverKey, key))
                    .OrderByDescending(x => x.UpdatedUtc)
                    .Select(x => x.TruckId?.Trim() ?? "")
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";
            }
        }

        public FleetCommandTruck? GetActiveTruck(FleetCommandStore store, string? driverName, string? driverDiscordId)
        {
            if (store == null)
                return null;

            var activeTruckId = GetActiveTruckId(driverName, driverDiscordId);
            if (string.IsNullOrWhiteSpace(activeTruckId))
                return null;

            return store.LoadAll().FirstOrDefault(t => Same(t.Id, activeTruckId));
        }

        public void SetActiveTruck(string? driverName, string? driverDiscordId, string? truckId)
        {
            driverName = Clean(driverName);
            driverDiscordId = Clean(driverDiscordId);
            truckId = Clean(truckId);

            var key = BuildDriverKey(driverName, driverDiscordId);
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(truckId))
                return;

            lock (_lock)
            {
                var items = Load();
                items.RemoveAll(x => Same(x.DriverKey, key));
                items.Add(new ActiveTruckSelection
                {
                    DriverKey = key,
                    DriverName = driverName,
                    DriverDiscordId = driverDiscordId,
                    TruckId = truckId,
                    UpdatedUtc = DateTimeOffset.UtcNow
                });

                Save(items);
            }
        }

        public bool IsActiveTruck(string? driverName, string? driverDiscordId, string? truckId)
        {
            truckId = Clean(truckId);
            if (string.IsNullOrWhiteSpace(truckId))
                return false;

            return Same(GetActiveTruckId(driverName, driverDiscordId), truckId);
        }

        public static string BuildDriverKey(string? driverName, string? driverDiscordId)
        {
            driverDiscordId = Clean(driverDiscordId);
            if (!string.IsNullOrWhiteSpace(driverDiscordId))
                return "discord:" + driverDiscordId;

            driverName = Clean(driverName);
            if (!string.IsNullOrWhiteSpace(driverName) &&
                !Same(driverName, "Driver") &&
                !Same(driverName, "Unknown Driver"))
            {
                return "name:" + driverName.ToLowerInvariant();
            }

            return "";
        }

        private List<ActiveTruckSelection> Load()
        {
            try
            {
                if (!File.Exists(_path))
                    return new List<ActiveTruckSelection>();

                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<List<ActiveTruckSelection>>(json, ReadOpts)
                       ?? new List<ActiveTruckSelection>();
            }
            catch
            {
                return new List<ActiveTruckSelection>();
            }
        }

        private void Save(List<ActiveTruckSelection> items)
        {
            try
            {
                File.WriteAllText(_path, JsonSerializer.Serialize(items, WriteOpts));
            }
            catch
            {
            }
        }

        private static string Clean(string? value) => value?.Trim() ?? "";

        private static bool Same(string? a, string? b)
        {
            return !string.IsNullOrWhiteSpace(a) &&
                   !string.IsNullOrWhiteSpace(b) &&
                   string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
