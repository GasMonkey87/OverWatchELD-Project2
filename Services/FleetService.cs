using OverWatchELD.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public sealed class FleetService
    {
        private readonly string _filePath;

        public FleetService()
        {
            _filePath = Path.Combine(AppContext.BaseDirectory, "fleet_trucks.json");
        }

        public List<FleetTruck> LoadAll()
        {
            try
            {
                if (!File.Exists(_filePath))
                    return new List<FleetTruck>();

                var json = File.ReadAllText(_filePath);
                var items = JsonSerializer.Deserialize<List<FleetTruck>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return items ?? new List<FleetTruck>();
            }
            catch
            {
                return new List<FleetTruck>();
            }
        }

        public void SaveAll(List<FleetTruck> trucks)
        {
            var json = JsonSerializer.Serialize(trucks.OrderByDescending(x => x.LastSeenUtc).ToList(),
                new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(_filePath, json);
        }

        public void AddOrUpdate(FleetTruck truck)
        {
            var items = LoadAll();
            var existing = items.FirstOrDefault(x => x.Id == truck.Id);

            if (existing == null)
            {
                items.Add(truck);
            }
            else
            {
                existing.TruckName = truck.TruckName;
                existing.Model = truck.Model;
                existing.ModName = truck.ModName;
                existing.PlateNumber = truck.PlateNumber;
                existing.DiscordUserId = truck.DiscordUserId;
                existing.DiscordUsername = truck.DiscordUsername;
                existing.DriverName = truck.DriverName;
                existing.LastSeenUtc = truck.LastSeenUtc;
                existing.OdometerMiles = truck.OdometerMiles;
                existing.IsActive = truck.IsActive;
                existing.TotalFuelCost = truck.TotalFuelCost;
                existing.TotalTollCost = truck.TotalTollCost;
                existing.TotalMaintenanceCost = truck.TotalMaintenanceCost;
                existing.TotalRepairCost = truck.TotalRepairCost;
                existing.LastKnownOdometerMiles = truck.LastKnownOdometerMiles;
                existing.LastKnownDutyStatus = truck.LastKnownDutyStatus;
                existing.LastFuelUtc = truck.LastFuelUtc;
                existing.LastTollUtc = truck.LastTollUtc;
                existing.LastMaintenanceUtc = truck.LastMaintenanceUtc;
                existing.LastRepairUtc = truck.LastRepairUtc;
            }

            SaveAll(items);
        }

        public void Remove(string id)
        {
            var items = LoadAll();
            items.RemoveAll(x => x.Id == id);
            SaveAll(items);
        }
    }
}