using OverWatchELD.Models;
using OverWatchELD.Models.Fleet;
using OverWatchELD.Services;
using OverWatchELD.Services.Fleet;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace OverWatchELD.ViewModels
{
    public sealed class FleetCommandCenterViewModel : INotifyPropertyChanged
    {
        private readonly FleetCommandStore _store = new();

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set { _searchText = value ?? ""; OnPropertyChanged(); Refresh(); }
        }

        private string _selectedFilter = "All";
        public string SelectedFilter
        {
            get => _selectedFilter;
            set { _selectedFilter = string.IsNullOrWhiteSpace(value) ? "All" : value; OnPropertyChanged(); Refresh(); }
        }

        public ObservableCollection<string> Filters { get; } = new()
        {
            "All", "Assigned Load", "Driving", "Active", "Needs Service",
            "Needs Inspection", "Unassigned", "Out of Service", "Inactive"
        };

        public ObservableCollection<FleetCommandTruckRow> Trucks { get; } = new();
        public ObservableCollection<string> DriverOptions { get; } = new();
        public ObservableCollection<string> LoadOptions { get; } = new();

        private FleetCommandTruckRow? _selectedTruck;
        public FleetCommandTruckRow? SelectedTruck
        {
            get => _selectedTruck;
            set { _selectedTruck = value; OnPropertyChanged(); }
        }

        public string TotalFleetText { get => _totalFleetText; private set { _totalFleetText = value; OnPropertyChanged(); } }
        private string _totalFleetText = "0";

        public string AssignedText { get => _assignedText; private set { _assignedText = value; OnPropertyChanged(); } }
        private string _assignedText = "0";

        public string UnassignedText { get => _unassignedText; private set { _unassignedText = value; OnPropertyChanged(); } }
        private string _unassignedText = "0";

        public string NeedsServiceText { get => _needsServiceText; private set { _needsServiceText = value; OnPropertyChanged(); } }
        private string _needsServiceText = "0";

        public string NeedsInspectionText { get => _needsInspectionText; private set { _needsInspectionText = value; OnPropertyChanged(); } }
        private string _needsInspectionText = "0";

        public string ActiveDriversText { get => _activeDriversText; private set { _activeDriversText = value; OnPropertyChanged(); } }
        private string _activeDriversText = "0";

        public string ReceiptTicketsText { get => _receiptTicketsText; private set { _receiptTicketsText = value; OnPropertyChanged(); } }
        private string _receiptTicketsText = "0";

        public void Refresh()
        {
            EnsureApprovedVtcTrucksExistInFleetCommand();

            var all = _store.LoadAll();

            UpdateDriverOptions(all);
            UpdateLoadOptions(all);
            UpdateTopStats(all);

            var filtered = all.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var q = SearchText.Trim();
                filtered = filtered.Where(x =>
                    Contains(x.TruckNumber, q) ||
                    Contains(x.PlateNumber, q) ||
                    Contains(x.TruckName, q) ||
                    Contains(x.Model, q) ||
                    Contains(x.AssignedDriver, q) ||
                    Contains(x.CurrentLoadNumber, q) ||
                    Contains(x.Location, q) ||
                    Contains(x.Status, q) ||
                    Contains(ReadString(x, "Destination"), q) ||
                    Contains(ReadString(x, "DestinationCity"), q) ||
                    Contains(ReadString(x, "DestinationCompany"), q) ||
                    Contains(ReadString(x, "TrailerName"), q) ||
                    Contains(ReadString(x, "CargoName"), q) ||
                    Contains(ReadString(x, "Warnings"), q));
            }

            filtered = ApplyFilter(filtered, SelectedFilter);

            var rows = filtered
                .Select(ToRow)
                .OrderByDescending(x => x.SortPriority)
                .ThenByDescending(x => x.IsRecentlyUpdated)
                .ThenByDescending(x => x.LastUpdatedUtcSort)
                .ThenBy(x => x.TruckNumber, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.PlateNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Trucks.Clear();
            foreach (var row in rows)
                Trucks.Add(row);
        }

        private void EnsureApprovedVtcTrucksExistInFleetCommand()
        {
            try
            {
                foreach (var locked in FleetTruckNumberLockStore.Load())
                {
                    var truckNumber = FleetTruckNumberLockStore.Normalize(locked.TruckNumber);
                    if (string.IsNullOrWhiteSpace(truckNumber))
                        continue;

                    var truck = _store.GetByTruckNumber(truckNumber) ?? new FleetCommandTruck
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        TruckNumber = truckNumber,
                        HealthPercent = 100,
                        FuelPercent = 100,
                        ServiceDueDate = DateTime.Today.AddDays(14),
                        InspectionDueDate = DateTime.Today.AddDays(7),
                        UpdatedUtc = DateTimeOffset.UtcNow
                    };

                    truck.TruckNumber = truckNumber;
                    truck.TruckName = FirstNonEmpty(truck.TruckName, locked.TruckName);
                    truck.AssignedDriver = FirstNonEmpty(truck.AssignedDriver, locked.AssignedDriver);

                    if (string.IsNullOrWhiteSpace(truck.Status) || Same(truck.Status, "Unassigned"))
                        truck.Status = string.IsNullOrWhiteSpace(truck.AssignedDriver) ? "Unassigned" : "Active";

                    truck.UpdatedUtc = DateTimeOffset.UtcNow;
                    _store.Save(truck);
                }

                foreach (var approval in PendingFleetTruckApprovalStore.Load().Where(x => Same(x.Status, "Approved")))
                {
                    var truckNumber = FleetTruckNumberLockStore.Normalize(approval.TruckNumber);
                    if (string.IsNullOrWhiteSpace(truckNumber))
                        continue;

                    var truck = _store.GetByTruckNumber(truckNumber)
                                ?? _store.LoadAll().FirstOrDefault(x => Same(x.PlateNumber, approval.PlateNumber))
                                ?? new FleetCommandTruck
                                {
                                    Id = Guid.NewGuid().ToString("N"),
                                    TruckNumber = truckNumber,
                                    HealthPercent = 100,
                                    FuelPercent = 100,
                                    ServiceDueDate = DateTime.Today.AddDays(14),
                                    InspectionDueDate = DateTime.Today.AddDays(7)
                                };

                    truck.TruckNumber = truckNumber;
                    truck.TruckName = FirstNonEmpty(approval.TruckName, truck.TruckName, truckNumber);
                    truck.Model = FirstNonEmpty(approval.MakeModel, truck.Model);
                    truck.PlateNumber = FirstNonEmpty(approval.PlateNumber, truck.PlateNumber);
                    truck.AssignedDriver = FirstNonEmpty(approval.AssignedDriver, truck.AssignedDriver);
                    truck.Location = FirstNonEmpty(approval.CurrentLocation, truck.Location);
                    truck.OdometerMiles = Math.Max(truck.OdometerMiles, approval.OdometerMiles);
                    truck.FuelPercent = ClampToPercent(approval.FuelPercent > 0 ? approval.FuelPercent : truck.FuelPercent);
                    truck.HealthPercent = (int)Math.Round(ClampToPercent(approval.HealthPercent > 0 ? approval.HealthPercent : truck.HealthPercent));

                    if (string.IsNullOrWhiteSpace(truck.Status) || Same(truck.Status, "Unassigned"))
                        truck.Status = string.IsNullOrWhiteSpace(truck.AssignedDriver) ? "Unassigned" : "Active";

                    truck.UpdatedUtc = DateTimeOffset.UtcNow;
                    _store.Save(truck);
                }
            }
            catch
            {
            }
        }

        public void AddTruckFromTelemetry(TelemetryTruckCapture capture)
        {
            if (capture == null) return;

            var truck = CreateOrGetTelemetryTruck(capture, capture.DriverName);

            truck.TruckName = (capture.TruckName ?? "").Trim();
            truck.PlateNumber = (capture.Plate ?? "").Trim();
            truck.AssignedDriver = FirstNonEmpty(capture.DriverName, truck.AssignedDriver);
            truck.OdometerMiles = Math.Max(0, capture.Odometer);
            truck.FuelPercent = ClampToPercent(capture.FuelPct);
            truck.HealthPercent = (int)Math.Round(ClampToPercent(capture.ConditionPct));

            if (string.IsNullOrWhiteSpace(truck.Status) || Same(truck.Status, "Unassigned"))
                truck.Status = string.IsNullOrWhiteSpace(truck.AssignedDriver) ? "Unassigned" : "Active";

            truck.UpdatedUtc = DateTimeOffset.UtcNow;

            _store.Save(truck);
            Refresh();
        }

        public FleetCommandTruck? LoadSelectedFull()
        {
            if (SelectedTruck == null) return null;
            return _store.GetById(SelectedTruck.Id);
        }

        public string GetNextAvailableTruckNumber() => _store.GetNextAvailableTruckNumber();

        public bool TruckExists(string truckNumber)
        {
            var key = (truckNumber ?? "").Trim();
            if (string.IsNullOrWhiteSpace(key))
                return false;

            return _store.GetByTruckNumber(key) != null;
        }

        public FleetCommandTruck? GetTruckByNumber(string truckNumber)
        {
            var key = (truckNumber ?? "").Trim();
            if (string.IsNullOrWhiteSpace(key))
                return null;

            return _store.GetByTruckNumber(key);
        }

        public FleetCommandTruck CreateOrGetTruck(string truckNumber)
        {
            var key = (truckNumber ?? "").Trim();
            if (string.IsNullOrWhiteSpace(key))
                key = _store.GetNextAvailableTruckNumber();

            var existing = _store.GetByTruckNumber(key);
            if (existing != null) return existing;

            return new FleetCommandTruck
            {
                TruckNumber = key,
                Status = "Unassigned",
                HealthPercent = 100,
                FuelPercent = 100,
                ServiceDueDate = DateTime.Today.AddDays(14),
                InspectionDueDate = DateTime.Today.AddDays(7),
                UpdatedUtc = DateTimeOffset.UtcNow
            };
        }

        public FleetCommandTruck CreateOrGetTelemetryTruck(TelemetryTruckCapture snapshot, string? fallbackDriverName)
        {
            var driverName = FirstNonEmpty(snapshot?.DriverName, fallbackDriverName);
            var plate = (snapshot?.Plate ?? "").Trim();
            var truckName = (snapshot?.TruckName ?? "").Trim();

            FleetCommandTruck? existing = null;

            if (!string.IsNullOrWhiteSpace(plate))
                existing = _store.LoadAll().FirstOrDefault(x => Same(x.PlateNumber, plate));

            if (existing == null && !string.IsNullOrWhiteSpace(driverName))
                existing = _store.LoadAll().FirstOrDefault(x => Same(x.AssignedDriver, driverName));

            if (existing == null && !string.IsNullOrWhiteSpace(truckName) && !string.IsNullOrWhiteSpace(driverName))
                existing = _store.LoadAll().FirstOrDefault(x => Same(x.TruckName, truckName) && Same(x.AssignedDriver, driverName));

            if (existing != null)
                return existing;

            return new FleetCommandTruck
            {
                TruckNumber = _store.GetNextAvailableTruckNumber(),
                TruckName = truckName,
                Model = "",
                PlateNumber = plate,
                AssignedDriver = driverName,
                Location = "",
                OdometerMiles = Math.Max(0, snapshot?.Odometer ?? 0),
                Status = string.IsNullOrWhiteSpace(driverName) ? "Unassigned" : "Active",
                HealthPercent = (int)Math.Round(ClampToPercent(snapshot?.ConditionPct ?? 100)),
                FuelPercent = ClampToPercent(snapshot?.FuelPct ?? 100),
                ServiceDueDate = DateTime.Today.AddDays(14),
                InspectionDueDate = DateTime.Today.AddDays(7),
                UpdatedUtc = DateTimeOffset.UtcNow
            };
        }

        public void SaveTruck(FleetCommandTruck truck)
        {
            if (truck == null) return;
            truck.UpdatedUtc = DateTimeOffset.UtcNow;
            _store.Save(truck);
            Refresh();
        }

        public void AssignTruck(FleetCommandTruck truck, string driverName, string loadNumber, string? explicitDriverDiscordId = null)
        {
            if (truck == null) return;

            driverName = CleanDriverName(driverName);
            loadNumber = (loadNumber ?? "").Trim();

            string driverDiscordId = (explicitDriverDiscordId ?? "").Trim();

            try
            {
                if (string.IsNullOrWhiteSpace(driverDiscordId))
                {
                    var match = DiscordIdentityStore.LoadAll().FirstOrDefault(x =>
                        Same(x.DiscordUsername, driverName) ||
                        Same(x.DiscordUserId, driverName));

                    if (match != null)
                        driverDiscordId = (match.DiscordUserId ?? "").Trim();
                }
            }
            catch
            {
                driverDiscordId = (explicitDriverDiscordId ?? "").Trim();
            }

            if (!string.IsNullOrWhiteSpace(driverName) || !string.IsNullOrWhiteSpace(driverDiscordId))
            {
                foreach (var other in _store.LoadAll())
                {
                    if (other == null || Same(other.Id, truck.Id))
                        continue;

                    var sameDriverName = !string.IsNullOrWhiteSpace(driverName) && Same(other.AssignedDriver, driverName);
                    var sameDiscordId = !string.IsNullOrWhiteSpace(driverDiscordId) && Same(other.DriverDiscordId, driverDiscordId);

                    if (sameDriverName || sameDiscordId)
                    {
                        other.AssignedDriver = "";
                        other.DriverDiscordId = "";
                        other.CurrentLoadNumber = "";
                        other.Status = "Unassigned";
                        other.UpdatedUtc = DateTimeOffset.UtcNow;
                        _store.Save(other);
                    }
                }
            }

            truck.AssignedDriver = driverName;
            truck.DriverDiscordId = driverDiscordId;
            truck.CurrentLoadNumber = loadNumber;
            truck.Status = string.IsNullOrWhiteSpace(loadNumber)
                ? (string.IsNullOrWhiteSpace(driverName) ? "Unassigned" : "Active")
                : "Assigned Load";

            truck.UpdatedUtc = DateTimeOffset.UtcNow;

            _store.Save(truck);
            Refresh();
        }



        public FleetCommandTruck? AssignTruckByNumber(string truckNumber, string driverName, string loadNumber, string? explicitDriverDiscordId = null)
        {
            var truck = GetTruckByNumber(truckNumber);
            if (truck == null)
                return null;

            AssignTruck(truck, driverName, loadNumber, explicitDriverDiscordId);
            return GetTruckByNumber(truckNumber) ?? truck;
        }

        public void UnassignDriver(FleetCommandTruck truck)
        {
            truck.AssignedDriver = "";
            truck.DriverDiscordId = "";
            truck.CurrentLoadNumber = "";
            truck.Status = "Unassigned";
            truck.UpdatedUtc = DateTimeOffset.UtcNow;
            SaveTruck(truck);
        }

        public void MarkInspectionComplete(FleetCommandTruck truck)
        {
            truck.LastInspectionDate = DateTime.Today;
            truck.InspectionDueDate = DateTime.Today.AddDays(7);

            if (Same(truck.Status, "Needs Inspection"))
                truck.Status = string.IsNullOrWhiteSpace(truck.CurrentLoadNumber)
                    ? (string.IsNullOrWhiteSpace(truck.AssignedDriver) ? "Unassigned" : "Active")
                    : "Assigned Load";

            truck.UpdatedUtc = DateTimeOffset.UtcNow;
            SaveTruck(truck);
        }

        public void MarkServiceComplete(FleetCommandTruck truck)
        {
            truck.LastServiceDate = DateTime.Today;
            truck.ServiceDueDate = DateTime.Today.AddDays(14);
            truck.HealthPercent = Math.Max(truck.HealthPercent, 95);

            if (Same(truck.Status, "Needs Service"))
                truck.Status = string.IsNullOrWhiteSpace(truck.CurrentLoadNumber)
                    ? (string.IsNullOrWhiteSpace(truck.AssignedDriver) ? "Unassigned" : "Active")
                    : "Assigned Load";

            truck.UpdatedUtc = DateTimeOffset.UtcNow;
            SaveTruck(truck);
        }

        public void DeleteSelected()
        {
            if (SelectedTruck == null) return;
            _store.Delete(SelectedTruck.Id);
            Refresh();
        }

        private void UpdateDriverOptions(List<FleetCommandTruck> all)
        {
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var d in DispatchService.Drivers)
            {
                var name = CleanDriverName(d);
                if (!string.IsNullOrWhiteSpace(name))
                    set.Add(name);
            }

            foreach (var d in DiscordIdentityStore.LoadAll())
            {
                var name = CleanDriverName(d.DiscordUsername);
                if (!string.IsNullOrWhiteSpace(name))
                    set.Add(name);
            }

            foreach (var d in all.Select(x => x.AssignedDriver))
            {
                var name = CleanDriverName(d);
                if (!string.IsNullOrWhiteSpace(name))
                    set.Add(name);
            }

            DriverOptions.Clear();
            foreach (var item in set)
                DriverOptions.Add(item);
        }

        private void UpdateLoadOptions(List<FleetCommandTruck> all)
        {
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var job in DispatchService.Jobs)
                if (!string.IsNullOrWhiteSpace(job.LoadNumber))
                    set.Add(job.LoadNumber.Trim());

            foreach (var load in all.Select(x => x.CurrentLoadNumber))
                if (!string.IsNullOrWhiteSpace(load))
                    set.Add(load.Trim());

            LoadOptions.Clear();
            foreach (var item in set)
                LoadOptions.Add(item);
        }

        private void UpdateTopStats(List<FleetCommandTruck> all)
        {
            TotalFleetText = all.Count.ToString();
            AssignedText = all.Count(x => !string.IsNullOrWhiteSpace(x.AssignedDriver)).ToString();
            UnassignedText = all.Count(x => string.IsNullOrWhiteSpace(x.AssignedDriver)).ToString();
            NeedsServiceText = all.Count(IsNeedsService).ToString();
            NeedsInspectionText = all.Count(IsNeedsInspection).ToString();

            ActiveDriversText = all
                .Where(x => !string.IsNullOrWhiteSpace(x.AssignedDriver))
                .Where(IsTelemetryActiveLike)
                .Select(x => x.AssignedDriver.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count()
                .ToString();

            UpdateReceiptTicketStats();
        }

        private void UpdateReceiptTicketStats()
        {
            try
            {
                var receipts = TelemetryExpenseReceiptStore.LoadAll();
                var fuel = receipts.Count(x => Same(x.EventType, "Fuel"));
                var tolls = receipts.Count(x => Same(x.EventType, "Toll"));
                var tickets = receipts.Count(x => Same(x.EventType, "Ticket"));

                ReceiptTicketsText = $"F:{fuel} T:{tolls} Tk:{tickets}";
            }
            catch
            {
                ReceiptTicketsText = "0";
            }
        }

        private static IEnumerable<FleetCommandTruck> ApplyFilter(IEnumerable<FleetCommandTruck> source, string filter)
        {
            filter = (filter ?? "All").Trim();

            return filter switch
            {
                "Assigned Load" => source.Where(x => !string.IsNullOrWhiteSpace(x.CurrentLoadNumber)),
                "Driving" => source.Where(x => x.IsDriving || Same(x.Status, "Driving")),
                "Active" => source.Where(IsTelemetryActiveLike),
                "Needs Service" => source.Where(IsNeedsService),
                "Needs Inspection" => source.Where(IsNeedsInspection),
                "Unassigned" => source.Where(x => string.IsNullOrWhiteSpace(x.AssignedDriver) || Same(x.Status, "Unassigned")),
                "Out of Service" => source.Where(x => Same(x.Status, "Out of Service")),
                "Inactive" => source.Where(x => Same(x.Status, "Inactive")),
                _ => source
            };
        }

        private static bool IsNeedsService(FleetCommandTruck x)
        {
            if (Same(x.Status, "Needs Service")) return true;
            if (x.ServiceDueDate.HasValue && x.ServiceDueDate.Value.Date <= DateTime.Today) return true;
            return x.HealthPercent <= 60;
        }

        private static bool IsNeedsInspection(FleetCommandTruck x)
        {
            if (Same(x.Status, "Needs Inspection")) return true;
            return x.InspectionDueDate.HasValue && x.InspectionDueDate.Value.Date <= DateTime.Today;
        }

        private static bool IsTelemetryActiveLike(FleetCommandTruck x)
        {
            if (x == null) return false;
            if (x.IsDriving) return true;
            if (Same(x.Status, "Active")) return true;
            if (Same(x.Status, "Driving")) return true;
            return false;
        }


        private static string BuildDestination(string? city, string? company)
        {
            city = (city ?? "").Trim();
            company = (company ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(city) && !string.IsNullOrWhiteSpace(company))
                return $"{city} - {company}";

            return FirstNonEmpty(city, company);
        }

        private static string BuildWarnings(double fuel, double truckDamage, double trailerDamage)
        {
            var parts = new List<string>();
            if (fuel > 0 && fuel <= 10) parts.Add("LOW FUEL");
            if (truckDamage >= 15) parts.Add("TRUCK DAMAGE");
            if (trailerDamage >= 15) parts.Add("TRAILER DAMAGE");
            return parts.Count == 0 ? "None" : string.Join(" • ", parts);
        }

        private static string ReadString(object? obj, string name)
        {
            try
            {
                if (obj == null) return "";
                var prop = obj.GetType().GetProperty(name);
                return prop?.GetValue(obj)?.ToString()?.Trim() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static double ReadDouble(object? obj, string name, double fallback)
        {
            var raw = ReadString(obj, name).Replace("%", "").Replace(",", "");
            if (double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
                return value;
            if (double.TryParse(raw, out value))
                return value;
            return fallback;
        }

        private static string CleanDriverName(string? value)
        {
            var s = (value ?? "").Trim();
            return IsGenericDriverLabel(s) ? "" : s;
        }

        private static bool IsGenericDriverLabel(string? value)
        {
            var s = (value ?? "").Trim();
            return string.IsNullOrWhiteSpace(s) ||
                   Same(s, "Driver") ||
                   Same(s, "Unknown Driver") ||
                   Same(s, "Unknown") ||
                   Same(s, "Unassigned");
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                var text = (value ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }

            return "";
        }

        private static bool Contains(string? source, string search)
            => (source ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool Same(string? a, string? b)
            => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        private static double ClampToPercent(double value)
        {
            if (value < 0) return 0;
            if (value > 100) return 100;
            return value;
        }

        private static FleetCommandTruckRow ToRow(FleetCommandTruck x)
        {
            var isNeedsService = IsNeedsService(x);
            var isNeedsInspection = IsNeedsInspection(x);
            var isActive = IsTelemetryActiveLike(x);
            var isInactive = Same(x.Status, "Inactive");
            var effectiveStatus = GetEffectiveStatus(x, isActive, isNeedsService, isNeedsInspection);
            var updatedUtc = x.UpdatedUtc.UtcDateTime;
            var speedMph = ReadDouble(x, "SpeedMph", ReadDouble(x, "CurrentSpeedMph", 0));
            var truckDamage = ReadDouble(x, "TruckDamagePercent", ReadDouble(x, "DamagePercent", Math.Max(0, 100 - x.HealthPercent)));
            var trailerDamage = ReadDouble(x, "TrailerDamagePercent", 0);
            if (truckDamage < 2) truckDamage = 0;
            if (trailerDamage < 2) trailerDamage = 0;
            var trailerName = FirstNonEmpty(ReadString(x, "TrailerName"), "--");
            var cargoName = FirstNonEmpty(ReadString(x, "CargoName"), "--");
            var destination = FirstNonEmpty(
                ReadString(x, "Destination"),
                BuildDestination(ReadString(x, "DestinationCity"), ReadString(x, "DestinationCompany")),
                "--");
            var remainingMiles = ReadDouble(x, "RemainingMiles", 0);
            var warnings = FirstNonEmpty(ReadString(x, "Warnings"), ReadString(x, "LiveWarnings"), BuildWarnings(x.FuelPercent, truckDamage, trailerDamage));
            var recentlyUpdated = (DateTimeOffset.UtcNow - x.UpdatedUtc).TotalMinutes <= 2;

            return new FleetCommandTruckRow
            {
                Id = x.Id,
                DriverDiscordId = x.DriverDiscordId ?? "",
                TruckNumber = string.IsNullOrWhiteSpace(x.TruckNumber) ? "--" : x.TruckNumber,
                PlateNumber = string.IsNullOrWhiteSpace(x.PlateNumber) ? "--" : x.PlateNumber,
                TruckName = string.IsNullOrWhiteSpace(x.TruckName) ? "--" : x.TruckName,
                Model = string.IsNullOrWhiteSpace(x.Model) ? "--" : x.Model,
                AssignedDriver = string.IsNullOrWhiteSpace(x.AssignedDriver) ? "Unassigned" : x.AssignedDriver,
                LoadNumber = string.IsNullOrWhiteSpace(x.CurrentLoadNumber) ? "--" : x.CurrentLoadNumber,
                Status = effectiveStatus,
                Location = string.IsNullOrWhiteSpace(x.Location) ? "--" : x.Location,
                Speed = speedMph > 0 ? $"{speedMph:0} mph" : "--",
                TruckDamage = $"{ClampToPercent(truckDamage):0}%",
                TrailerDamage = $"{ClampToPercent(trailerDamage):0}%",
                TrailerName = trailerName,
                CargoName = cargoName,
                Destination = destination,
                RemainingMiles = remainingMiles > 0 ? $"{remainingMiles:0} mi" : "--",
                Warnings = string.IsNullOrWhiteSpace(warnings) ? "None" : warnings,
                Condition = $"{Math.Clamp(x.HealthPercent, 0, 100)}%",
                Fuel = $"{ClampToPercent(x.FuelPercent):0}%",
                HealthPercent = Math.Clamp(x.HealthPercent, 0, 100).ToString("0"),
                FuelPercent = ClampToPercent(x.FuelPercent).ToString("0.##"),
                OdometerMiles = x.OdometerMiles <= 0 ? "0" : x.OdometerMiles.ToString("0.##"),
                Odometer = x.OdometerMiles <= 0 ? "--" : x.OdometerMiles.ToString("N0"),
                ServiceDue = x.ServiceDueDate?.ToString("MM/dd/yyyy") ?? "--",
                InspectionDue = x.InspectionDueDate?.ToString("MM/dd/yyyy") ?? "--",
                LastUpdated = GetLastUpdatedText(x.UpdatedUtc),
                LastUpdatedDisplay = GetLastUpdatedText(x.UpdatedUtc),
                LastUpdatedUtcSort = updatedUtc,
                IsRecentlyUpdated = recentlyUpdated,
                IsActive = isActive,
                IsInactive = isInactive,
                SortPriority = GetSortPriority(effectiveStatus, recentlyUpdated),
                ConditionBrush = GetConditionBrush(x.HealthPercent),
                ServiceBrush = isNeedsService ? Brushes.IndianRed : Brushes.LimeGreen,
                InspectionBrush = isNeedsInspection ? Brushes.IndianRed : Brushes.DeepSkyBlue,
                StatusBrush = GetStatusBrush(effectiveStatus),
                RowBackground = GetRowBackground(effectiveStatus, recentlyUpdated),
                RowBorderBrush = GetRowBorderBrush(effectiveStatus, recentlyUpdated)
            };
        }

        private static string GetEffectiveStatus(FleetCommandTruck x, bool isActive, bool isNeedsService, bool isNeedsInspection)
        {
            if (Same(x.Status, "Out of Service")) return "Out of Service";
            if (isActive) return "Active";
            if (isNeedsService) return "Needs Service";
            if (isNeedsInspection) return "Needs Inspection";
            if (!string.IsNullOrWhiteSpace(x.CurrentLoadNumber)) return "Assigned Load";
            if (Same(x.Status, "Inactive")) return "Inactive";
            if (!string.IsNullOrWhiteSpace(x.AssignedDriver)) return "Assigned";
            return "Unassigned";
        }

        private static int GetSortPriority(string status, bool recentlyUpdated)
        {
            var basePriority = status switch
            {
                "Active" => 700,
                "Needs Service" => 600,
                "Needs Inspection" => 500,
                "Assigned Load" => 400,
                "Assigned" => 300,
                "Out of Service" => 200,
                "Inactive" => 100,
                _ => 0
            };

            if (recentlyUpdated)
                basePriority += 25;

            return basePriority;
        }

        private static string GetLastUpdatedText(DateTimeOffset updatedUtc)
        {
            var age = DateTimeOffset.UtcNow - updatedUtc;

            if (age.TotalSeconds < 30) return "Just now";
            if (age.TotalMinutes < 1) return $"{Math.Max(1, (int)Math.Round(age.TotalSeconds))} sec ago";
            if (age.TotalMinutes < 60) return $"{Math.Max(1, (int)Math.Round(age.TotalMinutes))} min ago";

            return updatedUtc.LocalDateTime.ToString("MM/dd HH:mm");
        }

        private static Brush GetConditionBrush(int health)
        {
            if (health >= 85) return Brushes.LimeGreen;
            if (health >= 60) return Brushes.Goldenrod;
            return Brushes.IndianRed;
        }

        private static Brush GetStatusBrush(string status)
        {
            return status switch
            {
                "Active" => Brushes.LimeGreen,
                "Needs Service" => Brushes.OrangeRed,
                "Needs Inspection" => Brushes.IndianRed,
                "Assigned Load" => Brushes.DeepSkyBlue,
                "Assigned" => Brushes.SteelBlue,
                "Out of Service" => Brushes.DarkRed,
                "Inactive" => Brushes.DimGray,
                _ => Brushes.Gray
            };
        }

        private static Brush GetRowBackground(string status, bool recentlyUpdated)
        {
            if (Same(status, "Active"))
                return recentlyUpdated ? new SolidColorBrush(Color.FromRgb(26, 58, 26)) : new SolidColorBrush(Color.FromRgb(20, 42, 20));

            if (Same(status, "Needs Service")) return new SolidColorBrush(Color.FromRgb(52, 28, 18));
            if (Same(status, "Needs Inspection")) return new SolidColorBrush(Color.FromRgb(58, 18, 18));
            if (Same(status, "Assigned Load")) return new SolidColorBrush(Color.FromRgb(18, 30, 46));
            if (Same(status, "Inactive")) return new SolidColorBrush(Color.FromRgb(30, 30, 30));

            return new SolidColorBrush(Color.FromRgb(20, 20, 20));
        }

        private static Brush GetRowBorderBrush(string status, bool recentlyUpdated)
        {
            if (Same(status, "Active")) return recentlyUpdated ? Brushes.LawnGreen : Brushes.ForestGreen;
            if (Same(status, "Needs Service")) return Brushes.Orange;
            if (Same(status, "Needs Inspection")) return Brushes.IndianRed;
            if (Same(status, "Assigned Load")) return Brushes.DeepSkyBlue;
            if (Same(status, "Inactive")) return Brushes.DimGray;

            return Brushes.Transparent;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public sealed class FleetCommandTruckRow : INotifyPropertyChanged
        {
            private string _truckNumber = "";
            private string _plateNumber = "";
            private string _truckName = "";
            private string _model = "";
            private string _assignedDriver = "";
            private string _loadNumber = "";
            private string _status = "";
            private string _location = "";
            private string _fuelPercent = "";
            private string _healthPercent = "";
            private string _odometerMiles = "";
            private string _speed = "";
            private string _truckDamage = "";
            private string _trailerDamage = "";
            private string _trailerName = "";
            private string _cargoName = "";
            private string _destination = "";
            private string _remainingMiles = "";
            private string _warnings = "";

            public string Id { get; set; } = "";
            public string DriverDiscordId { get; set; } = "";

            public string TruckNumber { get => _truckNumber; set { _truckNumber = value ?? ""; OnPropertyChanged(); } }
            public string PlateNumber { get => _plateNumber; set { _plateNumber = value ?? ""; OnPropertyChanged(); } }
            public string TruckName { get => _truckName; set { _truckName = value ?? ""; OnPropertyChanged(); } }
            public string Model { get => _model; set { _model = value ?? ""; OnPropertyChanged(); } }
            public string AssignedDriver { get => _assignedDriver; set { _assignedDriver = value ?? ""; OnPropertyChanged(); } }
            public string LoadNumber { get => _loadNumber; set { _loadNumber = value ?? ""; OnPropertyChanged(); } }
            public string Status { get => _status; set { _status = value ?? ""; OnPropertyChanged(); } }
            public string Location { get => _location; set { _location = value ?? ""; OnPropertyChanged(); } }
            public string FuelPercent { get => _fuelPercent; set { _fuelPercent = value ?? ""; OnPropertyChanged(); } }
            public string HealthPercent { get => _healthPercent; set { _healthPercent = value ?? ""; OnPropertyChanged(); } }
            public string OdometerMiles { get => _odometerMiles; set { _odometerMiles = value ?? ""; OnPropertyChanged(); } }
            public string Speed { get => _speed; set { _speed = value ?? ""; OnPropertyChanged(); } }
            public string TruckDamage { get => _truckDamage; set { _truckDamage = value ?? ""; OnPropertyChanged(); } }
            public string TrailerDamage { get => _trailerDamage; set { _trailerDamage = value ?? ""; OnPropertyChanged(); } }
            public string TrailerName { get => _trailerName; set { _trailerName = value ?? ""; OnPropertyChanged(); } }
            public string CargoName { get => _cargoName; set { _cargoName = value ?? ""; OnPropertyChanged(); } }
            public string Destination { get => _destination; set { _destination = value ?? ""; OnPropertyChanged(); } }
            public string RemainingMiles { get => _remainingMiles; set { _remainingMiles = value ?? ""; OnPropertyChanged(); } }
            public string Warnings { get => _warnings; set { _warnings = value ?? ""; OnPropertyChanged(); } }

            public string Condition { get; set; } = "";
            public string Fuel { get; set; } = "";
            public string Odometer { get; set; } = "";
            public string ServiceDue { get; set; } = "";
            public string InspectionDue { get; set; } = "";
            public string LastUpdated { get; set; } = "";
            public string LastUpdatedDisplay { get; set; } = "";
            public DateTime LastUpdatedUtcSort { get; set; }
            public bool IsRecentlyUpdated { get; set; }
            public bool IsActive { get; set; }
            public bool IsInactive { get; set; }
            public int SortPriority { get; set; }
            public Brush ConditionBrush { get; set; } = Brushes.Gray;
            public Brush ServiceBrush { get; set; } = Brushes.Gray;
            public Brush InspectionBrush { get; set; } = Brushes.Gray;
            public Brush StatusBrush { get; set; } = Brushes.Gray;
            public Brush RowBackground { get; set; } = Brushes.Transparent;
            public Brush RowBorderBrush { get; set; } = Brushes.Transparent;

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string? name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}