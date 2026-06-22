using System;
using System.Reflection;

namespace OverWatchELD.Services
{
    /// <summary>
    /// Central place to read displayable version/build info.
    /// Works for Debug runs AND installed Velopack builds.
    /// </summary>
    public static class AppVersion
    {
        public static string ProductName =>
            Assembly.GetEntryAssembly()?.GetName().Name ?? "OverWatchELD";

        public static string Version =>
            Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0.0";

        // Prefer InformationalVersion when present (nice semver / git hash support)
        public static string InformationalVersion
        {
            get
            {
                try
                {
                    var asm = Assembly.GetEntryAssembly();
                    var attr = asm?.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                    var v = attr?.InformationalVersion;
                    return string.IsNullOrWhiteSpace(v) ? Version : v!;
                }
                catch
                {
                    return Version;
                }
            }
        }

        public static string Display =>
            $"{ProductName} v{InformationalVersion}";
    }
}