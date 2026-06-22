using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

namespace OverWatchELD.Services
{
    /// <summary>
    /// Finds ATS active local mods and Steam Workshop mods, then converts them into
    /// AtsDefinitionSource rows that AtsModScannerService can scan.
    /// </summary>
    public static class AtsActiveWorkshopModSourceService
    {
        private const string AtsSteamAppId = "270880";

        public static List<AtsDefinitionSource> DiscoverSources(string atsRoot, string modFolder, int basePriority = 5000)
        {
            var results = new List<AtsDefinitionSource>();

            try
            {
                var active = ReadActiveMods(atsRoot);
                var workshopFolders = FindSteamWorkshopFolders();

                AddActiveLocalMods(results, active, modFolder, basePriority + 100);
                AddSteamWorkshopMods(results, active, workshopFolders, basePriority + 500);
                AddLooseWorkshopMods(results, workshopFolders, basePriority + 900);
            }
            catch
            {
            }

            return results
                .GroupBy(x => x.FullPath, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderBy(x => x.Priority).First())
                .OrderBy(x => x.Priority)
                .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static AtsActiveModsSnapshot ReadActiveMods(string atsRoot)
        {
            var snapshot = new AtsActiveModsSnapshot
            {
                AtsRoot = atsRoot ?? ""
            };

            if (string.IsNullOrWhiteSpace(atsRoot) || !Directory.Exists(atsRoot))
                return snapshot;

            foreach (var file in EnumerateProfileModFiles(atsRoot))
            {
                try
                {
                    var text = File.ReadAllText(file);
                    snapshot.ProfileFiles.Add(file);

                    foreach (var token in ExtractModTokens(text))
                    {
                        snapshot.RawTokens.Add(token);

                        var workshopId = ExtractWorkshopId(token);
                        if (!string.IsNullOrWhiteSpace(workshopId))
                        {
                            snapshot.WorkshopIds.Add(workshopId);
                            continue;
                        }

                        var local = ExtractLocalPackageToken(token);
                        if (!string.IsNullOrWhiteSpace(local))
                            snapshot.LocalPackageTokens.Add(local);
                    }
                }
                catch
                {
                }
            }

            snapshot.RawTokens = snapshot.RawTokens
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            snapshot.WorkshopIds = snapshot.WorkshopIds
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            snapshot.LocalPackageTokens = snapshot.LocalPackageTokens
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return snapshot;
        }

        private static IEnumerable<string> EnumerateProfileModFiles(string atsRoot)
        {
            var candidates = new[]
            {
                Path.Combine(atsRoot, "profiles"),
                Path.Combine(atsRoot, "steam_profiles")
            };

            foreach (var root in candidates)
            {
                if (!Directory.Exists(root))
                    continue;

                foreach (var name in new[] { "mod_manager.sii", "profile.sii", "game.sii" })
                {
                    foreach (var file in Directory.EnumerateFiles(root, name, SearchOption.AllDirectories))
                        yield return file;
                }
            }
        }

        private static List<string> ExtractModTokens(string text)
        {
            var results = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
                return results;

            foreach (Match m in Regex.Matches(text, @"mod_(?:workshop_)?package\.[A-Za-z0-9_\.\-]+", RegexOptions.IgnoreCase))
                results.Add(m.Value.Trim());

            foreach (Match m in Regex.Matches(text, @"""([^""\r\n]*mod_(?:workshop_)?package\.[^""\r\n]+)""", RegexOptions.IgnoreCase))
                results.Add(m.Groups[1].Value.Trim());

            foreach (Match m in Regex.Matches(text, @"workshop[_\./\\-]?([0-9]{6,})", RegexOptions.IgnoreCase))
                results.Add("mod_workshop_package." + m.Groups[1].Value.Trim());

            return results
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string ExtractWorkshopId(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return "";

            var m = Regex.Match(token, @"([0-9]{6,})");
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        private static string ExtractLocalPackageToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return "";

            var value = token.Trim();

            value = Regex.Replace(value, @"^mod_package\.", "", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"^mod_workshop_package\.", "", RegexOptions.IgnoreCase);

            if (Regex.IsMatch(value, @"^[0-9]{6,}$"))
                return "";

            return value.Trim('.', ' ', '\t', '\r', '\n');
        }

        private static void AddActiveLocalMods(
            List<AtsDefinitionSource> results,
            AtsActiveModsSnapshot active,
            string modFolder,
            int priority)
        {
            if (string.IsNullOrWhiteSpace(modFolder) || !Directory.Exists(modFolder))
                return;

            foreach (var token in active.LocalPackageTokens)
            {
                var match = FindLocalModPath(modFolder, token);
                if (string.IsNullOrWhiteSpace(match))
                    continue;

                var kind = Directory.Exists(match)
                    ? AtsDefinitionSourceKind.Directory
                    : AtsDefinitionSourceKind.ZipArchive;

                Add(results, new AtsDefinitionSource
                {
                    Id = "active-local:" + NormalizeId(token),
                    DisplayName = "Active Local Mod - " + Path.GetFileNameWithoutExtension(match),
                    FullPath = match,
                    Kind = kind,
                    Priority = priority++
                });
            }
        }

        private static string FindLocalModPath(string modFolder, string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return "";

            var normalized = NormalizeId(token);

            foreach (var dir in Directory.GetDirectories(modFolder))
            {
                var name = NormalizeId(Path.GetFileName(dir));
                if (name.Equals(normalized, StringComparison.OrdinalIgnoreCase) || name.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                    return dir;
            }

            foreach (var file in Directory.GetFiles(modFolder))
            {
                var ext = Path.GetExtension(file);
                if (!ext.Equals(".scs", StringComparison.OrdinalIgnoreCase) &&
                    !ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                    continue;

                var name = NormalizeId(Path.GetFileNameWithoutExtension(file));
                if (name.Equals(normalized, StringComparison.OrdinalIgnoreCase) || name.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                    return file;
            }

            return "";
        }

        private static void AddSteamWorkshopMods(
            List<AtsDefinitionSource> results,
            AtsActiveModsSnapshot active,
            List<string> workshopFolders,
            int priority)
        {
            foreach (var id in active.WorkshopIds)
            {
                foreach (var root in workshopFolders)
                {
                    var itemFolder = Path.Combine(root, id);
                    if (!Directory.Exists(itemFolder))
                        continue;

                    AddWorkshopItem(results, itemFolder, id, true, ref priority);
                }
            }
        }

        private static void AddLooseWorkshopMods(
            List<AtsDefinitionSource> results,
            List<string> workshopFolders,
            int priority)
        {
            foreach (var root in workshopFolders)
            {
                if (!Directory.Exists(root))
                    continue;

                foreach (var itemFolder in Directory.GetDirectories(root))
                {
                    var id = Path.GetFileName(itemFolder);
                    AddWorkshopItem(results, itemFolder, id, false, ref priority);
                }
            }
        }

        private static void AddWorkshopItem(
            List<AtsDefinitionSource> results,
            string itemFolder,
            string workshopId,
            bool active,
            ref int priority)
        {
            var labelPrefix = active ? "Active Steam Workshop" : "Steam Workshop";
            var displayName = ReadWorkshopTitle(itemFolder);
            if (string.IsNullOrWhiteSpace(displayName))
                displayName = workshopId;

            foreach (var file in Directory.GetFiles(itemFolder, "*.*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file);
                if (!ext.Equals(".scs", StringComparison.OrdinalIgnoreCase) &&
                    !ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                    continue;

                Add(results, new AtsDefinitionSource
                {
                    Id = $"workshop:{workshopId}:{Path.GetFileName(file)}",
                    DisplayName = $"{labelPrefix} - {displayName}",
                    FullPath = file,
                    Kind = AtsDefinitionSourceKind.ZipArchive,
                    Priority = priority++
                });
            }

            if (DirectoryContainsDefs(itemFolder))
            {
                Add(results, new AtsDefinitionSource
                {
                    Id = $"workshop-dir:{workshopId}",
                    DisplayName = $"{labelPrefix} - {displayName}",
                    FullPath = itemFolder,
                    Kind = AtsDefinitionSourceKind.Directory,
                    Priority = priority++
                });
            }
        }

        private static bool DirectoryContainsDefs(string folder)
        {
            try
            {
                return Directory.EnumerateFiles(folder, "*.sii", SearchOption.AllDirectories).Any() ||
                       Directory.EnumerateFiles(folder, "*.sui", SearchOption.AllDirectories).Any();
            }
            catch
            {
                return false;
            }
        }

        private static string ReadWorkshopTitle(string itemFolder)
        {
            try
            {
                foreach (var file in Directory.GetFiles(itemFolder, "*.acf", SearchOption.TopDirectoryOnly))
                {
                    var text = File.ReadAllText(file);
                    var m = Regex.Match(text, "\"title\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                    if (m.Success)
                        return m.Groups[1].Value.Trim();
                }
            }
            catch
            {
            }

            return "";
        }

        private static List<string> FindSteamWorkshopFolders()
        {
            var results = new List<string>();

            foreach (var steamRoot in FindSteamRoots())
            {
                var root = Path.Combine(steamRoot, "steamapps", "workshop", "content", AtsSteamAppId);
                if (Directory.Exists(root))
                    results.Add(root);

                var libraryFile = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
                foreach (var lib in ReadSteamLibraryPaths(libraryFile))
                {
                    var workshop = Path.Combine(lib, "steamapps", "workshop", "content", AtsSteamAppId);
                    if (Directory.Exists(workshop))
                        results.Add(workshop);
                }
            }

            return results
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IEnumerable<string> FindSteamRoots()
        {
            var candidates = new List<string>();

            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            if (!string.IsNullOrWhiteSpace(programFilesX86))
                candidates.Add(Path.Combine(programFilesX86, "Steam"));

            if (!string.IsNullOrWhiteSpace(programFiles))
                candidates.Add(Path.Combine(programFiles, "Steam"));

            candidates.Add(@"C:\Steam");
            candidates.Add(@"D:\Steam");
            candidates.Add(@"E:\Steam");
            candidates.Add(@"F:\Steam");
            candidates.Add(@"G:\Steam");

            return candidates.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> ReadSteamLibraryPaths(string libraryFoldersVdf)
        {
            if (string.IsNullOrWhiteSpace(libraryFoldersVdf) || !File.Exists(libraryFoldersVdf))
                yield break;

            string text;
            try { text = File.ReadAllText(libraryFoldersVdf); }
            catch { yield break; }

            foreach (Match m in Regex.Matches(text, "\"path\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase))
            {
                var path = m.Groups[1].Value.Replace("\\\\", "\\").Trim();
                if (Directory.Exists(path))
                    yield return path;
            }
        }

        private static void Add(List<AtsDefinitionSource> results, AtsDefinitionSource source)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.FullPath))
                return;

            if (results.Any(x => string.Equals(x.FullPath, source.FullPath, StringComparison.OrdinalIgnoreCase)))
                return;

            results.Add(source);
        }

        private static string NormalizeId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            return Regex.Replace(value.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');
        }
    }

    public sealed class AtsActiveModsSnapshot
    {
        public string AtsRoot { get; set; } = "";
        public List<string> ProfileFiles { get; set; } = new();
        public List<string> RawTokens { get; set; } = new();
        public List<string> WorkshopIds { get; set; } = new();
        public List<string> LocalPackageTokens { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }
}
