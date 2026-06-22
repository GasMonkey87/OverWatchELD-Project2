// App.DashboardGlobals.cs
// ✅ Build-safe globals referenced by DashboardViewModel without touching UI.
// NOTE: Do NOT define App.Telemetry here (it already exists in App.xaml.cs).

using OverWatchELD.Services;

namespace OverWatchELD
{
    public partial class App
    {
        // Satisfy DashboardViewModel references
        public static object? DriverProfile { get; set; } = null;

        public static HosRulesService HosRules { get; } = new HosRulesService();
        public static LogStore LogStore { get; } = new LogStore();
    }
}