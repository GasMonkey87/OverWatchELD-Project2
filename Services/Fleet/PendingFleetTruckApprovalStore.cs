using OverWatchELD.Models.Fleet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services.Fleet
{
    public static class PendingFleetTruckApprovalStore
    {
        private static readonly string Folder =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OverWatchELD");

        private static readonly string FilePath =
            Path.Combine(Folder, "pending_fleet_trucks.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        // OLD COMPAT
        public static List<PendingFleetTruckApproval> Load()
        {
            return LoadAll();
        }

        // OLD COMPAT
        public static void Save(List<PendingFleetTruckApproval> items)
        {
            SaveAll(items);
        }

        // OLD COMPAT
        public static int PendingCount()
        {
            return LoadAll().Count(x =>
                string.Equals(
                    x.Status,
                    "Pending",
                    StringComparison.OrdinalIgnoreCase));
        }

        public static List<PendingFleetTruckApproval> LoadAll()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new List<PendingFleetTruckApproval>();

                var json = File.ReadAllText(FilePath);

                var items =
                    JsonSerializer.Deserialize<List<PendingFleetTruckApproval>>(
                        json,
                        JsonOptions);

                return items ?? new List<PendingFleetTruckApproval>();
            }
            catch
            {
                return new List<PendingFleetTruckApproval>();
            }
        }

        public static void SaveAll(List<PendingFleetTruckApproval> items)
        {
            try
            {
                Directory.CreateDirectory(Folder);

                var json = JsonSerializer.Serialize(
                    items ?? new List<PendingFleetTruckApproval>(),
                    JsonOptions);

                File.WriteAllText(FilePath, json);
            }
            catch
            {
            }
        }

        public static PendingFleetTruckApproval Upsert(
            PendingFleetTruckApproval item)
        {
            var all = LoadAll();

            var existing = all.FirstOrDefault(x =>
                string.Equals(
                    x.Id,
                    item.Id,
                    StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                if (string.IsNullOrWhiteSpace(item.Id))
                    item.Id = Guid.NewGuid().ToString("N");

                item.CreatedUtc = DateTime.UtcNow;
                item.UpdatedUtc = DateTime.UtcNow;

                all.Add(item);
            }
            else
            {
                existing.TruckNumber = item.TruckNumber;
                existing.TruckName = item.TruckName;
                existing.MakeModel = item.MakeModel;
                existing.PlateNumber = item.PlateNumber;
                existing.AssignedDriver = item.AssignedDriver;
                existing.DriverDiscordId = item.DriverDiscordId;
                existing.CurrentLocation = item.CurrentLocation;
                existing.OdometerMiles = item.OdometerMiles;
                existing.FuelPercent = item.FuelPercent;
                existing.HealthPercent = item.HealthPercent;
                existing.DamagePercent = item.DamagePercent;
                existing.Notes = item.Notes;
                existing.Status = item.Status;
                existing.Source = item.Source;
                existing.UpdatedUtc = DateTime.UtcNow;
            }

            SaveAll(all);

            return item;
        }

        public static void DeleteByTruckNumber(string truckNumber)
        {
            var rows = LoadAll()
                .Where(x =>
                    !string.Equals(
                        x.TruckNumber,
                        truckNumber,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

            SaveAll(rows);
        }
    }
}