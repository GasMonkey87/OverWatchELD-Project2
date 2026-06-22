using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Media;
using OverWatchELD.ViewModels;

namespace OverWatchELD.Services.Fleet
{
    public sealed class VtcFleetSnapshotService
    {
        public sealed class FleetSnapshotRecord
        {
            public string TruckId { get; set; } = "";
            public string Truck { get; set; } = "";
            public string Driver { get; set; } = "";
            public bool IsCurrentTruck { get; set; }
            public string ActionText { get; set; } = "Use Truck";
            public string Location { get; set; } = "";
            public string Status { get; set; } = "";
            public string ServiceDue { get; set; } = "";
            public string InspectionStatus { get; set; } = "";
            public string Health { get; set; } = "";
            public Brush StatusBrush { get; set; } = Brushes.Gray;
            public Brush HealthBrush { get; set; } = Brushes.Gray;
        }

        public List<FleetSnapshotRecord> BuildSnapshot(IReadOnlyList<VtcRosterViewModel.RosterDriverRow> rosterRows)
        {
            try
            {
                var store = new FleetCommandStore();
                var trucks = store.LoadAll()
                    .OrderByDescending(t => IsActiveStatus(t.Status))
                    .ThenByDescending(t => t.UpdatedUtc)
                    .ToList();

                if (trucks.Count > 0)
                    return BuildFromFleetCommand(rosterRows, trucks);

                return BuildFromRosterFallback(rosterRows);
            }
            catch
            {
                return BuildFromRosterFallback(rosterRows);
            }
        }

        private static List<FleetSnapshotRecord> BuildFromFleetCommand(
            IReadOnlyList<VtcRosterViewModel.RosterDriverRow> rosterRows,
            List<Models.Fleet.FleetCommandTruck> trucks)
        {
            var results = new List<FleetSnapshotRecord>();
            var activeStore = new ActiveTruckSelectionStore();

            foreach (var truck in trucks.Take(25))
            {
                var roster = FindRosterForTruck(rosterRows, truck);

                var driver = FirstNonEmpty(
                    truck.AssignedDriver,
                    roster?.Driver,
                    "Unknown Driver");

                var status = FirstNonEmpty(
                    truck.Status,
                    roster?.Status,
                    "Unknown");

                var location = FirstNonEmpty(
                    truck.Location,
                    roster?.Location,
                    "--");

                if (!location.StartsWith("Location:", StringComparison.OrdinalIgnoreCase))
                    location = $"Location: {location}";

                var truckName = FirstNonEmpty(
                    truck.TruckName,
                    truck.Model,
                    truck.TruckNumber,
                    truck.PlateNumber,
                    "Unknown Truck");

                var health = BuildHealthText(truck.HealthPercent, status);
                var isCurrentTruck = activeStore.IsActiveTruck(driver, truck.DriverDiscordId, truck.Id);

                results.Add(new FleetSnapshotRecord
                {
                    TruckId = truck.Id,
                    Truck = truckName,
                    IsCurrentTruck = isCurrentTruck,
                    ActionText = isCurrentTruck ? "Active Truck" : "Use Truck",
                    Driver = driver,
                    Location = location,
                    Status = status,
                    ServiceDue = BuildServiceDueText(truck),
                    InspectionStatus = BuildInspectionText(truck),
                    Health = health,
                    StatusBrush = GetStatusBrush(status),
                    HealthBrush = GetHealthBrush(health)
                });
            }

            return results
                .OrderByDescending(x => x.IsCurrentTruck)
                .ThenByDescending(x => StatusLooksOnline(x.Status))
                .ThenBy(x => x.Truck)
                .Take(12)
                .ToList();
        }

        private static VtcRosterViewModel.RosterDriverRow? FindRosterForTruck(
            IReadOnlyList<VtcRosterViewModel.RosterDriverRow> rosterRows,
            Models.Fleet.FleetCommandTruck truck)
        {
            return rosterRows.FirstOrDefault(r =>
                Same(r.Driver, truck.AssignedDriver) ||
                ContainsEither(r.Driver, truck.AssignedDriver) ||
                Same(r.DiscordUserId, truck.DriverDiscordId) ||
                Same(r.Truck, truck.TruckName) ||
                Same(r.Truck, truck.Model) ||
                Same(r.Truck, truck.TruckNumber));
        }

        private static List<FleetSnapshotRecord> BuildFromRosterFallback(IReadOnlyList<VtcRosterViewModel.RosterDriverRow> rosterRows)
        {
            return rosterRows
                .Where(r => !string.IsNullOrWhiteSpace(r.Truck) && r.Truck.Trim() != "-")
                .GroupBy(r => NormalizeTruckKey(r.Truck), StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var primary = g
                        .OrderByDescending(x => StatusLooksOnline(x.Status))
                        .ThenBy(x => SafeText(x.Driver, "Unknown Driver"))
                        .First();

                    var status = FirstNonEmpty(SafeTrim(primary.Status), "Unknown");
                    var health = BuildFallbackHealth(status);

                    return new FleetSnapshotRecord
                    {
                        TruckId = "",
                        Truck = NormalizeTruckKey(primary.Truck),
                        IsCurrentTruck = false,
                        ActionText = "Use Truck",
                        Driver = SafeText(primary.Driver, "Unknown Driver"),
                        Location = string.IsNullOrWhiteSpace(primary.Location) ? "Location: --" : $"Location: {primary.Location.Trim()}",
                        Status = status,
                        ServiceDue = BuildFallbackServiceDue(status),
                        InspectionStatus = BuildFallbackInspection(status),
                        Health = health,
                        StatusBrush = GetStatusBrush(status),
                        HealthBrush = GetHealthBrush(health)
                    };
                })
                .OrderByDescending(x => StatusLooksOnline(x.Status))
                .ThenBy(x => x.Truck)
                .Take(12)
                .ToList();
        }

        private static string BuildServiceDueText(Models.Fleet.FleetCommandTruck truck)
        {
            if (truck.ServiceDueDate.HasValue)
            {
                if (truck.ServiceDueDate.Value.Date <= DateTime.Today)
                    return "Service Due: Overdue";

                return $"Service Due: {truck.ServiceDueDate.Value:MM/dd/yyyy}";
            }

            return IsActiveStatus(truck.Status)
                ? "Service Due: OK"
                : "Service Due: Review";
        }

        private static string BuildInspectionText(Models.Fleet.FleetCommandTruck truck)
        {
            if (truck.InspectionDueDate.HasValue)
            {
                if (truck.InspectionDueDate.Value.Date <= DateTime.Today)
                    return "Inspection: Attention Needed";

                return $"Inspection: {truck.InspectionDueDate.Value:MM/dd/yyyy}";
            }

            return IsActiveStatus(truck.Status)
                ? "Inspection: OK"
                : "Inspection: Review";
        }

        private static string BuildHealthText(int healthPercent, string? status)
        {
            healthPercent = Math.Clamp(healthPercent, 0, 100);

            if (healthPercent >= 80)
                return $"Healthy ({healthPercent}%)";

            if (healthPercent >= 55)
                return $"Due Soon ({healthPercent}%)";

            return $"Needs Check ({healthPercent}%)";
        }

        private static string BuildFallbackHealth(string? status)
        {
            if (StatusLooksOnline(status))
                return "Healthy";

            if (IsIdleish(status))
                return "Due Soon";

            return "Needs Check";
        }

        private static string BuildFallbackServiceDue(string? status)
        {
            if (StatusLooksOnline(status))
                return "Service Due: OK";

            if (IsIdleish(status))
                return "Service Due: Review";

            return "Service Due: Inspect now";
        }

        private static string BuildFallbackInspection(string? status)
        {
            if (StatusLooksOnline(status))
                return "Inspection: OK";

            if (IsIdleish(status))
                return "Inspection: Review Soon";

            return "Inspection: Attention Needed";
        }

        private static string NormalizeTruckKey(string? truck)
            => string.IsNullOrWhiteSpace(truck) ? "" : truck.Trim();

        private static string SafeTrim(string? text)
            => string.IsNullOrWhiteSpace(text) ? "" : text.Trim();

        private static string SafeText(string? text, string fallback)
            => string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var v in values)
                if (!string.IsNullOrWhiteSpace(v))
                    return v.Trim();

            return "";
        }

        private static bool Same(string? a, string? b)
        {
            return !string.IsNullOrWhiteSpace(a) &&
                   !string.IsNullOrWhiteSpace(b) &&
                   string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsEither(string? a, string? b)
        {
            a = (a ?? "").Trim();
            b = (b ?? "").Trim();

            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                return false;

            return a.Contains(b, StringComparison.OrdinalIgnoreCase) ||
                   b.Contains(a, StringComparison.OrdinalIgnoreCase);
        }

        private static bool StatusLooksOnline(string? status)
        {
            if (string.IsNullOrWhiteSpace(status)) return false;

            var s = status.Trim();

            return s.Equals("online", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("active", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("driving", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("on duty", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("onduty", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsActiveStatus(string? status)
            => StatusLooksOnline(status);

        private static bool IsIdleish(string? status)
        {
            if (string.IsNullOrWhiteSpace(status)) return false;

            var s = status.Trim();

            return s.Equals("idle", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("parked", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("break", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("inactive", StringComparison.OrdinalIgnoreCase);
        }

        private static Brush GetStatusBrush(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return Brushes.Gray;

            if (StatusLooksOnline(status))
                return Brushes.LimeGreen;

            if (IsIdleish(status))
                return Brushes.Goldenrod;

            return Brushes.IndianRed;
        }

        private static Brush GetHealthBrush(string? health)
        {
            if (string.IsNullOrWhiteSpace(health))
                return Brushes.Gray;

            var s = health.Trim();

            if (s.StartsWith("Healthy", StringComparison.OrdinalIgnoreCase))
                return Brushes.ForestGreen;

            if (s.StartsWith("Due Soon", StringComparison.OrdinalIgnoreCase))
                return Brushes.Goldenrod;

            if (s.StartsWith("Needs Check", StringComparison.OrdinalIgnoreCase))
                return Brushes.IndianRed;

            return Brushes.Gray;
        }
    }
}