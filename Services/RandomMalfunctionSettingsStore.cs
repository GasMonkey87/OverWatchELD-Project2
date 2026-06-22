using System;
using System.IO;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public sealed class RandomMalfunctionVtcPolicy
    {
        public bool EnableRandomMalfunctionsForVtc { get; set; } = false;
        public bool AllowDriverOverride { get; set; } = false;

        public double ChancePercent { get; set; } = 3;
        public int CheckIntervalMinutes { get; set; } = 15;
        public int CooldownMinutes { get; set; } = 30;

        public bool OnlyWhileDriving { get; set; } = true;
        public double MinSpeedMph { get; set; } = 5;

        public string UpdatedBy { get; set; } = "";
        public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    public sealed class RandomMalfunctionDriverPreference
    {
        public string DriverKey { get; set; } = "default";
        public bool DriverEnabled { get; set; } = true;
        public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    // Backward-compatible simple settings class.
    // Keep this so older code that references RandomMalfunctionSettings still builds.
    public sealed class RandomMalfunctionSettings
    {
        public bool EnableRandomMalfunctionsForVtc { get; set; } = false;
        public bool AllowDriversToToggleRandomMalfunctions { get; set; } = false;
        public bool DriverEnabledRandomMalfunctions { get; set; } = false;
    }

    public static class RandomMalfunctionSettingsStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private static string FolderPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OverWatchELD");

        private static string PolicyFilePath =>
            Path.Combine(FolderPath, "random_malfunction_policy.json");

        private static string LegacyFilePath =>
            Path.Combine(FolderPath, "random_malfunctions.json");

        private static string DriverFilePath(string? driverKey) =>
            Path.Combine(
                FolderPath,
                $"random_malfunction_driver_{Sanitize(driverKey)}.json");

        public static RandomMalfunctionVtcPolicy LoadPolicy()
        {
            try
            {
                Directory.CreateDirectory(FolderPath);

                if (File.Exists(PolicyFilePath))
                {
                    var policy = JsonSerializer.Deserialize<RandomMalfunctionVtcPolicy>(
                        File.ReadAllText(PolicyFilePath),
                        JsonOptions);

                    return policy ?? new RandomMalfunctionVtcPolicy();
                }

                // Migrate old single-file settings if present.
                if (File.Exists(LegacyFilePath))
                {
                    var legacy = JsonSerializer.Deserialize<RandomMalfunctionSettings>(
                        File.ReadAllText(LegacyFilePath),
                        JsonOptions);

                    if (legacy != null)
                    {
                        var migrated = new RandomMalfunctionVtcPolicy
                        {
                            EnableRandomMalfunctionsForVtc = legacy.EnableRandomMalfunctionsForVtc,
                            AllowDriverOverride = legacy.AllowDriversToToggleRandomMalfunctions,
                            UpdatedUtc = DateTimeOffset.UtcNow,
                            UpdatedBy = "Migrated Legacy Settings"
                        };

                        SavePolicy(migrated);

                        SaveDriverPreference(new RandomMalfunctionDriverPreference
                        {
                            DriverKey = "default",
                            DriverEnabled = legacy.DriverEnabledRandomMalfunctions,
                            UpdatedUtc = DateTimeOffset.UtcNow
                        });

                        return migrated;
                    }
                }

                var defaults = new RandomMalfunctionVtcPolicy();
                SavePolicy(defaults);
                return defaults;
            }
            catch
            {
                return new RandomMalfunctionVtcPolicy();
            }
        }

        public static void SavePolicy(RandomMalfunctionVtcPolicy policy)
        {
            try
            {
                Directory.CreateDirectory(FolderPath);

                policy ??= new RandomMalfunctionVtcPolicy();

                if (policy.UpdatedUtc == default)
                    policy.UpdatedUtc = DateTimeOffset.UtcNow;

                File.WriteAllText(
                    PolicyFilePath,
                    JsonSerializer.Serialize(policy, JsonOptions));
            }
            catch
            {
            }
        }

        public static RandomMalfunctionDriverPreference LoadDriverPreference(string? driverKey = "default")
        {
            driverKey = NormalizeDriverKey(driverKey);

            try
            {
                Directory.CreateDirectory(FolderPath);

                var path = DriverFilePath(driverKey);

                if (File.Exists(path))
                {
                    var pref = JsonSerializer.Deserialize<RandomMalfunctionDriverPreference>(
                        File.ReadAllText(path),
                        JsonOptions);

                    if (pref != null)
                    {
                        if (string.IsNullOrWhiteSpace(pref.DriverKey))
                            pref.DriverKey = driverKey;

                        return pref;
                    }
                }

                // Migrate old single-file driver setting if present.
                if (File.Exists(LegacyFilePath))
                {
                    var legacy = JsonSerializer.Deserialize<RandomMalfunctionSettings>(
                        File.ReadAllText(LegacyFilePath),
                        JsonOptions);

                    if (legacy != null)
                    {
                        var migrated = new RandomMalfunctionDriverPreference
                        {
                            DriverKey = driverKey,
                            DriverEnabled = legacy.DriverEnabledRandomMalfunctions,
                            UpdatedUtc = DateTimeOffset.UtcNow
                        };

                        SaveDriverPreference(migrated);
                        return migrated;
                    }
                }

                var defaults = new RandomMalfunctionDriverPreference
                {
                    DriverKey = driverKey,
                    DriverEnabled = true,
                    UpdatedUtc = DateTimeOffset.UtcNow
                };

                SaveDriverPreference(defaults);
                return defaults;
            }
            catch
            {
                return new RandomMalfunctionDriverPreference
                {
                    DriverKey = driverKey,
                    DriverEnabled = true,
                    UpdatedUtc = DateTimeOffset.UtcNow
                };
            }
        }

        public static void SaveDriverPreference(RandomMalfunctionDriverPreference preference)
        {
            try
            {
                Directory.CreateDirectory(FolderPath);

                preference ??= new RandomMalfunctionDriverPreference();

                preference.DriverKey = NormalizeDriverKey(preference.DriverKey);

                if (preference.UpdatedUtc == default)
                    preference.UpdatedUtc = DateTimeOffset.UtcNow;

                File.WriteAllText(
                    DriverFilePath(preference.DriverKey),
                    JsonSerializer.Serialize(preference, JsonOptions));
            }
            catch
            {
            }
        }

        public static bool RandomMalfunctionsActive(string? driverKey = "default")
        {
            var policy = LoadPolicy();

            if (!policy.EnableRandomMalfunctionsForVtc)
                return false;

            if (!policy.AllowDriverOverride)
                return true;

            var driver = LoadDriverPreference(driverKey);
            return driver.DriverEnabled;
        }

        public static bool ShouldRollRandomMalfunction(
            double speedMph,
            DateTimeOffset? lastMalfunctionUtc = null,
            string? driverKey = "default")
        {
            var policy = LoadPolicy();

            if (!RandomMalfunctionsActive(driverKey))
                return false;

            if (policy.OnlyWhileDriving && speedMph < policy.MinSpeedMph)
                return false;

            if (lastMalfunctionUtc.HasValue &&
                (DateTimeOffset.UtcNow - lastMalfunctionUtc.Value).TotalMinutes < Math.Max(1, policy.CooldownMinutes))
                return false;

            return true;
        }

        // Backward-compatible legacy APIs.
        public static RandomMalfunctionSettings Load()
        {
            var policy = LoadPolicy();
            var driver = LoadDriverPreference();

            return new RandomMalfunctionSettings
            {
                EnableRandomMalfunctionsForVtc = policy.EnableRandomMalfunctionsForVtc,
                AllowDriversToToggleRandomMalfunctions = policy.AllowDriverOverride,
                DriverEnabledRandomMalfunctions = driver.DriverEnabled
            };
        }

        public static void Save(RandomMalfunctionSettings settings)
        {
            settings ??= new RandomMalfunctionSettings();

            SavePolicy(new RandomMalfunctionVtcPolicy
            {
                EnableRandomMalfunctionsForVtc = settings.EnableRandomMalfunctionsForVtc,
                AllowDriverOverride = settings.AllowDriversToToggleRandomMalfunctions,
                UpdatedUtc = DateTimeOffset.UtcNow,
                UpdatedBy = "Legacy Settings Save"
            });

            SaveDriverPreference(new RandomMalfunctionDriverPreference
            {
                DriverKey = "default",
                DriverEnabled = settings.DriverEnabledRandomMalfunctions,
                UpdatedUtc = DateTimeOffset.UtcNow
            });
        }

        private static string NormalizeDriverKey(string? driverKey)
        {
            driverKey = (driverKey ?? "").Trim();
            return string.IsNullOrWhiteSpace(driverKey) ? "default" : driverKey;
        }

        private static string Sanitize(string? value)
        {
            value = NormalizeDriverKey(value);

            foreach (var c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');

            value = value
                .Replace(":", "_")
                .Replace("/", "_")
                .Replace("\\", "_")
                .Replace(" ", "_");

            return string.IsNullOrWhiteSpace(value) ? "default" : value;
        }
    }
}
