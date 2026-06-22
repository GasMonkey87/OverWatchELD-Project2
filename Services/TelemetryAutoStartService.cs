using System;
using System.Diagnostics;
using System.IO;

namespace OverWatchELD.Services
{
    public sealed class TelemetryAutoStartService
    {
        public bool TryStartTelemetry(string? exePath, out string message)
        {
            message = "";

            // If user configured a path, prefer it
            if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
            {
                return StartExe(exePath, out message);
            }

            // Fallback candidates (best-effort only)
            var candidates = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "Ets2TelemetryServer.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Ets2TelemetryServer.exe"),
            };

            foreach (var c in candidates)
            {
                if (File.Exists(c))
                    return StartExe(c, out message);
            }

            message = "Telemetry server not found. Install it from https://github.com/Funbit/ets2-telemetry-server and set the path in Support > Telemetry Settings.";
            return false;
        }

        private static bool StartExe(string exePath, out string message)
        {
            message = "";

            try
            {
                var procName = Path.GetFileNameWithoutExtension(exePath);
                try
                {
                    if (Process.GetProcessesByName(procName).Length > 0)
                    {
                        message = "Telemetry server already running.";
                        return true;
                    }
                }
                catch { }

                Process.Start(new ProcessStartInfo(exePath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? ""
                });

                message = "Telemetry server started.";
                return true;
            }
            catch (Exception ex)
            {
                message = "Failed to start telemetry server: " + ex.Message;
                return false;
            }
        }

        internal void TryStartTelemetry()
        {
            throw new NotImplementedException();
        }
    }
}
