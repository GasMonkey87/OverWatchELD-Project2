using OverWatchELD.Models;
using OverWatchELD.Models.Fleet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services.Fleet
{
    public sealed class FleetCommandStore
    {
        private static readonly JsonSerializerOptions ReadOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly JsonSerializerOptions WriteOpts = new()
        {
            WriteIndented = true
        };

        private readonly string _folder;
        private readonly string _path;
        private readonly string _legacyCommandPath;
        private readonly string _legacyMaintenancePath;
        private readonly object _lock = new();

        public FleetCommandStore()
        {
            _folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OverWatchELD");

            Directory.CreateDirectory(_folder);

            _path = Path.Combine(_folder, "fleet_unified.json");
            _legacyCommandPath = Path.Combine(_folder, "fleet_command_center.json");
            _legacyMaintenancePath = Path.Combine(_folder, "fleet_store.json");
        }

        public void SaveAll(List<FleetCommandTruck> items)
        {
            lock (_lock)
            {
                Persist(items ?? new List<FleetCommandTruck>());
            }
        }

        public List<FleetCommandTruck> LoadAll()
        {
            lock (_lock)
            {
                try
                {
                    var items = LoadPrimary();
                    if (items.Count > 0)
                        return Normalize(items);

                    var migrated = MigrateFromLegacySources();
                    if (migrated.Count > 0)
                    {
                        Persist(migrated);
                        return Normalize(migrated);
                    }

                    return new List<FleetCommandTruck>();
                }
                catch
                {
                    return new List<FleetCommandTruck>();
                }
            }
        }

        public static void DeleteByTruckNumberStatic(string truckNumber)
        {
            var store = new FleetCommandStore();

            var rows = store.LoadAll()
                .Where(x =>
                    !string.Equals(
                        x.TruckNumber,
                        truckNumber,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

            store.SaveAll(rows);
        }

        public FleetCommandTruck? GetById(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;

            return LoadAll().FirstOrDefault(x =>
                string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public FleetCommandTruck? GetByTruckNumber(string? truckNumber)
        {
            if (string.IsNullOrWhiteSpace(truckNumber))
                return null;

            var key = truckNumber.Trim();
            return LoadAll().FirstOrDefault(x =>
                string.Equals(x.TruckNumber, key, StringComparison.OrdinalIgnoreCase));
        }

        public void Save(FleetCommandTruck item)
        {
            if (item == null) return;

            lock (_lock)
            {
                var all = LoadAll();
                var existing = all.FirstOrDefault(x =>
                    string.Equals(x.Id, item.Id, StringComparison.OrdinalIgnoreCase));

                NormalizeOne(item);

                if (existing == null)
                {
                    if (string.IsNullOrWhiteSpace(item.Id))
                        item.Id = Guid.NewGuid().ToString("N");

                    if (string.IsNullOrWhiteSpace(item.TruckNumber))
                        item.TruckNumber = InferTruckNumber(item);

                    item.UpdatedUtc = DateTimeOffset.UtcNow;
                    all.Add(item);
                }
                else
                {
                    Copy(item, existing);
                    existing.UpdatedUtc = DateTimeOffset.UtcNow;
                }

                Persist(all);
            }
        }

        public void Delete(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return;

            lock (_lock)
            {
                var all = LoadAll();
                all.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
                Persist(all);
            }
        }


        public string GetNextAvailableTruckNumber()
        {
            var used = new HashSet<int>();

            foreach (var item in LoadAll())
            {
                var truckNumber = (item.TruckNumber ?? "").Trim();
                if (string.IsNullOrWhiteSpace(truckNumber))
                    continue;

                var match = System.Text.RegularExpressions.Regex.Match(
                    truckNumber,
                    @"(?:TRK-|TRUCK-|UNIT-|#)?(\d+)$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (!match.Success)
                    continue;

                if (int.TryParse(match.Groups[1].Value, out var n) && n > 0)
                    used.Add(n);
            }

            var next = 1;
            while (used.Contains(next))
                next++;

            return $"TRK-{next:000}";
        }

        public FleetCommandTruck? FindByIdentity(string? plateNumber, string? truckName, string? model, string? assignedDriver)
        {
            var plate = (plateNumber ?? "").Trim();
            var name = (truckName ?? "").Trim();
            var modelText = (model ?? "").Trim();
            var driver = (assignedDriver ?? "").Trim();

            var all = LoadAll();

            foreach (var item in all)
            {
                if (!string.IsNullOrWhiteSpace(plate) &&
                    string.Equals((item.PlateNumber ?? "").Trim(), plate, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }

            foreach (var item in all)
            {
                var sameDriver = !string.IsNullOrWhiteSpace(driver) &&
                                 string.Equals((item.AssignedDriver ?? "").Trim(), driver, StringComparison.OrdinalIgnoreCase);

                var sameName = !string.IsNullOrWhiteSpace(name) &&
                               string.Equals((item.TruckName ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase);

                var sameModel = !string.IsNullOrWhiteSpace(modelText) &&
                                string.Equals((item.Model ?? "").Trim(), modelText, StringComparison.OrdinalIgnoreCase);

                if (sameDriver && sameName && sameModel)
                    return item;
            }

            foreach (var item in all)
            {
                var sameName = !string.IsNullOrWhiteSpace(name) &&
                               string.Equals((item.TruckName ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase);

                var sameModel = !string.IsNullOrWhiteSpace(modelText) &&
                                string.Equals((item.Model ?? "").Trim(), modelText, StringComparison.OrdinalIgnoreCase);

                if (sameName && sameModel)
                    return item;
            }

            return null;
        }

        private List<FleetCommandTruck> LoadPrimary()
        {
            if (!File.Exists(_path))
                return new List<FleetCommandTruck>();

            var json = File.ReadAllText(_path);
            var items = JsonSerializer.Deserialize<List<FleetCommandTruck>>(json, ReadOpts);
            return items ?? new List<FleetCommandTruck>();
        }

        private List<FleetCommandTruck> MigrateFromLegacySources()
        {
            var map = new Dictionary<string, FleetCommandTruck>(StringComparer.OrdinalIgnoreCase);

            foreach (var old in LoadLegacyCommandCenter())
                Merge(map, old);

            foreach (var old in LoadLegacyFleetService())
                Merge(map, old);

            foreach (var old in LoadLegacyMaintenanceStore())
                Merge(map, old);

            return map.Values
                .OrderBy(x => ParseTruckNumberForSort(x.TruckNumber))
                .ThenBy(x => x.TruckNumber, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.PlateNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private IEnumerable<FleetCommandTruck> LoadLegacyCommandCenter()
        {
            var results = new List<FleetCommandTruck>();

            try
            {
                if (!File.Exists(_legacyCommandPath))
                    return results;

                var json = File.ReadAllText(_legacyCommandPath);
                var items = JsonSerializer.Deserialize<List<FleetCommandTruck>>(json, ReadOpts);

                if (items != null)
                    results.AddRange(items);
            }
            catch
            {
            }

            return results;
        }

        private IEnumerable<FleetCommandTruck> LoadLegacyFleetService()
        {
            var results = new List<FleetCommandTruck>();

            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "fleet_trucks.json"),
                Path.Combine(Environment.CurrentDirectory, "fleet_trucks.json"),
                Path.Combine(_folder, "fleet_trucks.json")
            };

            var src = candidates.FirstOrDefault(File.Exists);
            if (string.IsNullOrWhiteSpace(src))
                return results;

            List<OverWatchELD.Models.FleetTruck>? items = null;
            try
            {
                var json = File.ReadAllText(src);
                items = JsonSerializer.Deserialize<List<OverWatchELD.Models.FleetTruck>>(json, ReadOpts);
            }
            catch
            {
            }

            if (items == null)
                return results;

            foreach (var item in items)
            {
                results.Add(new FleetCommandTruck
                {
                    Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id,
                    TruckNumber = FirstNonEmpty(item.PlateNumber, item.TruckName, item.Model, item.Id),
                    PlateNumber = item.PlateNumber ?? "",
                    TruckName = item.TruckName ?? "",
                    Model = item.Model ?? "",
                    ModName = item.ModName ?? "",
                    AssignedDriver = FirstNonEmpty(item.DriverName, item.DiscordUsername),
                    DriverDiscordId = item.DiscordUserId ?? "",
                    Location = item.LastSeenLocation ?? "",
                    OdometerMiles = item.OdometerMiles ?? item.LastKnownOdometerMiles ?? 0,
                    IsActive = item.IsActive,
                    Status = item.IsActive ? "Active" : "Offline",
                    TotalFuelCost = item.TotalFuelCost,
                    TotalTollCost = item.TotalTollCost,
                    TotalMaintenanceCost = item.TotalMaintenanceCost,
                    TotalRepairCost = item.TotalRepairCost,
                    LastFuelUtc = item.LastFuelUtc,
                    LastTollUtc = item.LastTollUtc,
                    LastMaintenanceUtc = item.LastMaintenanceUtc,
                    LastRepairUtc = item.LastRepairUtc,
                    UpdatedUtc = item.LastSeenUtc == default
                        ? DateTimeOffset.UtcNow
                        : new DateTimeOffset(DateTime.SpecifyKind(item.LastSeenUtc, DateTimeKind.Utc))
                });
            }

            return results;
        }

        private IEnumerable<FleetCommandTruck> LoadLegacyMaintenanceStore()
        {
            var results = new List<FleetCommandTruck>();

            try
            {
                if (!File.Exists(_legacyMaintenancePath))
                    return results;

                var json = File.ReadAllText(_legacyMaintenancePath);
                var items = JsonSerializer.Deserialize<Dictionary<string, OverWatchELD.Models.Fleet.FleetTruck>>(json, ReadOpts);
                if (items == null)
                    return results;

                foreach (var kv in items)
                {
                    var item = kv.Value;
                    if (item == null) continue;

                    var health = 100.0 - new[]
                    {
                        item.EngineDamagePct,
                        item.TransmissionDamagePct,
                        item.CabinDamagePct,
                        item.ChassisDamagePct,
                        item.WheelsDamagePct
                    }.Max();

                    results.Add(new FleetCommandTruck
                    {
                        TruckNumber = FirstNonEmpty(item.Plate, item.Nickname, item.MakeModel),
                        PlateNumber = item.Plate ?? "",
                        TruckName = item.Nickname ?? "",
                        Model = item.MakeModel ?? "",
                        AssignedDriver = item.AssignedDriver ?? "",
                        Location = item.LastKnownLocation ?? "",
                        OdometerMiles = item.OdometerMiles,
                        FuelPercent = item.FuelPercent > 0 ? item.FuelPercent : item.FuelPct,
                        HealthPercent = (int)Math.Round(Math.Clamp(item.ConditionPercent > 0 ? item.ConditionPercent : health, 0, 100)),
                        IsOnline = item.IsOnline || item.LastTelemetryUtc > DateTimeOffset.UtcNow.AddHours(-6),
                        IsDriving = item.IsDriving,
                        Status = item.NeedsService ? "Needs Service" : (!string.IsNullOrWhiteSpace(item.AssignedDriver) ? "Active" : "Unassigned"),
                        TotalFuelCost = item.TotalFuelCost,
                        TotalTollCost = item.TotalTollCost,
                        TotalMaintenanceCost = item.TotalMaintenanceCost,
                        TotalRepairCost = item.TotalRepairCost,
                        LastFuelUtc = item.LastFuelUtc,
                        LastTollUtc = item.LastTollUtc,
                        LastMaintenanceUtc = item.LastMaintenanceUtc,
                        LastRepairUtc = item.LastRepairUtc,
                        LastServiceDate = item.LastMaintenanceUtc?.LocalDateTime.Date,
                        LastInspectionDate = item.LastDotInspectionUtc == DateTimeOffset.MinValue ? null : item.LastDotInspectionUtc.LocalDateTime.Date,
                        ServiceDueDate = BuildDueDate(item.OdometerMiles, item.LastOilChangeMiles, 25000),
                        InspectionDueDate = item.LastDotInspectionUtc == DateTimeOffset.MinValue ? null : item.LastDotInspectionUtc.LocalDateTime.Date.AddDays(7),
                        UpdatedUtc = item.LastTelemetryUtc == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : item.LastTelemetryUtc
                    });
                }
            }
            catch
            {
            }

            return results;
        }

        private static DateTime? BuildDueDate(double odo, double lastServiceAtMiles, double intervalMiles)
        {
            if (odo <= 0 || intervalMiles <= 0)
                return null;

            var milesSince = Math.Max(0, odo - lastServiceAtMiles);
            var milesLeft = Math.Max(0, intervalMiles - milesSince);
            var days = (int)Math.Ceiling(milesLeft / 500.0);
            return DateTime.Today.AddDays(days <= 0 ? 0 : days);
        }

        private static void Merge(Dictionary<string, FleetCommandTruck> map, FleetCommandTruck incoming)
        {
            NormalizeOne(incoming);
            var key = InferTruckNumber(incoming);
            incoming.TruckNumber = key;

            if (!map.TryGetValue(key, out var existing))
            {
                map[key] = incoming;
                return;
            }

            existing.PlateNumber = Prefer(existing.PlateNumber, incoming.PlateNumber);
            existing.TruckName = Prefer(existing.TruckName, incoming.TruckName);
            existing.Model = Prefer(existing.Model, incoming.Model);
            existing.ModName = Prefer(existing.ModName, incoming.ModName);
            existing.AssignedDriver = Prefer(existing.AssignedDriver, incoming.AssignedDriver);
            existing.DriverDiscordId = Prefer(existing.DriverDiscordId, incoming.DriverDiscordId);
            existing.CurrentLoadNumber = Prefer(existing.CurrentLoadNumber, incoming.CurrentLoadNumber);
            existing.Status = Prefer(existing.Status, incoming.Status);
            existing.Location = Prefer(existing.Location, incoming.Location);
            existing.HealthPercent = Math.Max(existing.HealthPercent, incoming.HealthPercent);
            existing.FuelPercent = Math.Max(existing.FuelPercent, incoming.FuelPercent);
            existing.OdometerMiles = Math.Max(existing.OdometerMiles, incoming.OdometerMiles);
            existing.IsActive = existing.IsActive || incoming.IsActive;
            existing.IsOnline = existing.IsOnline || incoming.IsOnline;
            existing.IsDriving = existing.IsDriving || incoming.IsDriving;
            existing.ServiceDueDate ??= incoming.ServiceDueDate;
            existing.LastServiceDate ??= incoming.LastServiceDate;
            existing.InspectionDueDate ??= incoming.InspectionDueDate;
            existing.LastInspectionDate ??= incoming.LastInspectionDate;
            existing.TotalFuelCost = Math.Max(existing.TotalFuelCost, incoming.TotalFuelCost);
            existing.TotalTollCost = Math.Max(existing.TotalTollCost, incoming.TotalTollCost);
            existing.TotalMaintenanceCost = Math.Max(existing.TotalMaintenanceCost, incoming.TotalMaintenanceCost);
            existing.TotalRepairCost = Math.Max(existing.TotalRepairCost, incoming.TotalRepairCost);
            existing.LastFuelUtc ??= incoming.LastFuelUtc;
            existing.LastTollUtc ??= incoming.LastTollUtc;
            existing.LastMaintenanceUtc ??= incoming.LastMaintenanceUtc;
            existing.LastRepairUtc ??= incoming.LastRepairUtc;

            if (incoming.UpdatedUtc > existing.UpdatedUtc)
                existing.UpdatedUtc = incoming.UpdatedUtc;

            RefreshDerivedStatus(existing);
        }

        private static string Prefer(string current, string incoming)
        {
            current = CleanDriverName(current);
            incoming = CleanDriverName(incoming);
            return string.IsNullOrWhiteSpace(current) ? incoming : current;
        }

        private static string CleanDriverName(string? value)
        {
            var text = (value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text)) return "";
            if (text.Equals("Driver", StringComparison.OrdinalIgnoreCase)) return "";
            if (text.Equals("Unknown Driver", StringComparison.OrdinalIgnoreCase)) return "";
            if (text.Equals("Unassigned", StringComparison.OrdinalIgnoreCase)) return "";
            if (text.Equals("No Driver", StringComparison.OrdinalIgnoreCase)) return "";
            return text;
        }

        private static void Copy(FleetCommandTruck src, FleetCommandTruck dest)
        {
            dest.TruckNumber = src.TruckNumber;
            dest.PlateNumber = src.PlateNumber;
            dest.TruckName = src.TruckName;
            dest.Model = src.Model;
            dest.ModName = src.ModName;
            dest.AssignedDriver = src.AssignedDriver;
            dest.DriverDiscordId = src.DriverDiscordId;
            dest.CurrentLoadNumber = src.CurrentLoadNumber;
            dest.Status = src.Status;
            dest.Location = src.Location;
            dest.HealthPercent = src.HealthPercent;
            dest.FuelPercent = src.FuelPercent;
            dest.OdometerMiles = src.OdometerMiles;
            dest.IsActive = src.IsActive;
            dest.IsOnline = src.IsOnline;
            dest.IsDriving = src.IsDriving;
            dest.ServiceDueDate = src.ServiceDueDate;
            dest.LastServiceDate = src.LastServiceDate;
            dest.InspectionDueDate = src.InspectionDueDate;
            dest.LastInspectionDate = src.LastInspectionDate;
            dest.TotalFuelCost = src.TotalFuelCost;
            dest.TotalTollCost = src.TotalTollCost;
            dest.TotalMaintenanceCost = src.TotalMaintenanceCost;
            dest.TotalRepairCost = src.TotalRepairCost;
            dest.LastFuelUtc = src.LastFuelUtc;
            dest.LastTollUtc = src.LastTollUtc;
            dest.LastMaintenanceUtc = src.LastMaintenanceUtc;
            dest.LastRepairUtc = src.LastRepairUtc;
            RefreshDerivedStatus(dest);
        }

        private void Persist(List<FleetCommandTruck> items)
        {
            try
            {
                Directory.CreateDirectory(_folder);
                items = Normalize(items)
                    .OrderBy(x => ParseTruckNumberForSort(x.TruckNumber))
                    .ThenBy(x => x.TruckNumber, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.PlateNumber, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var json = JsonSerializer.Serialize(items, WriteOpts);
                File.WriteAllText(_path, json);
            }
            catch
            {
            }
        }

        private static List<FleetCommandTruck> Normalize(List<FleetCommandTruck> items)
        {
            foreach (var item in items)
                NormalizeOne(item);

            return items;
        }

        private static void NormalizeOne(FleetCommandTruck item)
        {
            item.TruckNumber = (item.TruckNumber ?? "").Trim();
            item.PlateNumber = (item.PlateNumber ?? "").Trim();
            item.TruckName = (item.TruckName ?? "").Trim();
            item.Model = (item.Model ?? "").Trim();
            item.ModName = (item.ModName ?? "").Trim();
            item.AssignedDriver = CleanDriverName(item.AssignedDriver);
            item.DriverDiscordId = (item.DriverDiscordId ?? "").Trim();
            item.CurrentLoadNumber = (item.CurrentLoadNumber ?? "").Trim();
            item.Status = (item.Status ?? "").Trim();
            item.Location = (item.Location ?? "").Trim();
            item.HealthPercent = Math.Clamp(item.HealthPercent, 0, 100);
            item.FuelPercent = Math.Clamp(item.FuelPercent, 0, 100);
            item.OdometerMiles = Math.Max(0, item.OdometerMiles);

            if (string.IsNullOrWhiteSpace(item.TruckNumber))
                item.TruckNumber = InferTruckNumber(item);

            RefreshDerivedStatus(item);
        }

        private static void RefreshDerivedStatus(FleetCommandTruck item)
        {
            if (item.Status.Equals("Out of Service", StringComparison.OrdinalIgnoreCase))
                return;

            if (item.ServiceDueDate.HasValue && item.ServiceDueDate.Value.Date <= DateTime.Today)
                item.Status = "Needs Service";
            else if (item.InspectionDueDate.HasValue && item.InspectionDueDate.Value.Date <= DateTime.Today)
                item.Status = "Needs Inspection";
            else if (!string.IsNullOrWhiteSpace(item.CurrentLoadNumber))
                item.Status = "Assigned Load";
            else if (!string.IsNullOrWhiteSpace(item.AssignedDriver))
                item.Status = item.IsDriving ? "Driving" : "Active";
            else
                item.Status = "Unassigned";
        }

        private static string InferTruckNumber(FleetCommandTruck item)
        {
            return FirstNonEmpty(item.TruckNumber, item.PlateNumber, item.TruckName, item.Model, item.Id, "UNIT");
        }

        private static int ParseTruckNumberForSort(string? truckNumber)
        {
            var text = (truckNumber ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text))
                return int.MaxValue;

            var match = System.Text.RegularExpressions.Regex.Match(
                text,
                @"(?:TRK-|TRUCK-|UNIT-|#)?(\d+)$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (match.Success && int.TryParse(match.Groups[1].Value, out var n))
                return n;

            return int.MaxValue;
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                var s = (value ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(s))
                    return s;
            }

            return "";
        }
    }
}