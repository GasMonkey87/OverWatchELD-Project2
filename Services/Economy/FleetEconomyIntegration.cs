using OverWatchELD.Services.Operations;
using OverWatchELD.Views.Analytics;
using OverWatchELD.Views.Dispatch;
using OverWatchELD.Views.Economy;
using OverWatchELD.Views.Operations;
using OverWatchELD.Views.Performance;
using System.Windows;

namespace OverWatchELD.Services.Economy
{
    public static class FleetEconomyIntegration
    {
        public static void OpenEconomyWindow(Window? owner = null)
        {
            var win = new FleetEconomyWindow { Owner = owner };
            win.ShowDialog();
        }

        public static void OpenRealDriverEconomyWindow(Window? owner = null)
        {
            var win = new RealDriverEconomyWindow { Owner = owner };
            win.ShowDialog();
        }

        public static void OpenTruckProfitabilityLeaderboardsWindow(Window? owner = null)
        {
            var win = new TruckProfitabilityLeaderboardWindow { Owner = owner };
            win.ShowDialog();
        }

        public static void OpenDriverSafetyPerformanceWindow(Window? owner = null)
        {
            var win = new DriverSafetyPerformanceWindow { Owner = owner };
            win.ShowDialog();
        }

        public static void OpenFuelMaintenanceAutomationWindow(Window? owner = null)
        {
            var win = new FuelMaintenanceAutomationWindow { Owner = owner };
            win.ShowDialog();
        }

        public static void OpenDispatchContractsWindow(Window? owner = null)
        {
            var win = new DispatchContractsWindow { Owner = owner };
            win.ShowDialog();
        }

        public static void OpenFleetAnalyticsDashboardWindow(Window? owner = null)
        {
            var win = new FleetAnalyticsDashboardWindow { Owner = owner };
            win.ShowDialog();
        }

        public static void OpenOperationsCommandCenterWindow(Window? owner = null)
        {
            var win = new OperationsCommandCenterWindow { Owner = owner };
            win.ShowDialog();
        }

        public static void TrySyncDeliveredLoads()
        {
            FleetEconomyService.SyncDeliveredDispatchJobs();
            RealDriverEconomyPayrollService.SyncDeliveredLoadsAndPayroll();
        }

        public static void TryPostGarageDailyIncome()
        {
            GarageEconomyService.PostDailyGarageIncome();
        }

        public static void TryProcessCurrentFuelMaintenanceAutomation()
        {
            FuelMaintenanceAutomationService.ProcessCurrentTelemetryIfAvailable();
        }

        public static void TryRunUnifiedOperationsSync()
        {
            OperationsOrchestratorService.RunFullSync();
        }
    }
}
