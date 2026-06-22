using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public static class AdminAccessService
    {
        private sealed class AdminAccessConfig
        {
            public List<string> Owners { get; set; } = new();
            public List<string> Admins { get; set; } = new();
            public List<string> Managers { get; set; } = new();
            public List<string> Management { get; set; } = new();
            public List<string> AllowedRoles { get; set; } = new()
            {
                "owner",
                "admin",
                "manager",
                "management",
                "dispatcher",
                "dispatch"
            };
        }

        private static readonly JsonSerializerOptions _json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private static string ConfigPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "admin_access.json");

        public static bool CanManageDiscordSettings(
            string? linkedDiscordUser,
            string? linkedRole = null,
            IEnumerable<string>? extraRoleNames = null)
        {
            var cfg = Load();

            var user = Normalize(linkedDiscordUser);
            var role = Normalize(linkedRole);

            if (!string.IsNullOrWhiteSpace(user))
            {
                if (cfg.Owners.Any(x => Normalize(x) == user))
                    return true;

                if (cfg.Admins.Any(x => Normalize(x) == user))
                    return true;

                if (cfg.Managers.Any(x => Normalize(x) == user))
                    return true;

                if (cfg.Management.Any(x => Normalize(x) == user))
                    return true;
            }

            if (!string.IsNullOrWhiteSpace(role) &&
                cfg.AllowedRoles.Any(x => Normalize(x) == role))
                return true;

            if (extraRoleNames != null)
            {
                foreach (var r in extraRoleNames)
                {
                    var nr = Normalize(r);
                    if (cfg.AllowedRoles.Any(x => Normalize(x) == nr))
                        return true;
                }
            }

            return false;
        }

        public static IReadOnlyList<string> GetManagers()
        {
            return Load().Managers
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .OrderBy(x => x)
                .ToList();
        }

        public static void SaveManagers(IEnumerable<string> managers)
        {
            var cfg = Load();
            cfg.Managers = managers?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList() ?? new List<string>();

            Save(cfg);
        }

        public static void EnsureExists()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                if (!File.Exists(ConfigPath))
                {
                    Save(new AdminAccessConfig());
                }
            }
            catch
            {
            }
        }

        private static AdminAccessConfig Load()
        {
            try
            {
                EnsureExists();

                if (!File.Exists(ConfigPath))
                    return new AdminAccessConfig();

                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<AdminAccessConfig>(json, _json) ?? new AdminAccessConfig();
            }
            catch
            {
                return new AdminAccessConfig();
            }
        }

        private static void Save(AdminAccessConfig cfg)
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(cfg, _json);
                File.WriteAllText(ConfigPath, json);
            }
            catch
            {
            }
        }

        private static string Normalize(string? value) =>
            (value ?? "").Trim().ToLowerInvariant();
    }
}