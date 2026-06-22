using OverWatchELD.Models.Economy;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services.Economy
{
    public static class RealDriverPayrollStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private static string EconomyFolder
        {
            get
            {
                var folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "OverWatchELD",
                    "Economy");

                Directory.CreateDirectory(folder);
                return folder;
            }
        }

        private static string FilePath => Path.Combine(EconomyFolder, "real_driver_payroll_profiles.json");

        public static List<RealDriverPayrollProfile> Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new List<RealDriverPayrollProfile>();

                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<List<RealDriverPayrollProfile>>(json, JsonOptions)
                       ?? new List<RealDriverPayrollProfile>();
            }
            catch
            {
                return new List<RealDriverPayrollProfile>();
            }
        }

        public static void Save(List<RealDriverPayrollProfile> profiles)
        {
            try
            {
                profiles = profiles
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.DriverName))
                    .GroupBy(x => DriverKey(x.DriverName, x.DriverDiscordId), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .OrderBy(x => x.DriverName)
                    .ToList();

                File.WriteAllText(FilePath, JsonSerializer.Serialize(profiles, JsonOptions));
            }
            catch
            {
            }
        }

        public static RealDriverPayrollProfile GetOrCreate(string driverName, string driverDiscordId = "")
        {
            driverName = (driverName ?? "").Trim();
            driverDiscordId = (driverDiscordId ?? "").Trim();

            if (string.IsNullOrWhiteSpace(driverName))
                driverName = "Unknown Driver";

            var profiles = Load();
            var key = DriverKey(driverName, driverDiscordId);

            var existing = profiles.FirstOrDefault(x =>
                string.Equals(DriverKey(x.DriverName, x.DriverDiscordId), key, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
                return existing;

            var profile = new RealDriverPayrollProfile
            {
                DriverName = driverName,
                DriverDiscordId = driverDiscordId,
                PayMode = "Percent",
                PercentOfLoad = 25m,
                CentsPerMile = 65m,
                FlatPerLoad = 250m,
                Enabled = true,
                UpdatedUtc = DateTime.UtcNow
            };

            profiles.Add(profile);
            Save(profiles);

            return profile;
        }

        public static void Upsert(RealDriverPayrollProfile profile)
        {
            var profiles = Load();
            var key = DriverKey(profile.DriverName, profile.DriverDiscordId);

            profiles.RemoveAll(x =>
                string.Equals(DriverKey(x.DriverName, x.DriverDiscordId), key, StringComparison.OrdinalIgnoreCase));

            profile.UpdatedUtc = DateTime.UtcNow;
            profiles.Add(profile);
            Save(profiles);
        }

        private static string DriverKey(string driverName, string driverDiscordId)
        {
            if (!string.IsNullOrWhiteSpace(driverDiscordId))
                return "id:" + driverDiscordId.Trim();

            return "name:" + (driverName ?? "").Trim();
        }
    }
}
