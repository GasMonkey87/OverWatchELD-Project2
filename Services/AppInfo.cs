using System.Reflection;

namespace OverWatchELD.Services
{
    public static class AppInfo
    {
        public static string ProductName =>
            Assembly.GetExecutingAssembly().GetName().Name ?? "OverWatch ELD";

        public static string Version =>
            Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "0.0.0";

        public static string Creator => "GasMonkey Creations";
        public static string Copyright => "© 2026 GasMonkey Creations. All rights reserved.";
    }
}