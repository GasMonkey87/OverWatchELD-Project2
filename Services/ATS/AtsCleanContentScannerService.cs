using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OverWatchELD.Services.ATS
{
    /// <summary>
    /// Create Load content scanner backed by AtsModScannerService.
    /// It prefers mods active in the current ATS profile, supports Steam Workshop,
    /// labels active/inactive sources, and keeps safe fallbacks so dropdowns never go blank.
    /// </summary>
    public sealed class AtsCleanContentScannerService
    {
        public Task<AtsContentScanResult> ScanAsync() => Task.Run(Scan);

        public AtsContentScanResult Scan()
        {
            // FAST BASE-ATS MODE:
            // Do not read ATS mod folders, Steam Workshop folders, .scs/.zip files,
            // manifests, or active mod profiles. This keeps login/Create Load fast and
            // limits generated loads to SCS/base ATS content only.
            var result = new AtsContentScanResult();

            result.ContentPacks.Add(new AtsContentPack
            {
                Id = "base-ats",
                DisplayName = "SCS ATS Base Loads",
                IsAll = true
            });

            try
            {
                AtsDataService.EnsureLoaded();

                var cargos = AtsDataService.Cargoes
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (cargos.Count == 0)
                {
                    cargos = new List<string>
                    {
                        "General Goods",
                        "Furniture",
                        "Food Products",
                        "Lumber",
                        "Machinery",
                        "Construction Materials"
                    };
                }

                foreach (var cargo in cargos)
                {
                    result.Cargo.Add(new AtsCargoOption
                    {
                        Token = ToToken(cargo),
                        Name = cargo.Trim(),
                        SourceMod = "SCS ATS Base",
                        WeightLbs = GuessWeightLbs(cargo)
                    });
                }

                var trailers = AtsDataService.Trailers
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (trailers.Count == 0)
                {
                    trailers = new List<string>
                    {
                        "Dry Van",
                        "Reefer",
                        "Flatbed",
                        "Lowboy",
                        "Tanker",
                        "Container Carrier",
                        "Car Hauler"
                    };
                }

                foreach (var trailer in trailers)
                {
                    result.Trailers.Add(new AtsTrailerOption
                    {
                        Token = ToToken(trailer),
                        Name = trailer.Trim(),
                        SourceMod = "SCS ATS Base"
                    });
                }

                var knownCities = AtsKnownCityService.All
                    .OrderBy(x => x.State)
                    .ThenBy(x => x.City)
                    .ToList();

                if (knownCities.Count > 0)
                {
                    foreach (var city in knownCities)
                    {
                        result.Companies.Add(new AtsCompanyOption
                        {
                            Token = AtsKnownCityService.TokenFor(city),
                            Name = city.City,
                            City = city.City,
                            State = city.State,
                            SourceMod = "SCS ATS Base"
                        });
                    }
                }
                else
                {
                    foreach (var city in AtsDataService.Cities
                        .Where(x => !string.IsNullOrWhiteSpace(x.City))
                        .OrderBy(x => x.State)
                        .ThenBy(x => x.City))
                    {
                        result.Companies.Add(new AtsCompanyOption
                        {
                            Token = ToToken(city.City),
                            Name = city.City,
                            City = city.City,
                            State = city.State,
                            SourceMod = "SCS ATS Base"
                        });
                    }
                }

                result.TechnicalLog.Add("FAST MODE: ATS mod/workshop scanning disabled.");
                result.TechnicalLog.Add("Create Load is using SCS/base ATS loads only.");
                result.TechnicalLog.Add($"Base content ready. Cargo={result.Cargo.Count}, Trailers={result.Trailers.Count}, Cities={result.Companies.Count}");
            }
            catch (Exception ex)
            {
                result.TechnicalLog.Add("Base ATS content load failed: " + ex.Message);
                AddFallback(result);
            }

            if (result.Cargo.Count == 0 || result.Trailers.Count == 0 || result.Companies.Count == 0)
                AddFallback(result);

            return result;
        }

        private static int GuessWeightLbs(string cargo)
        {
            cargo = (cargo ?? "").ToLowerInvariant();
            if (cargo.Contains("machinery") || cargo.Contains("equipment") || cargo.Contains("construction")) return 46000;
            if (cargo.Contains("lumber") || cargo.Contains("steel") || cargo.Contains("pipe")) return 44000;
            if (cargo.Contains("food") || cargo.Contains("frozen") || cargo.Contains("refrigerated")) return 38000;
            return 42000;
        }

        private static string ToToken(string value)
        {
            value = (value ?? "").Trim().ToLowerInvariant();
            value = Regex.Replace(value, @"[^a-z0-9]+", "_");
            value = Regex.Replace(value, @"_+", "_").Trim('_');
            return string.IsNullOrWhiteSpace(value) ? "base_item" : value;
        }

        private static string SourceLabel(string source, bool active, string note)
        {
            source = CleanSourceName(source);

            if (string.IsNullOrWhiteSpace(source))
                source = "ATS";

            if (source.Equals("Base Game Seed", StringComparison.OrdinalIgnoreCase) ||
                source.Contains("fallback", StringComparison.OrdinalIgnoreCase) ||
                source.Contains("vanilla", StringComparison.OrdinalIgnoreCase))
            {
                return "ATS Base";
            }

            // Do not expose verification wording in Create Load. The ViewModel will
            // show this clean mod name in brackets, for example: [B4RT Flatbed Pack].
            return source;
        }

        private static string CleanSourceName(string? value)
        {
            value = (value ?? "").Trim()
                .Replace("🟢", "")
                .Replace("🟡", "")
                .Replace("⚪", "")
                .Replace("🔴", "")
                .Trim();

            value = value.Replace("Installed Not Verified [scr]", "", StringComparison.OrdinalIgnoreCase).Trim();
            value = value.Replace("Installed / Not Verified [scr]", "", StringComparison.OrdinalIgnoreCase).Trim();
            value = value.Replace("Installed Not Verified", "", StringComparison.OrdinalIgnoreCase).Trim();
            value = value.Replace("Installed / Not Verified", "", StringComparison.OrdinalIgnoreCase).Trim();
            value = value.Replace("Verified", "", StringComparison.OrdinalIgnoreCase).Trim();
            value = value.Replace("Active Steam Workshop", "Steam Workshop", StringComparison.OrdinalIgnoreCase).Trim();
            value = value.Replace("Inactive Steam Workshop", "Steam Workshop", StringComparison.OrdinalIgnoreCase).Trim();
            value = value.Replace("Active Local", "Local", StringComparison.OrdinalIgnoreCase).Trim();
            value = value.Replace("Inactive Local", "Local", StringComparison.OrdinalIgnoreCase).Trim();
            value = value.Replace("Active -", "", StringComparison.OrdinalIgnoreCase).Trim();
            value = value.Replace("Inactive -", "", StringComparison.OrdinalIgnoreCase).Trim();

            while (value.StartsWith("-", StringComparison.Ordinal) || value.StartsWith("•", StringComparison.Ordinal))
                value = value.Substring(1).Trim();

            return value;
        }



        private static void ScanReadableLocalModArchives(AtsContentScanResult result)
        {
            try
            {
                foreach (var modRoot in GetAtsModFolders())
                {
                    if (!Directory.Exists(modRoot))
                        continue;

                    foreach (var file in Directory.EnumerateFiles(modRoot, "*.*", SearchOption.TopDirectoryOnly)
                                 .Where(f =>
                                     f.EndsWith(".scs", StringComparison.OrdinalIgnoreCase) ||
                                     f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
                    {
                        ScanReadableArchive(result, file);
                    }
                }
            }
            catch (Exception ex)
            {
                result.TechnicalLog.Add("Readable local mod archive scan skipped: " + ex.Message);
            }
        }

        private static IEnumerable<string> GetAtsModFolders()
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            yield return Path.Combine(docs, "American Truck Simulator", "mod");

            // Some systems place Documents under OneDrive.
            var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            yield return Path.Combine(user, "OneDrive", "Documents", "American Truck Simulator", "mod");
            yield return Path.Combine(user, "OneDrive", "My Documents", "American Truck Simulator", "mod");
        }


        private static bool TryExtractPackedScsAndScan(
            AtsContentScanResult result,
            string archivePath,
            string source,
            out string message)
        {
            message = "";

            try
            {
                var extractor = FindScsExtractor(archivePath);

                if (string.IsNullOrWhiteSpace(extractor) || !File.Exists(extractor))
                {
                    message = $"Packed/protected SCS skipped: {source}. Put scs_extractor.exe in the ATS mod folder or app folder to scan this locked archive.";
                    return false;
                }

                var cacheRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "OverWatchELD",
                    "AtsModCache",
                    SafeFolderName(source));

                if (Directory.Exists(cacheRoot))
                    Directory.Delete(cacheRoot, true);

                Directory.CreateDirectory(cacheRoot);

                var psi = new ProcessStartInfo
                {
                    FileName = extractor,
                    Arguments = $"\"{archivePath}\" \"{cacheRoot}\"",
                    WorkingDirectory = Path.GetDirectoryName(extractor) ?? "",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = Process.Start(psi);

                if (proc == null)
                {
                    message = $"SCS extractor failed to start for {source}.";
                    return false;
                }

                if (!proc.WaitForExit(60000))
                {
                    try { proc.Kill(true); } catch { }
                    message = $"SCS extractor timed out for {source}.";
                    return false;
                }

                var stdout = proc.StandardOutput.ReadToEnd();
                var stderr = proc.StandardError.ReadToEnd();

                if (proc.ExitCode != 0 && !Directory.EnumerateFiles(cacheRoot, "*.*", SearchOption.AllDirectories).Any())
                {
                    message = $"SCS extractor failed for {source}. Exit={proc.ExitCode}. {FirstNonBlank(stderr, stdout)}";
                    return false;
                }

                var beforeCargo = result.Cargo.Count;
                var beforeTrailers = result.Trailers.Count;

                ScanExtractedFolder(result, cacheRoot, source);

                var addedCargo = result.Cargo.Count - beforeCargo;
                var addedTrailers = result.Trailers.Count - beforeTrailers;

                message = $"Extracted locked SCS with scs_extractor: {source} cargo={addedCargo}, trailers={addedTrailers}";
                return addedCargo > 0 || addedTrailers > 0;
            }
            catch (Exception ex)
            {
                message = $"SCS extractor scan failed for {source}: {ex.Message}";
                return false;
            }
        }

        private static string? FindScsExtractor(string archivePath)
        {
            var names = new[]
            {
                "scs_extractor.exe",
                "SCS Extractor.exe",
                "scs_extractor"
            };

            var candidates = new List<string>();

            var archiveDir = Path.GetDirectoryName(archivePath);
            if (!string.IsNullOrWhiteSpace(archiveDir))
            {
                foreach (var name in names)
                    candidates.Add(Path.Combine(archiveDir, name));
            }

            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            foreach (var name in names)
                candidates.Add(Path.Combine(appDir, name));

            foreach (var modRoot in GetAtsModFolders())
            {
                foreach (var name in names)
                    candidates.Add(Path.Combine(modRoot, name));
            }

            return candidates.FirstOrDefault(File.Exists);
        }

        private static void ScanExtractedFolder(AtsContentScanResult result, string folder, string source)
        {
            if (!Directory.Exists(folder))
                return;

            foreach (var file in Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                         .Where(f =>
                             f.EndsWith(".sii", StringComparison.OrdinalIgnoreCase) ||
                             f.EndsWith(".sui", StringComparison.OrdinalIgnoreCase)))
            {
                var rel = Path.GetRelativePath(folder, file).Replace('\\', '/');
                var text = "";

                try
                {
                    text = File.ReadAllText(file);
                }
                catch
                {
                    continue;
                }

                if (LooksLikeCargoDefinition(rel))
                {
                    var token = TokenFromDefPath(rel);
                    var name = FirstNonBlank(ReadDisplayNameFromText(text), CleanTokenForDisplay(token));

                    if (!string.IsNullOrWhiteSpace(token))
                        AddCargoIfMissing(result, token, name, source, ReadWeightLbsFromText(text) ?? 42000);
                }

                if (LooksLikeTrailerDefinition(rel))
                {
                    var token = TokenFromDefPath(rel);
                    var name = FirstNonBlank(ReadDisplayNameFromText(text), CleanTokenForDisplay(token));

                    if (!string.IsNullOrWhiteSpace(token))
                        AddTrailerIfMissing(result, token, name, source);
                }

                foreach (var cargo in ExtractCargoTokensFromText(text))
                    AddCargoIfMissing(result, cargo.Token, cargo.Name, source, cargo.WeightLbs);
            }
        }

        private static string SafeFolderName(string value)
        {
            var s = string.IsNullOrWhiteSpace(value) ? "mod" : value.Trim();

            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');

            return s;
        }

        private static void ScanReadableArchive(AtsContentScanResult result, string archivePath)
        {
            var source = CleanArchiveSourceName(Path.GetFileNameWithoutExtension(archivePath));

            try
            {
                using var zip = ZipFile.OpenRead(archivePath);

                var addedCargo = 0;
                var addedTrailers = 0;

                foreach (var entry in zip.Entries)
                {
                    var path = (entry.FullName ?? "").Replace('\\', '/');

                    if (entry.Length <= 0)
                        continue;

                    if (!path.EndsWith(".sii", StringComparison.OrdinalIgnoreCase) &&
                        !path.EndsWith(".sui", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var entryText = ReadEntryText(entry);

                    if (LooksLikeCargoDefinition(path))
                    {
                        var token = TokenFromDefPath(path);
                        var name = FirstNonBlank(ReadDisplayNameFromText(entryText), CleanTokenForDisplay(token));

                        if (!string.IsNullOrWhiteSpace(token))
                        {
                            AddCargoIfMissing(result, token, name, source, ReadWeightLbsFromText(entryText) ?? 42000);
                            addedCargo++;
                        }
                    }

                    if (LooksLikeTrailerDefinition(path))
                    {
                        var token = TokenFromDefPath(path);
                        var name = FirstNonBlank(ReadDisplayNameFromText(entryText), CleanTokenForDisplay(token));

                        if (!string.IsNullOrWhiteSpace(token))
                        {
                            AddTrailerIfMissing(result, token, name, source);
                            addedTrailers++;
                        }
                    }

                    foreach (var cargo in ExtractCargoTokensFromText(entryText))
                    {
                        AddCargoIfMissing(result, cargo.Token, cargo.Name, source, cargo.WeightLbs);
                        addedCargo++;
                    }
                }

                if (addedCargo > 0 || addedTrailers > 0)
                    result.TechnicalLog.Add($"Readable mod archive scanned: {source} cargo={addedCargo}, trailers={addedTrailers}");
            }
            catch (InvalidDataException)
            {
                if (TryExtractPackedScsAndScan(result, archivePath, source, out var message))
                    result.TechnicalLog.Add(message);
                else
                    result.TechnicalLog.Add(message);
            }
            catch (Exception ex)
            {
                result.TechnicalLog.Add($"Readable archive scan failed for {source}: {ex.Message}");
            }
        }

        private static bool LooksLikeCargoDefinition(string path)
        {
            var p = path.ToLowerInvariant();

            return (p.Contains("/def/cargo/") ||
                    p.StartsWith("def/cargo/") ||
                    p.Contains("/cargo/") ||
                    p.Contains("cargo_def") ||
                    p.Contains("cargo_data") ||
                    p.Contains("cargo.") ||
                    p.Contains("_cargo")) &&
                   !p.Contains("accessory") &&
                   !p.Contains("paint") &&
                   !p.Contains("ui/");
        }

        private static bool LooksLikeTrailerDefinition(string path)
        {
            var p = path.ToLowerInvariant();

            return p.Contains("/def/vehicle/trailer_defs/") ||
                   p.StartsWith("def/vehicle/trailer_defs/") ||
                   p.Contains("/def/vehicle/trailer/") ||
                   p.StartsWith("def/vehicle/trailer/") ||
                   p.Contains("/def/trailer/") ||
                   p.StartsWith("def/trailer/");
        }

        private static string TokenFromDefPath(string path)
        {
            path = (path ?? "").Replace('\\', '/').Trim();

            var file = Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrWhiteSpace(file) &&
                !file.Equals("manifest", StringComparison.OrdinalIgnoreCase))
                return file.Trim();

            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

            for (var i = parts.Length - 1; i >= 0; i--)
            {
                var part = Path.GetFileNameWithoutExtension(parts[i]);
                if (!string.IsNullOrWhiteSpace(part) &&
                    !part.Equals("def", StringComparison.OrdinalIgnoreCase) &&
                    !part.Equals("cargo", StringComparison.OrdinalIgnoreCase) &&
                    !part.Equals("trailer", StringComparison.OrdinalIgnoreCase) &&
                    !part.Equals("vehicle", StringComparison.OrdinalIgnoreCase))
                {
                    return part.Trim();
                }
            }

            return "";
        }


        private static string ReadEntryText(ZipArchiveEntry entry)
        {
            try
            {
                using var stream = entry.Open();
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch
            {
                return "";
            }
        }

        private static string ReadDisplayNameFromText(string text)
        {
            var directName = MatchQuotedValue(text, "name")
                             ?? MatchQuotedValue(text, "display_name")
                             ?? MatchQuotedValue(text, "localized_name")
                             ?? MatchQuotedValue(text, "cargo_name");

            return string.IsNullOrWhiteSpace(directName)
                ? ""
                : CleanTokenForDisplay(directName);
        }

        private static int? ReadWeightLbsFromText(string text)
        {
            var kg = MatchNumberValue(text, "mass")
                     ?? MatchNumberValue(text, "weight")
                     ?? MatchNumberValue(text, "cargo_mass");

            if (kg.HasValue && kg.Value > 0)
                return (int)Math.Round(kg.Value * 2.20462);

            return null;
        }

        private static List<(string Token, string Name, int WeightLbs)> ExtractCargoTokensFromText(string text)
        {
            var list = new List<(string Token, string Name, int WeightLbs)>();

            if (string.IsNullOrWhiteSpace(text))
                return list;

            foreach (Match match in Regex.Matches(
                         text,
                         @"\b(?:cargo_data|cargo_def|cargo)\s*:\s*([A-Za-z0-9_\\.\\-/]+)",
                         RegexOptions.IgnoreCase))
            {
                var token = NormalizeUnitToken(match.Groups[1].Value);

                if (IsUsableCargoToken(token))
                    list.Add((token, CleanTokenForDisplay(token), 42000));
            }

            foreach (Match match in Regex.Matches(
                         text,
                         @"\b(?:cargo|compatible_cargo|allowed_cargo|cargoes)\s*\[\]\s*:\s*([A-Za-z0-9_\\.\\-/]+)",
                         RegexOptions.IgnoreCase))
            {
                var token = NormalizeUnitToken(match.Groups[1].Value);

                if (IsUsableCargoToken(token))
                    list.Add((token, CleanTokenForDisplay(token), 42000));
            }

            return list
                .GroupBy(x => x.Token, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        private static string NormalizeUnitToken(string? value)
        {
            value = (value ?? "").Trim().Trim('"', '\'');

            if (value.Contains("/", StringComparison.Ordinal))
                value = Path.GetFileNameWithoutExtension(value);

            if (value.StartsWith("cargo.", StringComparison.OrdinalIgnoreCase))
                value = value.Substring("cargo.".Length);

            if (value.StartsWith("trailer.", StringComparison.OrdinalIgnoreCase))
                value = value.Substring("trailer.".Length);

            if (value.StartsWith("trailer_def.", StringComparison.OrdinalIgnoreCase))
                value = value.Substring("trailer_def.".Length);

            return value.Trim();
        }

        private static bool IsUsableCargoToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return false;

            var t = token.ToLowerInvariant();

            return t.Length > 1 &&
                   !t.Contains("paint") &&
                   !t.Contains("accessory") &&
                   !t.Contains("sound") &&
                   !t.Contains("ui") &&
                   !t.Contains("icon") &&
                   !t.Contains("trailer_def");
        }

        private static string ReadEntryDisplayName(ZipArchiveEntry entry)
        {
            return ReadDisplayNameFromText(ReadEntryText(entry));
        }

        private static int? ReadEntryWeightLbs(ZipArchiveEntry entry)
        {
            return ReadWeightLbsFromText(ReadEntryText(entry));
        }

        private static string? MatchQuotedValue(string text, string key)
        {
            try
            {
                var match = Regex.Match(
                    text ?? "",
                    @"\b" + Regex.Escape(key) + @"\s*:\s*""([^""]+)""",
                    RegexOptions.IgnoreCase);

                return match.Success ? match.Groups[1].Value.Trim() : null;
            }
            catch
            {
                return null;
            }
        }

        private static double? MatchNumberValue(string text, string key)
        {
            try
            {
                var match = Regex.Match(
                    text ?? "",
                    @"\b" + Regex.Escape(key) + @"\s*:\s*([-+]?[0-9]*\.?[0-9]+)",
                    RegexOptions.IgnoreCase);

                if (match.Success &&
                    double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var value))
                    return value;
            }
            catch
            {
            }

            return null;
        }

        private static string CleanArchiveSourceName(string? value)
        {
            value = CleanSourceName(value);

            if (string.IsNullOrWhiteSpace(value))
                return "Local ATS Mod";

            return value
                .Replace("_", " ")
                .Replace("-", " ")
                .Trim();
        }

        private static string CleanTokenForDisplay(string? value)
        {
            value = (value ?? "").Trim();

            if (string.IsNullOrWhiteSpace(value))
                return "";

            value = value
                .Replace("@@", "")
                .Replace("_", " ")
                .Replace(".", " ")
                .Replace("/", " ")
                .Replace("-", " ")
                .Trim();

            while (value.Contains("  ", StringComparison.Ordinal))
                value = value.Replace("  ", " ");

            return string.Join(" ",
                value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(w => w.Length <= 1
                        ? w.ToUpperInvariant()
                        : char.ToUpperInvariant(w[0]) + w.Substring(1).ToLowerInvariant()));
        }

        private static string FirstNonBlank(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }

        private static void AddTntBlackhawkTrailerPack(AtsContentScanResult result)
        {
            const string source = "TNT Blackhawk Trailer V0.6";

            AddCargoIfMissing(
                result,
                token: "kal_hum",
                name: "M1114 Humvees",
                source: source,
                weightLbs: 15600);

            AddCargoIfMissing(
                result,
                token: "m1114_humvees",
                name: "M1114 Humvees",
                source: source,
                weightLbs: 15600);

            AddTrailerIfMissing(
                result,
                token: "dm",
                name: "Kalyn Siebert Stepdeck",
                source: source);

            AddTrailerIfMissing(
                result,
                token: "kalyn_siebert_stepdeck",
                name: "Kalyn Siebert Stepdeck",
                source: source);

            if (!result.TechnicalLog.Any(x => x.Contains("TNT Blackhawk", StringComparison.OrdinalIgnoreCase)))
                result.TechnicalLog.Add("Seeded TNT Blackhawk Trailer V0.6 cargo/trailer definitions for Create Load.");
        }

        private static void AddCargoIfMissing(
            AtsContentScanResult result,
            string token,
            string name,
            string source,
            int weightLbs)
        {
            if (result.Cargo.Any(x =>
                    string.Equals(x.Token, token, StringComparison.OrdinalIgnoreCase) ||
                    (string.Equals(CleanSourceName(x.SourceMod), source, StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))))
                return;

            result.Cargo.Add(new AtsCargoOption
            {
                Token = token,
                Name = name,
                SourceMod = source,
                WeightLbs = weightLbs
            });
        }

        private static void AddTrailerIfMissing(
            AtsContentScanResult result,
            string token,
            string name,
            string source)
        {
            if (result.Trailers.Any(x =>
                    string.Equals(x.Token, token, StringComparison.OrdinalIgnoreCase) ||
                    (string.Equals(CleanSourceName(x.SourceMod), source, StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))))
                return;

            result.Trailers.Add(new AtsTrailerOption
            {
                Token = token,
                Name = name,
                SourceMod = source
            });
        }

        private static void AddFallback(AtsContentScanResult result)
        {
            if (result.Cargo.Count == 0)
            {
                result.Cargo.Add(new AtsCargoOption { Token = "general_goods", Name = "General Goods", SourceMod = "Fallback ATS Seed", WeightLbs = 42000 });
                result.Cargo.Add(new AtsCargoOption { Token = "machinery", Name = "Machinery", SourceMod = "Fallback ATS Seed", WeightLbs = 46000 });
            }

            if (result.Trailers.Count == 0)
            {
                result.Trailers.Add(new AtsTrailerOption { Token = "auto", Name = "Auto Compatible Trailer", SourceMod = "Fallback ATS Seed" });
                result.Trailers.Add(new AtsTrailerOption { Token = "dryvan", Name = "Dry Van", SourceMod = "Fallback ATS Seed" });
                result.Trailers.Add(new AtsTrailerOption { Token = "flatbed", Name = "Flatbed", SourceMod = "Fallback ATS Seed" });
            }

            if (result.Companies.Count == 0)
            {
                foreach (var city in AtsKnownCityService.All.OrderBy(x => x.State).ThenBy(x => x.City))
                {
                    result.Companies.Add(new AtsCompanyOption
                    {
                        Token = AtsKnownCityService.TokenFor(city),
                        Name = city.City,
                        City = city.City,
                        State = city.State,
                        SourceMod = "Fallback ATS Seed"
                    });
                }
            }
        }
    }
}
