using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;

namespace OverWatchELD.Services
{
    public static class VtcHardResetService
    {
        private static string RoamingDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OverWatchELD");

        private static string LocalDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OverWatchELD");

        public static void HardUnlink()
        {
            ClearLocalVtcState(resetFirstRun: true);
        }

        public static void SetStandaloneMode()
        {
            ClearLocalVtcState(resetFirstRun: false);
            try { FirstRunSetupService.MarkStandalone(); } catch { }
        }

        public static void ClearLocalVtcState(bool resetFirstRun)
        {
            foreach (var path in GetKnownVtcStateFiles(resetFirstRun))
                DeleteIfExists(path);

            if (resetFirstRun)
            {
                try { FirstRunSetupService.Reset(); } catch { }
            }

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
            catch { }

            try
            {
                new DiscordIdentityService().SaveOrUpdate(new DiscordIdentityService.DiscordIdentity
                {
                    DiscordUserId = "",
                    DiscordUsername = "",
                    GuildId = "",
                    VtcName = ""
                });
            }
            catch { }

            try
            {
                var settingsService = new AppSettingsService();
                var settings = settingsService.Load();
                settings.UseVtcMode = false;
                settingsService.Save(settings);
            }
            catch { }

            ClearRunningSession();
        }

        private static IEnumerable<string> GetKnownVtcStateFiles(bool includeFirstRunFlag)
        {
            yield return VtcConfigService.ConfigPath;

            yield return Path.Combine(RoamingDir, "vtc_config.json");
            yield return Path.Combine(RoamingDir, "vtc.config.json");
            yield return Path.Combine(RoamingDir, "Config", "vtc_config.json");
            yield return Path.Combine(RoamingDir, "Config", "vtc.config.json");

            yield return Path.Combine(LocalDir, "vtc_config.json");
            yield return Path.Combine(LocalDir, "vtc.config.json");
            yield return Path.Combine(LocalDir, "Config", "vtc_config.json");
            yield return Path.Combine(LocalDir, "Config", "vtc.config.json");

            yield return AppPaths.FileInConfig("discord_identity.json");
            yield return Path.Combine(AppContext.BaseDirectory, "discord_identity.json");

            if (includeFirstRunFlag)
            {
                yield return FirstRunSetupService.SetupFlagPath;
                yield return Path.Combine(LocalDir, "first_setup_complete.flag");
            }
        }

        private static void ClearRunningSession()
        {
            try
            {
                if (Application.Current is App app)
                {
                    app.EnsureSession();

                    try { app.ForceReloadVtcConfigSafe(); } catch { }

                    try { app.Session.VtcProvider = "Standalone"; } catch { }
                    try { app.Session.VtcName = ""; } catch { }
                    try { app.Session.GuildId = ""; } catch { }
                    try { app.Session.DriverRole = "user"; } catch { }
                    try { app.Session.LinkedUserRole = "user"; } catch { }
                    try { app.Session.DiscordUserId = ""; } catch { }
                    try { app.Session.DiscordUsername = ""; } catch { }
                }
            }
            catch { }
        }

        private static void DeleteIfExists(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }
    }
}
