using OverWatchELD.Models.Fleet;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OverWatchELD.Services.Fleet
{
    /// <summary>
    /// Unified Fleet Command projection service for the existing
    /// OverWatchELD.Models.Fleet.FleetTruck model.
    /// </summary>
    public class FleetCommandService
    {
        public List<FleetCommandRow> BuildRows(
            IEnumerable<FleetTruck> fleetTrucks,
            IEnumerable<FleetDriverAssignment>? assignments = null,
            IEnumerable<DispatchLoadSnapshot>? activeLoads = null)
        {
            assignments ??= Enumerable.Empty<FleetDriverAssignment>();
            activeLoads ??= Enumerable.Empty<DispatchLoadSnapshot>();

            var rows = new List<FleetCommandRow>();

            foreach (var truck in fleetTrucks)
            {
                var truckKey = GetTruckKey(truck);

                var assign = assignments.FirstOrDefault(a =>
                    string.Equals(a.TruckId, truckKey, StringComparison.OrdinalIgnoreCase));

                var driverName = FirstNonEmpty(
                    assign?.DriverName,
                    truck.AssignedDriver,
                    "Unassigned");

                var activeLoad = activeLoads.FirstOrDefault(l =>
                    (!string.IsNullOrWhiteSpace(assign?.DriverId) &&
                     string.Equals(l.DriverId, assign.DriverId, StringComparison.OrdinalIgnoreCase))
                    ||
                    string.Equals(l.TruckId, truckKey, StringComparison.OrdinalIgnoreCase));

                var row = new FleetCommandRow
                {
                    TruckId = truckKey,
                    DriverId = assign?.DriverId ?? "",
                    DriverName = driverName,
                    CurrentLoadNumber = activeLoad?.LoadNumber ?? "",
                    CurrentLocation = FirstNonEmpty(activeLoad?.CurrentLocation, truck.LastKnownLocation),
                    Status = ResolveStatus(truck, activeLoad),
                    ConditionPercent = ClampPercent(GetConditionPercent(truck)),
                    FuelPercent = ClampPercent(GetFuelPercent(truck)),
                    OdometerMiles = Math.Max(0, truck.OdometerMiles),
                    NeedsService = truck.NeedsService
                };

                rows.Add(row);
            }

            return rows
                .OrderByDescending(r => r.NeedsService)
                .ThenBy(r => r.TruckId)
                .ToList();
        }

        public List<FleetCommandAlert> BuildAlerts(IEnumerable<FleetCommandRow> rows)
        {
            var alerts = new List<FleetCommandAlert>();

            foreach (var row in rows)
            {
                if (row.NeedsService)
                {
                    alerts.Add(new FleetCommandAlert
                    {
                        TruckId = row.TruckId,
                        DriverName = row.DriverName,
                        Severity = "Warning",
                        Message = $"Truck {row.TruckId} requires service soon."
                    });
                }

                if (row.ConditionPercent <= 70)
                {
                    alerts.Add(new FleetCommandAlert
                    {
                        TruckId = row.TruckId,
                        DriverName = row.DriverName,
                        Severity = "Critical",
                        Message = $"Truck {row.TruckId} condition is low ({row.ConditionPercent:0}%)."
                    });
                }

                if (row.FuelPercent <= 15)
                {
                    alerts.Add(new FleetCommandAlert
                    {
                        TruckId = row.TruckId,
                        DriverName = row.DriverName,
                        Severity = "Warning",
                        Message = $"Truck {row.TruckId} is low on fuel ({row.FuelPercent:0}%)."
                    });
                }
            }

            return alerts;
        }

        private static string ResolveStatus(FleetTruck truck, DispatchLoadSnapshot? load)
        {
            if (truck.NeedsService) return "Needs Service";
            if (load != null && !string.IsNullOrWhiteSpace(load.LoadNumber)) return "Assigned Load";
            if (truck.IsDriving) return "Driving";
            if (truck.IsOnline) return "Idle";
            if (!string.IsNullOrWhiteSpace(truck.AssignedDriver)) return "Assigned";
            return "Offline";
        }

        private static string GetTruckKey(FleetTruck truck)
        {
            return FirstNonEmpty(truck.Plate, truck.Nickname, truck.MakeModel, "UNIT");
        }

        private static double GetFuelPercent(FleetTruck truck)
        {
            if (truck.FuelPercent > 0)
                return truck.FuelPercent;

            return truck.FuelPct;
        }

        private static double GetConditionPercent(FleetTruck truck)
        {
            if (truck.ConditionPercent > 0)
                return truck.ConditionPercent;

            var derivedHealth = 100.0 - new[]
            {
                truck.EngineDamagePct,
                truck.TransmissionDamagePct,
                truck.CabinDamagePct,
                truck.ChassisDamagePct,
                truck.WheelsDamagePct
            }.Max();

            return derivedHealth;
        }

        private static double ClampPercent(double value)
        {
            if (value < 0) return 0;
            if (value > 100) return 100;
            return value;
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

    /// <summary>
    /// Additive helper DTO so this file compiles clean without forcing you
    /// to rename your existing dispatch models first.
    /// Replace later if you already have an equivalent load snapshot model.
    /// </summary>
    public class DispatchLoadSnapshot
    {
        public string LoadNumber { get; set; } = "";
        public string DriverId { get; set; } = "";
        public string TruckId { get; set; } = "";
        public string CurrentLocation { get; set; } = "";
    }

    /// <summary>
    /// Additive helper DTO for truck-driver assignment.
    /// Replace later if your project already has one.
    /// </summary>
    public class FleetDriverAssignment
    {
        public string TruckId { get; set; } = "";
        public string DriverId { get; set; } = "";
        public string DriverName { get; set; } = "";
    }
}