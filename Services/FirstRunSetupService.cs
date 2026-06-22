using System;
using System.IO;

namespace OverWatchELD.Services
{
    public static class FirstRunSetupService
    {
        private static string AppDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OverWatchELD");

        public static string SetupFlagPath =>
            Path.Combine(AppDir, "first_setup_complete.flag");

        public static bool IsComplete()
        {
            try
            {
                if (!File.Exists(SetupFlagPath))
                    return false;

                var mode = File.ReadAllText(SetupFlagPath).Trim();

                return mode.Equals("standalone", StringComparison.OrdinalIgnoreCase)
                    || mode.Equals("vtc", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static string GetMode()
        {
            try
            {
                if (!File.Exists(SetupFlagPath))
                    return "";

                var mode = File.ReadAllText(SetupFlagPath).Trim().ToLowerInvariant();
                return mode == "standalone" || mode == "vtc" ? mode : "";
            }
            catch
            {
                return "";
            }
        }

        public static void MarkStandalone()
        {
            Directory.CreateDirectory(AppDir);
            File.WriteAllText(SetupFlagPath, "standalone");
        }

        public static void MarkVtc()
        {
            Directory.CreateDirectory(AppDir);
            File.WriteAllText(SetupFlagPath, "vtc");
        }

        public static void Reset()
        {
            try
            {
                if (File.Exists(SetupFlagPath))
                    File.Delete(SetupFlagPath);
            }
            catch { }
        }
    }
}
