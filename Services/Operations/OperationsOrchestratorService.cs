using OverWatchELD.Models.Dispatch;
using OverWatchELD.Services.Analytics;
using OverWatchELD.Services.Dispatch;
using OverWatchELD.Services.Economy;
using OverWatchELD.Services.Performance;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OverWatchELD.Services.Operations
{
    public sealed class OperationsSyncResult
    {
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
        public DateTime CompletedUtc { get; set; } = DateTime.UtcNow;
        public bool Success { get; set; } = true;
        public List<string> Completed { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public List<string> Errors { get; set; } = new();

        public string Summary =>
            $"Completed {Completed.Count} operation(s), warnings {Warnings.Count}, errors {Errors.Count}.";
    }

    public static class OperationsOrchestratorService
    {
        public static OperationsSyncResult RunFullSync()
        {
            var result = new OperationsSyncResult
            {
                StartedUtc = DateTime.UtcNow
            };

            Step(result, "Sync real delivered loads + payroll", () =>
            {
                FleetEconomyIntegration.TrySyncDeliveredLoads();
            });

            Step(result, "Sync contract delivered loads", () =>
            {
                DispatchContractAtsIntegrationService.SyncDeliveredContractLoads();
            });

            Step(result, "Process fuel / maintenance telemetry", () =>
            {
                FuelMaintenanceAutomationService.ProcessCurrentTelemetryIfAvailable();
            });

            Step(result, "Post daily garage income", () =>
            {
                // Safe to call; the garage service blocks duplicate daily postings.
                GarageEconomyService.PostDailyGarageIncome();
            });

            Step(result, "Refresh truck profitability + leaderboards", () =>
            {
                _ = TruckProfitabilityLeaderboardService.BuildTruckProfitability();
                _ = TruckProfitabilityLeaderboardService.BuildDriverLeaderboard();
            });

            Step(result, "Refresh driver safety / performance scores", () =>
            {
                _ = DriverSafetyPerformanceService.BuildLeaderboard();
            });

            Step(result, "Refresh fleet analytics snapshot", () =>
            {
                _ = FleetAnalyticsService.BuildSnapshot();
            });

            result.CompletedUtc = DateTime.UtcNow;
            result.Success = result.Errors.Count == 0;
            return result;
        }

        public static OperationsSyncResult RunPostDeliveryOperations(DispatchJob? job)
        {
            var result = new OperationsSyncResult
            {
                StartedUtc = DateTime.UtcNow
            };

            if (job == null)
            {
                result.Warnings.Add("No dispatch job was supplied for post-delivery operations.");
                result.CompletedUtc = DateTime.UtcNow;
                return result;
            }

            Step(result, "Post real driver economy + payroll for delivered load", () =>
            {
                RealDriverEconomyPayrollService.TryPostDeliveredLoadEconomy(job);
            });

            Step(result, "Sync delivered contract progress", () =>
            {
                DispatchContractAtsIntegrationService.SyncDeliveredContractLoads();
            });

            Step(result, "Process fuel / maintenance telemetry", () =>
            {
                FuelMaintenanceAutomationService.ProcessCurrentTelemetryIfAvailable();
            });

            Step(result, "Refresh driver safety / performance scores", () =>
            {
                _ = DriverSafetyPerformanceService.BuildLeaderboard();
            });

            Step(result, "Refresh analytics", () =>
            {
                _ = FleetAnalyticsService.BuildSnapshot();
            });

            result.CompletedUtc = DateTime.UtcNow;
            result.Success = result.Errors.Count == 0;
            return result;
        }

        public static OperationsSyncResult RunDispatchOperations()
        {
            var result = new OperationsSyncResult
            {
                StartedUtc = DateTime.UtcNow
            };

            Step(result, "Sync delivered dispatch loads", () =>
            {
                FleetEconomyIntegration.TrySyncDeliveredLoads();
            });

            Step(result, "Sync delivered contract loads", () =>
            {
                DispatchContractAtsIntegrationService.SyncDeliveredContractLoads();
            });

            Step(result, "Process current fuel / maintenance telemetry", () =>
            {
                FuelMaintenanceAutomationService.ProcessCurrentTelemetryIfAvailable();
            });

            Step(result, "Refresh analytics", () =>
            {
                _ = FleetAnalyticsService.BuildSnapshot();
            });

            result.CompletedUtc = DateTime.UtcNow;
            result.Success = result.Errors.Count == 0;
            return result;
        }

        public static List<OperationsDashboardRow> BuildDashboardRows()
        {
            var rows = new List<OperationsDashboardRow>();

            Add(rows, "Fleet Economy", "Company balance, revenue, expenses, transaction ledger", "Ready");
            Add(rows, "Real Driver Payroll", "Delivered loads create real driver payroll", "Ready");
            Add(rows, "Truck Profitability", "Revenue, fuel, maintenance, payroll, profit per truck", "Ready");
            Add(rows, "Driver Scores", "Safety, performance, economy, grades, events", "Ready");
            Add(rows, "Fuel / Maintenance", "Auto fuel, wear reserve, repair reserve from telemetry", "Ready");
            Add(rows, "Dispatch Contracts", "Recurring freight, deadlines, bonuses, penalties", "Ready");
            Add(rows, "ATS Contract Export", "Contracts create dispatch jobs and attempt ATS save injection", "Ready");
            Add(rows, "Fleet Analytics", "30-day trends, top drivers, top trucks, top contracts", "Ready");
            Add(rows, "Load Board History", "Delivered/imported loads persist through restart", "Ready");

            return rows;
        }

        private static void Add(List<OperationsDashboardRow> rows, string system, string purpose, string status)
        {
            rows.Add(new OperationsDashboardRow
            {
                System = system,
                Purpose = purpose,
                Status = status,
                LastChecked = DateTime.Now
            });
        }

        private static void Step(OperationsSyncResult result, string label, Action action)
        {
            try
            {
                action();
                result.Completed.Add(label);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Errors.Add($"{label}: {ex.Message}");
            }
        }
    }

    public sealed class OperationsDashboardRow
    {
        public string System { get; set; } = "";
        public string Purpose { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime LastChecked { get; set; } = DateTime.Now;
    }
}
