using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using OverWatchELD.Services.Fleet;

namespace OverWatchELD.Services
{
    /// <summary>
    /// Owner-only clean slate reset for all user-entered VTC data.
    /// This intentionally leaves application binaries and map/static assets alone.
    /// </summary>
    public static class VtcCleanSlateResetService
    {
        private static string RoamingDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OverWatchELD");

        private static string LocalDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OverWatchELD");

        private static string BaseDir => AppDomain.CurrentDomain.BaseDirectory;

        public sealed class ResetResult
        {
            public int FilesDeleted { get; set; }
            public int DirectoriesDeleted { get; set; }
            public List<string> Warnings { get; set; } = new();

            public string Summary =>
                $"Clean slate complete. Deleted {FilesDeleted} file(s) and {DirectoriesDeleted} folder(s)." +
                (Warnings.Count == 0 ? "" : $" {Warnings.Count} item(s) could not be removed while the app was running.");
        }

        public static ResetResult WipeAllVtcData()
        {
            var result = new ResetResult();

            foreach (var dir in GetVtcDataDirectories().Distinct(StringComparer.OrdinalIgnoreCase))
                DeleteDirectory(dir, result);

            foreach (var file in GetVtcDataFiles().Distinct(StringComparer.OrdinalIgnoreCase))
                DeleteFile(file, result);

            // Reset in-memory/session state and first-run so the owner must connect Discord/VTC again.
            try { VtcHardResetService.ClearLocalVtcState(resetFirstRun: true); } catch (Exception ex) { result.Warnings.Add("Session reset: " + ex.Message); }
            try { FirstRunSetupService.Reset(); } catch (Exception ex) { result.Warnings.Add("First run reset: " + ex.Message); }

            try
            {
                var cfg = new VtcConfig
                {
                    Enabled = false,
                    VtcName = "",
                    VtcShort = "",
                    GuildId = "",
                    BotApiBaseUrl = "https://overwatcheld.up.railway.app",
                    PairCode = "",
                    Discord = new VtcConfig.DiscordConfig(),
                    Linking = new VtcConfig.LinkingConfig()
                };
                VtcConfigService.Save(cfg);
            }
            catch (Exception ex) { result.Warnings.Add("VTC config reset: " + ex.Message); }

            try { new FleetTruckRepository().SaveAll(Array.Empty<OverWatchELD.Models.Fleet.FleetTruck>()); } catch { }
            try { new FleetCommandStore().SaveAll(new List<OverWatchELD.Models.Fleet.FleetCommandTruck>()); } catch { }

            return result;
        }

        private static IEnumerable<string> GetVtcDataDirectories()
        {
            yield return Path.Combine(BaseDir, "Data", "Fleet");
            yield return Path.Combine(BaseDir, "Data", "Vtc");
            yield return Path.Combine(BaseDir, "Data", "VTC");
            yield return Path.Combine(BaseDir, "Data", "BOL");
            yield return Path.Combine(BaseDir, "Data", "Bols");
            yield return Path.Combine(BaseDir, "Data", "Loads");
            yield return Path.Combine(BaseDir, "Data", "Dispatch");
            yield return Path.Combine(BaseDir, "Data", "Maintenance");
            yield return Path.Combine(BaseDir, "Data", "Discord");
            yield return Path.Combine(BaseDir, "Data", "Roster");
            yield return Path.Combine(BaseDir, "Config", "Vtc");
            yield return Path.Combine(RoamingDir, "CompanyLoads");
            yield return Path.Combine(RoamingDir, "VTC");
            yield return Path.Combine(RoamingDir, "Vtc");
            yield return Path.Combine(RoamingDir, "Fleet");
            yield return Path.Combine(RoamingDir, "BOL");
            yield return Path.Combine(RoamingDir, "Bols");
            yield return Path.Combine(RoamingDir, "Dispatch");
            yield return Path.Combine(RoamingDir, "Maintenance");
            yield return Path.Combine(LocalDir, "VTC");
            yield return Path.Combine(LocalDir, "Vtc");
            yield return Path.Combine(LocalDir, "Fleet");
            yield return Path.Combine(LocalDir, "BOL");
            yield return Path.Combine(LocalDir, "Bols");
        }

        private static IEnumerable<string> GetVtcDataFiles()
        {
            string[] names =
            {
                "eld.db", "eld.sqlite", "overwatch.db", "overwatch.sqlite",
                "vtc.config.json", "vtc_config.json", "discord_identity.json", "discord_vtc_binding.json",
                "vtc_pairing.json", "vtc_link_directory.json", "fleet_trucks.json", "fleet_command_trucks.json",
                "trucks.json", "loads.json", "bols.json", "bol_index.json", "maintenance.json",
                "roster.json", "driver_roster.json", "announcements.json", "dispatch_contracts.json"
            };

            foreach (var n in names)
            {
                yield return Path.Combine(BaseDir, n);
                yield return Path.Combine(BaseDir, "Data", n);
                yield return Path.Combine(BaseDir, "Config", n);
                yield return Path.Combine(RoamingDir, n);
                yield return Path.Combine(LocalDir, n);
            }

            yield return VtcConfigService.ConfigPath;
            yield return FirstRunSetupService.SetupFlagPath;
            yield return Path.Combine(LocalDir, "first_setup_complete.flag");
        }

        private static void DeleteFile(string path, ResetResult result)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return;
                File.Delete(path);
                result.FilesDeleted++;
            }
            catch (Exception ex)
            {
                result.Warnings.Add(path + ": " + ex.Message);
            }
        }

        private static void DeleteDirectory(string path, ResetResult result)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                    return;
                Directory.Delete(path, recursive: true);
                result.DirectoriesDeleted++;
            }
            catch (Exception ex)
            {
                result.Warnings.Add(path + ": " + ex.Message);
            }
        }
    }
}
