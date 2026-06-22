using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

using OverWatchELD.Services;

namespace OverWatchELD.Services
{
    /// <summary>
    /// Support helpers used by the Support page.
    /// - Creates a diagnostics zip
    /// - Provides basic telemetry status text
    /// - Opens URLs safely
    /// </summary>
    public static class SupportDiagnosticsService
    {
        // Funbit ETS2/ATS Telemetry Server releases page
        public const string TelemetryDownloadUrl = "https://github.com/Funbit/ets2-telemetry-server/releases";

        public sealed record TelemetryStatus(
            bool Connected,
            string Summary,
            string? EndpointUrl,
            string? LastError);

        public static TelemetryStatus GetTelemetryStatus(object? telemetryService)
        {
            try
            {
                if (telemetryService == null)
                    return new TelemetryStatus(false, "Telemetry: Not configured", null, null);

                // Read the same fields the companion uses.
                var endpoint = (string?)GetProp(telemetryService, "EndpointUrl");
                var snap = GetProp(telemetryService, "LastSnapshot") ?? GetProp(telemetryService, "Snapshot") ?? GetProp(telemetryService, "CurrentSnapshot");
                var connected = (bool?)GetProp(snap, "Connected") ?? false;

                var summary = connected ? "Telemetry: Connected" : "Telemetry: Disconnected";
                if (string.IsNullOrWhiteSpace(endpoint)) endpoint = null;

                return new TelemetryStatus(connected, summary, endpoint, null);
            }
            catch (Exception ex)
            {
                return new TelemetryStatus(false, "Telemetry: Error", null, ex.Message);
            }
        }

        public static async Task<bool> TryPingTelemetryAsync(string? endpointUrl)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(endpointUrl)) return false;
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2.5) };
                using var resp = await http.GetAsync(endpointUrl).ConfigureAwait(false);
                return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        public static string CreateDiagnosticsZip(bool includeDatabase)
        {
            // Put zips in Documents so users can find them easily.
            var outDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OverWatchELD", "Support");
            Directory.CreateDirectory(outDir);

            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var zipPath = Path.Combine(outDir, $"OverWatchELD_Diagnostics_{stamp}.zip");

            // Candidate locations (support both older ATS_ELD and current OverWatchELD folders)
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var candidates = new List<string>
            {
                Path.Combine(appData, "OverWatchELD"),
                Path.Combine(appData, "ATS_ELD"),
                Path.Combine(local, "OverWatchELD"),
                Path.Combine(local, "ATS_ELD"),
            };

            // Typical logs stored near exe
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;

            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                // App base logs
                AddIfExists(zip, Path.Combine(baseDir, "eld.log"), "app/eld.log");
                AddIfExists(zip, Path.Combine(baseDir, "vtcbot.log"), "app/vtcbot.log");
                AddIfExists(zip, Path.Combine(baseDir, "OverWatchELD.log"), "app/OverWatchELD.log");

                // Settings/login
                foreach (var c in candidates)
                {
                    AddIfExists(zip, Path.Combine(c, "settings.json"), $"settings/{Path.GetFileName(c)}_settings.json");
                    AddIfExists(zip, Path.Combine(c, "login.json"), $"settings/{Path.GetFileName(c)}_login.json");
                }

                // Companion/VTC db
                if (includeDatabase)
                {
                    AddIfExists(zip, Path.Combine(local, "OverWatchELD", "vtc.db"), "db/vtc.db");
                }

                // Lightweight environment snapshot
                var info = BuildEnvironmentInfo();
                AddText(zip, info, "app/environment.txt");
            }

            return zipPath;
        }

        public static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }

        public static void OpenFileInExplorer(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                if (File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                    return;
                }
                if (Directory.Exists(path))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
                }
            }
            catch { }
        }

        /// <summary>
        /// Opens the folder that contains the primary ELD logs.
        /// </summary>
        public static void OpenLogsFolder()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;

                // Prefer selecting a log file if it exists, otherwise just open the folder.
                var candidates = new[]
                {
                    Path.Combine(baseDir, "eld.log"),
                    Path.Combine(baseDir, "OverWatchELD.log"),
                    Path.Combine(baseDir, "vtcbot.log"),
                };

                var log = candidates.FirstOrDefault(File.Exists);
                if (!string.IsNullOrWhiteSpace(log))
                {
                    OpenFileInExplorer(log);
                    return;
                }

                OpenFileInExplorer(baseDir);
            }
            catch { }
        }

        /// <summary>
        /// Returns true if any process with the given name is currently running.
        /// </summary>
        public static bool IsProcessRunning(string processName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(processName)) return false;
                return Process.GetProcessesByName(processName).Length > 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// Prompts the user to pick the Funbit telemetry server executable.
        /// </summary>
        public static string? FindOrAskTelemetryServerPath()
        {
            try
            {
                var dlg = new OpenFileDialog
                {
                    Title = "Select Telemetry Server EXE",
                    Filter = "Executable (*.exe)|*.exe|All files (*.*)|*.*",
                    CheckFileExists = true,
                    Multiselect = false
                };

                // Best-effort default folder (Downloads)
                try
                {
                    var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                    if (Directory.Exists(downloads)) dlg.InitialDirectory = downloads;
                }
                catch { }

                return dlg.ShowDialog() == true ? dlg.FileName : null;
            }
            catch { return null; }
        }

        private static void AddIfExists(ZipArchive zip, string srcPath, string destPath)
        {
            try
            {
                if (!File.Exists(srcPath)) return;
                zip.CreateEntryFromFile(srcPath, destPath, CompressionLevel.Optimal);
            }
            catch { }
        }

        private static void AddText(ZipArchive zip, string text, string destPath)
        {
            try
            {
                var entry = zip.CreateEntry(destPath, CompressionLevel.Optimal);
                using var s = entry.Open();
                using var w = new StreamWriter(s, new UTF8Encoding(false));
                w.Write(text ?? "");
            }
            catch { }
        }

        private static string BuildEnvironmentInfo()
        {
            var sb = new StringBuilder();
            sb.AppendLine("OverWatch ELD Support Snapshot");
            sb.AppendLine($"Timestamp: {DateTimeOffset.Now:O}");
            sb.AppendLine($"OS: {Environment.OSVersion}");
            sb.AppendLine($"User: {EldDriverIdentityResolver.DriverName()}");
            sb.AppendLine($"Machine: {Environment.MachineName}");
            sb.AppendLine($"AppBase: {AppDomain.CurrentDomain.BaseDirectory}");
            sb.AppendLine($".NET: {Environment.Version}");
            return sb.ToString();
        }

        private static object? GetProp(object? obj, string name)
        {
            try
            {
                if (obj == null) return null;
                return obj.GetType().GetProperty(name)?.GetValue(obj);
            }
            catch { return null; }
        }
    }
}
