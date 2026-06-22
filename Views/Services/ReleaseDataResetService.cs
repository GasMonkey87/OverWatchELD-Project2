using System;
using System.IO;

namespace OverWatchELD.Services
{
    public static class ReleaseDataResetService
    {
        public static void ClearAllLocalUserData()
        {
            TryDeleteDirectory(Path.Combine(AppContext.BaseDirectory, "Data"));
            TryDeleteFile(Path.Combine(AppContext.BaseDirectory, "Config", "settings_window.discord.json"));
            TryDeleteFile(Path.Combine(AppContext.BaseDirectory, "Config", "vtc.config.json"));

            TryDeleteDirectory(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OverWatchELD"));

            TryDeleteDirectory(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "OverWatchELD"));
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch { }
        }
    }
}