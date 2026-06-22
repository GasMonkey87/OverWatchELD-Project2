using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services.Licensing
{
    public sealed class DriverLicenseEndorsementDefinition
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string Icon { get; set; } = "🪪";
        public string Description { get; set; } = "";
        public string[] RoleAliases { get; set; } = Array.Empty<string>();
    }

    public sealed class DriverLicenseEndorsementRow
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string Icon { get; set; } = "🪪";
        public string Source { get; set; } = "";
    }

    public static class DriverLicenseEndorsementService
    {
        private static readonly object Gate = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private static string StorePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "OverWatchELD",
                "driver_license_endorsements.json");

        public static IReadOnlyList<DriverLicenseEndorsementDefinition> StandardDefinitions { get; } =
            new List<DriverLicenseEndorsementDefinition>
            {
                new()
                {
                    Code = "H",
                    Name = "Hazmat",
                    Icon = "☣️",
                    Description = "DOT hazardous materials endorsement.",
                    RoleAliases = new[] { "hazmat", "hazardous materials", "endorsement h", "dot h" }
                },
                new()
                {
                    Code = "N",
                    Name = "Tank Vehicle",
                    Icon = "🛢️",
                    Description = "DOT tank vehicle endorsement.",
                    RoleAliases = new[] { "tank", "tanker", "tank vehicle", "endorsement n", "dot n" }
                },
                new()
                {
                    Code = "P",
                    Name = "Passenger",
                    Icon = "🚌",
                    Description = "DOT passenger endorsement.",
                    RoleAliases = new[] { "passenger", "endorsement p", "dot p" }
                },
                new()
                {
                    Code = "S",
                    Name = "School Bus",
                    Icon = "🏫",
                    Description = "DOT school bus endorsement.",
                    RoleAliases = new[] { "school bus", "schoolbus", "endorsement s", "dot s" }
                },
                new()
                {
                    Code = "T",
                    Name = "Double / Triple Trailers",
                    Icon = "🚛",
                    Description = "DOT double and triple trailer endorsement.",
                    RoleAliases = new[] { "double", "triple", "doubles", "triples", "double triple", "endorsement t", "dot t" }
                },
                new()
                {
                    Code = "X",
                    Name = "Tank + Hazmat",
                    Icon = "⚠️",
                    Description = "DOT combined tank vehicle and hazardous materials endorsement.",
                    RoleAliases = new[] { "x endorsement", "hazmat tanker", "tank hazmat", "tanker hazmat", "endorsement x", "dot x" }
                },
                new()
                {
                    Code = "AIR",
                    Name = "Air Brake",
                    Icon = "🛑",
                    Description = "Air brake qualification / restriction cleared.",
                    RoleAliases = new[] { "air brake", "air brakes", "no air brake restriction" }
                },
                new()
                {
                    Code = "COMBO",
                    Name = "Combination Vehicle",
                    Icon = "🔗",
                    Description = "Combination vehicle qualification.",
                    RoleAliases = new[] { "combination", "combination vehicle", "combo" }
                }
            };

        public static List<DriverLicenseEndorsementRow> BuildRows(string? driverKey, string? discordRoleText)
        {
            var rows = new List<DriverLicenseEndorsementRow>();

            var manual = GetManualCodes(driverKey);
            foreach (var code in manual)
            {
                var def = Find(code);
                if (def == null)
                    continue;

                rows.Add(new DriverLicenseEndorsementRow
                {
                    Code = def.Code,
                    Name = def.Name,
                    Icon = def.Icon,
                    Source = "Manual DOT endorsement"
                });
            }

            foreach (var code in DetectCodesFromDiscordRoles(discordRoleText))
            {
                if (rows.Any(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var def = Find(code);
                if (def == null)
                    continue;

                rows.Add(new DriverLicenseEndorsementRow
                {
                    Code = def.Code,
                    Name = def.Name,
                    Icon = def.Icon,
                    Source = "Detected from Discord role"
                });
            }

            return rows
                .OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static HashSet<string> GetManualCodes(string? driverKey)
        {
            var key = NormalizeKey(driverKey);
            if (string.IsNullOrWhiteSpace(key))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            lock (Gate)
            {
                var map = LoadMap();

                if (!map.TryGetValue(key, out var codes) || codes == null)
                    return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                return codes
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim().ToUpperInvariant())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
        }

        public static void SaveManualCodes(string? driverKey, IEnumerable<string>? codes)
        {
            var key = NormalizeKey(driverKey);
            if (string.IsNullOrWhiteSpace(key))
                return;

            lock (Gate)
            {
                var map = LoadMap();

                map[key] = (codes ?? Array.Empty<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim().ToUpperInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                SaveMap(map);
            }
        }

        public static List<string> DetectCodesFromDiscordRoles(string? roleText)
        {
            var text = (roleText ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();

            var normalized = text
                .ToLowerInvariant()
                .Replace("_", " ")
                .Replace("-", " ")
                .Replace(".", " ");

            var found = new List<string>();

            foreach (var def in StandardDefinitions)
            {
                var aliases = new List<string> { def.Code, def.Name };
                aliases.AddRange(def.RoleAliases ?? Array.Empty<string>());

                foreach (var alias in aliases)
                {
                    var a = (alias ?? "").Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(a))
                        continue;

                    if (normalized.Contains(a))
                    {
                        found.Add(def.Code);
                        break;
                    }
                }
            }

            return found
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static DriverLicenseEndorsementDefinition? Find(string? code)
        {
            return StandardDefinitions.FirstOrDefault(x =>
                string.Equals(x.Code, (code ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private static Dictionary<string, List<string>> LoadMap()
        {
            try
            {
                if (!File.Exists(StorePath))
                    return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

                var json = File.ReadAllText(StorePath);
                return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json, JsonOptions)
                    ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static void SaveMap(Dictionary<string, List<string>> map)
        {
            try
            {
                var dir = Path.GetDirectoryName(StorePath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(StorePath, JsonSerializer.Serialize(map, JsonOptions));
            }
            catch
            {
            }
        }

        private static string NormalizeKey(string? value)
        {
            return (value ?? "").Trim();
        }
    }
}
