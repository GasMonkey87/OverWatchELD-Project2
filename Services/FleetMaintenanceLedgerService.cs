using OverWatchELD.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public sealed class FleetMaintenanceLedgerService
    {
        private readonly string _path;

        public FleetMaintenanceLedgerService()
        {
            var configDir = Path.Combine(AppContext.BaseDirectory, "Config");
            Directory.CreateDirectory(configDir);
            _path = Path.Combine(configDir, "fleet_maintenance_ledger.json");
        }

        public List<FleetCostEntry> LoadAll()
        {
            try
            {
                if (!File.Exists(_path))
                    return new List<FleetCostEntry>();

                var json = File.ReadAllText(_path);
                var items = JsonSerializer.Deserialize<List<FleetCostEntry>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return items ?? new List<FleetCostEntry>();
            }
            catch
            {
                return new List<FleetCostEntry>();
            }
        }

        public void SaveAll(List<FleetCostEntry> items)
        {
            try
            {
                items ??= new List<FleetCostEntry>();

                var json = JsonSerializer.Serialize(items.OrderByDescending(x => x.DateUtc).ToList(),
                    new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(_path, json);
            }
            catch
            {
            }
        }

        public void AddOrUpdate(FleetCostEntry entry)
        {
            var items = LoadAll();

            var existing = items.FirstOrDefault(x => string.Equals(x.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                if (string.IsNullOrWhiteSpace(entry.Id))
                    entry.Id = Guid.NewGuid().ToString();

                items.Add(entry);
            }
            else
            {
                existing.TruckId = entry.TruckId ?? "";
                existing.TruckName = entry.TruckName ?? "";
                existing.PlateNumber = entry.PlateNumber ?? "";
                existing.DateUtc = entry.DateUtc;
                existing.BillType = entry.BillType ?? "Fuel";
                existing.Amount = entry.Amount;
                existing.OdometerMiles = entry.OdometerMiles;
                existing.Vendor = entry.Vendor ?? "";
                existing.Location = entry.Location ?? "";
                existing.Notes = entry.Notes ?? "";
                existing.RequiresFollowUp = entry.RequiresFollowUp;
                existing.IsResolved = entry.IsResolved;
                existing.DueAtMiles = entry.DueAtMiles;
                existing.DueDateUtc = entry.DueDateUtc;
            }

            SaveAll(items);
        }

        public void Remove(string id)
        {
            var items = LoadAll();
            items.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            SaveAll(items);
        }

        public List<FleetCostEntry> Search(
            string searchText,
            string billType,
            string truckId,
            bool alertsOnly)
        {
            IEnumerable<FleetCostEntry> query = LoadAll();

            if (!string.IsNullOrWhiteSpace(truckId) && truckId != "__ALL__")
                query = query.Where(x => string.Equals(x.TruckId, truckId, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(billType) && billType != "All")
                query = query.Where(x => string.Equals(x.BillType, billType, StringComparison.OrdinalIgnoreCase));

            if (alertsOnly)
                query = query.Where(x => x.RequiresFollowUp || x.IsOverdueByDate || x.IsOverdueByMiles);

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                var q = searchText.Trim();

                query = query.Where(x =>
                    (x.TruckName ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (x.PlateNumber ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (x.BillType ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (x.Vendor ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (x.Location ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (x.Notes ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (x.Status ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            return query
                .OrderByDescending(x => x.DateUtc)
                .ThenByDescending(x => x.RequiresFollowUp)
                .ToList();
        }

        public decimal TotalCost() => LoadAll().Sum(x => x.Amount);

        public decimal ThisMonthCost()
        {
            var now = DateTime.UtcNow;
            return LoadAll()
                .Where(x => x.DateUtc.Year == now.Year && x.DateUtc.Month == now.Month)
                .Sum(x => x.Amount);
        }

        public int OpenAlertsCount()
        {
            return LoadAll().Count(x => !x.IsResolved && (x.RequiresFollowUp || x.IsOverdueByDate || x.IsOverdueByMiles));
        }

        public int OverdueCount()
        {
            return LoadAll().Count(x => !x.IsResolved && (x.IsOverdueByDate || x.IsOverdueByMiles));
        }

        public int UnresolvedTicketsCount()
        {
            return LoadAll().Count(x =>
                !x.IsResolved &&
                string.Equals(x.BillType, "Ticket", StringComparison.OrdinalIgnoreCase));
        }

        public int UnresolvedDamageCount()
        {
            return LoadAll().Count(x =>
                !x.IsResolved &&
                string.Equals(x.BillType, "Damage", StringComparison.OrdinalIgnoreCase));
        }
    }
}