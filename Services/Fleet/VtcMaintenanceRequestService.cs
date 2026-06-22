using OverWatchELD.Models;
using OverWatchELD.Stores;
using System;
using System.Linq;

namespace OverWatchELD.Services.Fleet
{
    public static class VtcMaintenanceRequestService
    {
        public static void LogRequest(
            string unit,
            string truckName,
            string driver,
            string issue,
            string notes)
        {
            var state =
                VtcMaintenanceStore.Load()
                ?? new VtcMaintenanceState();

            var truck = state.Trucks.FirstOrDefault(t =>
                string.Equals(
                    t.UnitNumber,
                    unit,
                    StringComparison.OrdinalIgnoreCase));

            if (truck == null)
                return;

            truck.CurrentIssue = issue;
            truck.CurrentIssueSeverity = "Request";

            truck.ServiceHistory.Add(new VtcServiceRecord
            {
                ServiceType = "Maintenance Requested",
                Notes = notes,
                OdometerMiles = truck.OdometerMiles,
                CompletedBy = driver
            });

            state.Trucks = state.Trucks.ToList();

            VtcMaintenanceStore.Save(state);
        }

        public static void ClearMalfunctions(
            string unit,
            string driver,
            string notes)
        {
            var state =
                VtcMaintenanceStore.Load()
                ?? new VtcMaintenanceState();

            var truck = state.Trucks.FirstOrDefault(t =>
                string.Equals(
                    t.UnitNumber,
                    unit,
                    StringComparison.OrdinalIgnoreCase));

            if (truck == null)
                return;

            truck.CurrentIssue = "";
            truck.CurrentIssueSeverity = "";
            truck.OutOfService = false;

            foreach (var report in truck.DamageReports.Where(x => !x.Resolved))
                report.Resolved = true;

            truck.ServiceHistory.Add(new VtcServiceRecord
            {
                ServiceType = "Malfunction Cleared",
                Notes = notes,
                OdometerMiles = truck.OdometerMiles,
                CompletedBy = driver
            });

            state.Trucks = state.Trucks.ToList();

            VtcMaintenanceStore.Save(state);
        }
    }
}