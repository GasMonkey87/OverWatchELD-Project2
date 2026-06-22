using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace OverWatchELD.Services
{
    public sealed class AtsExpansionModAddon
    {
        public string ModName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public bool IsMapExpansion { get; set; }
        public bool HasCities { get; set; }
        public bool HasCompanies { get; set; }
        public bool HasCargo { get; set; }
        public List<string> DetectedCities { get; set; } = new();
        public List<string> DetectedCompanies { get; set; } = new();
    }

    public static class AtsExpansionModAddonService
    {
        public static List<AtsExpansionModAddon> ScanExpansionMods(string atsModFolder)
        {
            var results = new List<AtsExpansionModAddon>();

            if (string.IsNullOrWhiteSpace(atsModFolder) || !Directory.Exists(atsModFolder))
                return results;

            foreach (var file in Directory.GetFiles(atsModFolder, "*.scs"))
            {
                var addon = ScanScs(file);

                if (addon.IsMapExpansion || addon.HasCities || addon.HasCompanies)
                    results.Add(addon);
            }

            return results
                .OrderByDescending(x => x.IsMapExpansion)
                .ThenBy(x => x.ModName)
                .ToList();
        }

        private static AtsExpansionModAddon ScanScs(string filePath)
        {
            var addon = new AtsExpansionModAddon
            {
                FilePath = filePath,
                ModName = Path.GetFileNameWithoutExtension(filePath)
            };

            try
            {
                using var zip = ZipFile.OpenRead(filePath);

                foreach (var entry in zip.Entries)
                {
                    var path = entry.FullName.Replace('\\', '/').ToLowerInvariant();

                    if (path.Contains("/city/") || path.StartsWith("def/city/"))
                    {
                        addon.HasCities = true;
                        addon.IsMapExpansion = true;

                        var city = CleanToken(Path.GetFileNameWithoutExtension(path));
                        if (!string.IsNullOrWhiteSpace(city) && !addon.DetectedCities.Contains(city))
                            addon.DetectedCities.Add(city);
                    }

                    if (path.Contains("/company/") || path.StartsWith("def/company/"))
                    {
                        addon.HasCompanies = true;

                        var company = CleanToken(Path.GetFileNameWithoutExtension(path));
                        if (!string.IsNullOrWhiteSpace(company) && !addon.DetectedCompanies.Contains(company))
                            addon.DetectedCompanies.Add(company);
                    }

                    if (path.Contains("/cargo/") || path.StartsWith("def/cargo/"))
                        addon.HasCargo = true;

                    if (path.Contains("map/") ||
                        path.Contains("def/country/") ||
                        path.Contains("def/world/") ||
                        path.Contains("def/ferry/"))
                    {
                        addon.IsMapExpansion = true;
                    }
                }
            }
            catch
            {
                // Some .scs files are not normal ZIP archives. Skip safely.
            }

            addon.DetectedCities = addon.DetectedCities
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .Take(250)
                .ToList();

            addon.DetectedCompanies = addon.DetectedCompanies
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .Take(250)
                .ToList();

            return addon;
        }

        private static string CleanToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            value = value
                .Replace(".sii", "")
                .Replace("_", " ")
                .Replace("-", " ")
                .Trim();

            return string.Join(" ",
                value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => char.ToUpperInvariant(x[0]) + x[1..].ToLowerInvariant()));
        }
    }
}