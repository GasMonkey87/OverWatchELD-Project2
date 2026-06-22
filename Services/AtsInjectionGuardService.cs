using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public sealed class AtsInjectionOptions
    {
        public bool EnableExperimentalAtsInjection { get; set; } = false;
        public string AtsProfilePath { get; set; } = "";
        public string AtsSaveFolderName { get; set; } = "autosave";
        public bool RequireBackupBeforeInjection { get; set; } = true;
        public bool DisableInjectionOnSteamCloud { get; set; } = true;
        public bool AutoDetectProfileAndSave { get; set; } = true;
    }

    public sealed class AtsInjectionGuardResult
    {
        public bool CanInject { get; set; }
        public string Message { get; set; } = "";
        public string GameSiiPath { get; set; } = "";
        public AtsInjectionOptions Options { get; set; } = new();
    }

    public static class AtsInjectionGuardService
    {
        private const string ConfigFolderName = "OverWatchELD";
        private const string ConfigFileName = "ats_injection.settings.json";

        public static string GetConfigPath()
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var folder = Path.Combine(docs, ConfigFolderName);
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, ConfigFileName);
        }

        public static AtsInjectionOptions LoadOrCreateDefaults()
        {
            var path = GetConfigPath();

            try
            {
                AtsInjectionOptions options;

                if (!File.Exists(path))
                {
                    options = new AtsInjectionOptions();
                    AutoFillFromDetectedProfile(options);
                    Save(options);
                    return options;
                }

                var text = File.ReadAllText(path);
                options = JsonSerializer.Deserialize<AtsInjectionOptions>(text) ?? new AtsInjectionOptions();

                var changed = false;

                if (options.AutoDetectProfileAndSave)
                    changed = AutoFillFromDetectedProfile(options);

                if (changed)
                    Save(options);

                return options;
            }
            catch
            {
                return new AtsInjectionOptions();
            }
        }

        public static void Save(AtsInjectionOptions options)
        {
            var path = GetConfigPath();
            var json = JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }

        public static AtsInjectionGuardResult Check()
        {
            var options = LoadOrCreateDefaults();

            if (!options.EnableExperimentalAtsInjection)
            {
                return new AtsInjectionGuardResult
                {
                    CanInject = false,
                    Message = "ATS Save Injection is disabled. Set EnableExperimentalAtsInjection to true in Documents\\OverWatchELD\\ats_injection.settings.json.",
                    Options = options
                };
            }

            if (string.IsNullOrWhiteSpace(options.AtsProfilePath) || !Directory.Exists(options.AtsProfilePath))
            {
                return new AtsInjectionGuardResult
                {
                    CanInject = false,
                    Message = "ATS profile path is missing or invalid in ats_injection.settings.json.",
                    Options = options
                };
            }

            if (options.DisableInjectionOnSteamCloud &&
                options.AtsProfilePath.Contains("steam_profiles", StringComparison.OrdinalIgnoreCase))
            {
                return new AtsInjectionGuardResult
                {
                    CanInject = false,
                    Message = "Steam Cloud profiles are blocked for public-release-safe injection.",
                    Options = options
                };
            }

            var saveFolder = string.IsNullOrWhiteSpace(options.AtsSaveFolderName)
                ? "autosave"
                : options.AtsSaveFolderName.Trim();

            var gameSiiPath = Path.Combine(options.AtsProfilePath, "save", saveFolder, "game.sii");
            if (!File.Exists(gameSiiPath))
            {
                return new AtsInjectionGuardResult
                {
                    CanInject = false,
                    Message = $"No game.sii was found at: {gameSiiPath}",
                    Options = options
                };
            }

            string text;
            try
            {
                text = File.ReadAllText(gameSiiPath);
            }
            catch (Exception ex)
            {
                return new AtsInjectionGuardResult
                {
                    CanInject = false,
                    Message = "Could not read game.sii: " + ex.Message,
                    Options = options
                };
            }

            if (!text.Contains("SiiNunit", StringComparison.OrdinalIgnoreCase))
            {
                return new AtsInjectionGuardResult
                {
                    CanInject = false,
                    Message = "game.sii is not decrypted. Injection requires a decrypted local save.",
                    Options = options
                };
            }

            return new AtsInjectionGuardResult
            {
                CanInject = true,
                Message = "ATS injection checks passed.",
                GameSiiPath = gameSiiPath,
                Options = options
            };
        }

        private static bool AutoFillFromDetectedProfile(AtsInjectionOptions options)
        {
            var detected = DetectMostRecentLocalProfileAndSave();
            if (detected.profilePath == "" || detected.saveFolder == "")
                return false;

            var changed = false;

            if (string.IsNullOrWhiteSpace(options.AtsProfilePath) ||
                !Directory.Exists(options.AtsProfilePath))
            {
                options.AtsProfilePath = detected.profilePath;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(options.AtsSaveFolderName) ||
                !Directory.Exists(Path.Combine(options.AtsProfilePath, "save", options.AtsSaveFolderName)))
            {
                options.AtsSaveFolderName = detected.saveFolder;
                changed = true;
            }

            return changed;
        }

        private static (string profilePath, string saveFolder) DetectMostRecentLocalProfileAndSave()
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var atsRoot = Path.Combine(docs, "American Truck Simulator");
            var profilesRoot = Path.Combine(atsRoot, "profiles");

            if (!Directory.Exists(profilesRoot))
                return ("", "");

            var bestProfile = "";
            var bestSaveFolder = "";
            var bestUtc = DateTime.MinValue;

            foreach (var profileDir in Directory.GetDirectories(profilesRoot))
            {
                var saveRoot = Path.Combine(profileDir, "save");
                if (!Directory.Exists(saveRoot))
                    continue;

                foreach (var saveDir in Directory.GetDirectories(saveRoot))
                {
                    var gameSii = Path.Combine(saveDir, "game.sii");
                    if (!File.Exists(gameSii))
                        continue;

                    DateTime stamp;
                    try
                    {
                        stamp = File.GetLastWriteTimeUtc(gameSii);
                    }
                    catch
                    {
                        continue;
                    }

                    if (stamp > bestUtc)
                    {
                        bestUtc = stamp;
                        bestProfile = profileDir;
                        bestSaveFolder = Path.GetFileName(saveDir);
                    }
                }
            }

            return (bestProfile, bestSaveFolder);
        }
    }
}