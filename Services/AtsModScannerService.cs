using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OverWatchELD.Services
{
    public static class AtsModScannerService
    {
        private static readonly object _gate = new();

        private static AtsResolvedDefinitionCache? _cache;
        private static string _cacheKey = "";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static string SelectionFilePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "OverWatchELD",
                "ats_selected_mods.json");

        public static AtsResolvedDefinitionCache GetResolvedDefinitions()
        {
            lock (_gate)
            {
                try
                {
                    var selected = LoadSelectedSourceIds();
                    var scan = BuildScanContext(selected);
                    var key = scan.CacheKey;

                    if (_cache != null && string.Equals(_cacheKey, key, StringComparison.Ordinal))
                        return _cache;

                    var result = ResolveDefinitions(scan);
                    _cache = result;
                    _cacheKey = key;
                    return result;
                }
                catch (Exception ex)
                {
                    return new AtsResolvedDefinitionCache
                    {
                        GeneratedUtc = DateTime.UtcNow,
                        Warnings = new List<string> { "ATS mod scan failed: " + ex.Message }
                    };
                }
            }
        }

        public static AtsModScanResult ScanDefault() => AtsModScanResult.FromCache(GetResolvedDefinitions());
        public static AtsModScanResult GetScanResult() => AtsModScanResult.FromCache(GetResolvedDefinitions());
        public static AtsModScanResult Scan() => AtsModScanResult.FromCache(GetResolvedDefinitions());
        public static AtsModScanResult ScanMods() => AtsModScanResult.FromCache(GetResolvedDefinitions());

        public static object ReadLoadBoardOptionsPayload()
        {
            var resolved = GetResolvedDefinitions();

            return new
            {
                ok = true,
                generatedUtc = resolved.GeneratedUtc,
                modFolder = resolved.ModFolder,
                profileName = resolved.ProfileName,
                sourceCount = resolved.Sources.Count,
                selectedSourceIds = LoadSelectedSourceIds(),
                activeProfile = resolved.ActiveProfile,
                availableSources = GetAvailableSources()
                    .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new
                    {
                        id = x.Id,
                        name = x.DisplayName,
                        kind = x.Kind.ToString(),
                        detectedType = x.DetectedType.ToString(),
                        path = x.FullPath
                    })
                    .ToList(),
                cargoes = resolved.Cargoes
                    .OrderBy(x => x.SourceLabel, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new
                    {
                        id = x.Token,
                        name = x.DisplayName,
                        source = x.SourceLabel,
                        activeInProfile = x.IsActiveInProfile,
                        activeProfileNote = x.ActiveProfileNote,
                        trailers = x.AllowedTrailerTokens
                    })
                    .ToList(),
                trailers = resolved.Trailers
                    .OrderBy(x => x.ModGroup, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new
                    {
                        id = x.Token,
                        name = x.DisplayName,
                        source = x.SourceLabel,
                        modGroup = x.ModGroup,
                        activeInProfile = x.IsActiveInProfile,
                        activeProfileNote = x.ActiveProfileNote
                    })
                    .ToList(),
                companies = resolved.Companies
                    .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new
                    {
                        id = x.Token,
                        name = x.DisplayName,
                        city = x.CityToken,
                        source = x.SourceLabel
                    })
                    .ToList(),
                cities = resolved.Cities
                    .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new
                    {
                        id = x.Token,
                        name = x.DisplayName,
                        source = x.SourceLabel
                    })
                    .ToList(),
                warnings = resolved.Warnings
            };
        }

        public static List<AtsDefinitionSource> GetAvailableSources()
        {
            var ctx = BuildScanContext(null);
            return ctx.Sources
                .Where(x => x.Kind != AtsDefinitionSourceKind.BuiltInSeed)
                .OrderBy(x => x.Priority)
                .ToList();
        }

        public static List<string> LoadSelectedSourceIds()
        {
            try
            {
                if (!File.Exists(SelectionFilePath))
                    return new List<string>();

                var json = File.ReadAllText(SelectionFilePath);
                var model = JsonSerializer.Deserialize<AtsSelectedModsFile>(json, JsonOptions);

                return model?.SelectedSourceIds?
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                    ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        public static void SaveSelectedSourceIds(IEnumerable<string>? ids)
        {
            try
            {
                var dir = Path.GetDirectoryName(SelectionFilePath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                var model = new AtsSelectedModsFile
                {
                    SelectedSourceIds = ids?
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                        ?? new List<string>()
                };

                File.WriteAllText(SelectionFilePath, JsonSerializer.Serialize(model, JsonOptions));
            }
            catch
            {
            }

            Invalidate();
        }

        public static AtsResolvedCargoDef? FindCargo(string? tokenOrName)
        {
            var resolved = GetResolvedDefinitions();
            if (string.IsNullOrWhiteSpace(tokenOrName)) return null;

            var key = tokenOrName.Trim();
            return resolved.Cargoes.FirstOrDefault(x =>
                string.Equals(x.Token, key, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.DisplayName, key, StringComparison.OrdinalIgnoreCase));
        }

        public static AtsResolvedTrailerDef? FindTrailer(string? tokenOrName)
        {
            var resolved = GetResolvedDefinitions();
            if (string.IsNullOrWhiteSpace(tokenOrName)) return null;

            var key = tokenOrName.Trim();
            return resolved.Trailers.FirstOrDefault(x =>
                string.Equals(x.Token, key, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.DisplayName, key, StringComparison.OrdinalIgnoreCase));
        }

        public static AtsResolvedCompanyDef? FindCompany(string? tokenOrName)
        {
            var resolved = GetResolvedDefinitions();
            if (string.IsNullOrWhiteSpace(tokenOrName)) return null;

            var key = tokenOrName.Trim();
            return resolved.Companies.FirstOrDefault(x =>
                string.Equals(x.Token, key, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.DisplayName, key, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsTrailerAllowedForCargo(string? cargoTokenOrName, string? trailerTokenOrName)
        {
            var cargo = FindCargo(cargoTokenOrName);
            var trailer = FindTrailer(trailerTokenOrName);

            if (cargo == null || trailer == null)
                return false;

            if (cargo.AllowedTrailerTokens.Count == 0)
                return false;

            return cargo.AllowedTrailerTokens.Any(x =>
                string.Equals(x, trailer.Token, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x, trailer.DisplayName, StringComparison.OrdinalIgnoreCase));
        }

        public static void Invalidate()
        {
            lock (_gate)
            {
                _cache = null;
                _cacheKey = "";
            }
        }

        private static AtsResolvedDefinitionCache ResolveDefinitions(AtsScanContext ctx)
        {
            var result = new AtsResolvedDefinitionCache
            {
                GeneratedUtc = DateTime.UtcNow,
                ModFolder = ctx.ModFolder,
                ProfileName = ctx.ProfileName,
                ActiveProfile = ctx.ActiveProfile
            };

            var cargoMap = new Dictionary<string, AtsResolvedCargoDef>(StringComparer.OrdinalIgnoreCase);
            var trailerMap = new Dictionary<string, AtsResolvedTrailerDef>(StringComparer.OrdinalIgnoreCase);
            var companyMap = new Dictionary<string, AtsResolvedCompanyDef>(StringComparer.OrdinalIgnoreCase);
            var cityMap = new Dictionary<string, AtsResolvedCityDef>(StringComparer.OrdinalIgnoreCase);

            foreach (var src in ctx.Sources.OrderBy(x => x.Priority))
            {
                result.Sources.Add(src);

                try
                {
                    var files = (src.Kind switch
                    {
                        AtsDefinitionSourceKind.Directory => ReadDirectoryFiles(src),
                        AtsDefinitionSourceKind.ZipArchive => ReadArchiveFiles(src),
                        _ => Array.Empty<AtsTextDefFile>()
                    }).ToList();

                    src.DetectedType = DetectModType(files);

                    var sourceHasCargoTrailerText = files.Any(f =>
                        LooksLikeCargoFile(f.Path, f.Text) ||
                        LooksLikeTrailerFile(f.Path, f.Text));

                    var containsUsefulData =
                        files.Any(f =>
                            LooksLikeCargoFile(f.Path, f.Text) ||
                            LooksLikeTrailerFile(f.Path, f.Text));

                    if (src.Kind != AtsDefinitionSourceKind.BuiltInSeed && !containsUsefulData)
                    {
                        result.Warnings.Add(
                            $"Source '{src.DisplayName}' skipped because no cargo/trailer defs were found.");

                        continue;
                    }

                    var parsedCount = 0;

                    foreach (var file in files)
                    {
                        try
                        {
                            ParseFileIntoMaps(file, src, cargoMap, trailerMap, companyMap, cityMap, result.Warnings);
                            parsedCount++;
                        }
                        catch (Exception ex)
                        {
                            result.Warnings.Add($"Parse skipped for '{file.Path}': {ex.Message}");
                        }
                    }

                    result.Warnings.Add($"Source '{src.DisplayName}' scanned with {parsedCount} matching def files.");
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Source skipped '{src.DisplayName}': {ex.Message}");
                }
            }

            result.Cargoes = cargoMap.Values
                .Where(c => !string.IsNullOrWhiteSpace(c.Token))
                .OrderBy(x => x.SourceLabel, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            result.Trailers = trailerMap.Values
                .Where(t =>
                    !string.IsNullOrWhiteSpace(t.Token) &&
                    !t.DisplayName.Contains("paint", StringComparison.OrdinalIgnoreCase) &&
                    !t.DisplayName.Contains("accessory", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.ModGroup, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            result.Companies = companyMap.Values.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
            result.Cities = cityMap.Values.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();

            SeedFromAtsDataServiceIfEmpty(result);
            EnsureHardcodedFallbacksIfEmpty(result);

            return result;
        }

        private static AtsScanContext BuildScanContext(List<string>? selectedSourceIds)
        {
            // FAST MODE:
            // Do not scan Documents\American Truck Simulator\mod, Steam Workshop,
            // .scs files, zip archives, or manifest files. Startup/create-load speed
            // depends on this staying base-game only.
            var ctx = new AtsScanContext();

            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var atsRoot = Path.Combine(docs, "American Truck Simulator");
            var modFolder = Path.Combine(atsRoot, "mod");

            ctx.ModFolder = modFolder;
            ctx.ProfileName = "Base ATS";
            ctx.ActiveProfile = new AtsActiveModsSnapshot
            {
                AtsRoot = atsRoot
            };

            ctx.Sources = new List<AtsDefinitionSource>
            {
                BuildBuiltInBaseSource()
            };

            ctx.CacheKey = "base-ats-only|" + DateTime.UtcNow.Date.ToString("yyyyMMdd");
            return ctx;
        }

        private static AtsDefinitionSource BuildBuiltInBaseSource()
        {
            return new AtsDefinitionSource
            {
                Id = "builtin-base",
                DisplayName = "Base Game Seed",
                FullPath = "",
                Kind = AtsDefinitionSourceKind.BuiltInSeed,
                Priority = 0,
                IsActiveInProfile = true,
                ActiveProfileNote = "Base ATS content"
            };
        }

        private static void AddSourceIfMissing(List<AtsDefinitionSource> sources, AtsDefinitionSource source)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.Id))
                return;

            if (sources.Any(x => string.Equals(x.Id, source.Id, StringComparison.OrdinalIgnoreCase)))
                return;

            if (!string.IsNullOrWhiteSpace(source.FullPath) &&
                sources.Any(x => !string.IsNullOrWhiteSpace(x.FullPath) &&
                                 string.Equals(x.FullPath, source.FullPath, StringComparison.OrdinalIgnoreCase)))
                return;

            sources.Add(source);
        }

        private static IEnumerable<AtsTextDefFile> ReadDirectoryFiles(AtsDefinitionSource src)
        {
            var root = src.FullPath;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                yield break;

            foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file);
                if (!ext.Equals(".sii", StringComparison.OrdinalIgnoreCase) &&
                    !ext.Equals(".sui", StringComparison.OrdinalIgnoreCase) &&
                    !ext.Equals(".txt", StringComparison.OrdinalIgnoreCase))
                    continue;

                var rel = MakeRelative(root, file);
                if (!LooksLikeInterestingDef(rel))
                    continue;

                string text;
                try { text = File.ReadAllText(file); }
                catch { continue; }

                yield return new AtsTextDefFile
                {
                    Path = rel.Replace('\\', '/'),
                    Text = text
                };
            }
        }

        private static bool IsLikelyZipArchive(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return false;

                using var fs = File.OpenRead(path);
                if (fs.Length < 4)
                    return false;

                var b0 = fs.ReadByte();
                var b1 = fs.ReadByte();
                return b0 == 'P' && b1 == 'K';
            }
            catch
            {
                return false;
            }
        }

        private static string ResolveFriendlyModName(string file)
        {
            try
            {
                var manifestName = ParseManifestDisplayName(file);
                if (!string.IsNullOrWhiteSpace(manifestName))
                    return manifestName.Trim();
            }
            catch
            {
            }

            try
            {
                var descriptionName = ParseModDescriptionName(file);
                if (!string.IsNullOrWhiteSpace(descriptionName))
                    return descriptionName.Trim();
            }
            catch
            {
            }

            try
            {
                var name = Path.GetFileNameWithoutExtension(file);
                return string.IsNullOrWhiteSpace(name) ? "ATS Mod" : name.Trim();
            }
            catch
            {
                return "ATS Mod";
            }
        }

        private static string ParseManifestDisplayName(string modPath)
        {
            try
            {
                using var zip = ZipFile.OpenRead(modPath);

                var manifest = zip.Entries.FirstOrDefault(e =>
                    e.FullName.EndsWith("manifest.sii", StringComparison.OrdinalIgnoreCase));

                if (manifest == null)
                    return "";

                using var reader = new StreamReader(manifest.Open());
                var text = reader.ReadToEnd();

                foreach (var key in new[] { "display_name", "name", "mod_name" })
                {
                    var match = Regex.Match(
                        text,
                        $@"(?im)^\s*{Regex.Escape(key)}\s*:\s*""([^""]+)""");

                    if (match.Success)
                        return CleanModDisplayName(match.Groups[1].Value);
                }
            }
            catch
            {
            }

            return "";
        }

        private static string ParseModDescriptionName(string modPath)
        {
            try
            {
                using var zip = ZipFile.OpenRead(modPath);

                var desc = zip.Entries.FirstOrDefault(e =>
                    e.FullName.EndsWith("mod_description.txt", StringComparison.OrdinalIgnoreCase) ||
                    e.FullName.EndsWith("description.txt", StringComparison.OrdinalIgnoreCase));

                if (desc == null)
                    return "";

                using var reader = new StreamReader(desc.Open());
                var text = reader.ReadToEnd();

                var firstLine = text
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

                return CleanModDisplayName(firstLine ?? "");
            }
            catch
            {
                return "";
            }
        }

        private static string CleanModDisplayName(string? value)
        {
            value = (value ?? "").Trim();
            value = value.Replace("@@", "").Trim();

            if (string.IsNullOrWhiteSpace(value))
                return "";

            if (IsMostlyNumericText(value))
                return "";

            return value;
        }

        private static bool IsMostlyNumericText(string? value)
        {
            value = (value ?? "").Trim();
            if (value.Length < 6)
                return false;

            var digits = value.Count(char.IsDigit);
            return digits >= Math.Max(6, (int)(value.Length * 0.75));
        }

        private static IEnumerable<AtsTextDefFile> ReadArchiveFiles(AtsDefinitionSource src)
        {
            if (string.IsNullOrWhiteSpace(src.FullPath) || !File.Exists(src.FullPath))
                yield break;

            // IMPORTANT:
            // Do NOT reject .scs files only because the first bytes are not PK.
            // Many ATS/Steam Workshop .scs files are non-standard, have junk bytes,
            // or are packed differently. Try normal ZipFile first, then a memory
            // backed ZipArchive fallback. If both fail, skip safely.
            ZipArchive? zip = null;
            MemoryStream? backingMemory = null;

            try
            {
                try
                {
                    zip = ZipFile.OpenRead(src.FullPath);
                }
                catch
                {
                    try
                    {
                        using var fs = File.OpenRead(src.FullPath);
                        backingMemory = new MemoryStream();
                        fs.CopyTo(backingMemory);
                        backingMemory.Position = 0;

                        zip = new ZipArchive(backingMemory, ZipArchiveMode.Read, leaveOpen: false);
                    }
                    catch
                    {
                        backingMemory?.Dispose();
                        yield break;
                    }
                }

                List<ZipArchiveEntry> entries;

                try
                {
                    entries = zip.Entries.ToList();
                }
                catch
                {
                    yield break;
                }

                foreach (var entry in entries)
                {
                    string name;

                    try
                    {
                        name = (entry.FullName ?? "").Replace('\\', '/');
                    }
                    catch
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(name) || name.EndsWith("/", StringComparison.Ordinal))
                        continue;

                    var ext = Path.GetExtension(name);
                    if (!ext.Equals(".sii", StringComparison.OrdinalIgnoreCase) &&
                        !ext.Equals(".sui", StringComparison.OrdinalIgnoreCase) &&
                        !ext.Equals(".txt", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!LooksLikeInterestingDef(name))
                        continue;

                    string entryText;

                    try
                    {
                        using var stream = entry.Open();
                        using var reader = new StreamReader(stream);
                        entryText = reader.ReadToEnd();
                    }
                    catch
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(entryText))
                        continue;

                    yield return new AtsTextDefFile
                    {
                        Path = name,
                        Text = entryText
                    };
                }
            }
            finally
            {
                zip?.Dispose();
                backingMemory?.Dispose();
            }
        }

        private static bool LooksLikeInterestingDef(string path)
        {
            var p = (path ?? "").Replace('\\', '/').ToLowerInvariant();

            // Keep this intentionally broad. ATS mods put valid load content in many
            // slightly different folders and include files. Over-filtering here is what
            // caused Create Load to fall back to generic cargo/trailers only.
            if (p.EndsWith("manifest.sii")) return true;
            if (p.EndsWith(".sii")) return true;
            if (p.EndsWith(".sui")) return true;
            if (p.EndsWith(".txt")) return true;

            if (p.Contains("/def/")) return true;
            if (p.Contains("/cargo")) return true;
            if (p.Contains("/cargo_data")) return true;
            if (p.Contains("/cargoes")) return true;
            if (p.Contains("/cargo_market")) return true;
            if (p.Contains("/freight")) return true;
            if (p.Contains("/economy")) return true;
            if (p.Contains("/company")) return true;
            if (p.Contains("/city")) return true;
            if (p.Contains("/trailer")) return true;
            if (p.Contains("/trailer_owned")) return true;
            if (p.Contains("/trailer_defs")) return true;
            if (p.Contains("/trailer_cargo")) return true;

            return false;
        }

        private static AtsDetectedModType DetectModType(IEnumerable<AtsTextDefFile> files)
        {
            var list = (files ?? Array.Empty<AtsTextDefFile>()).ToList();

            foreach (var file in list)
            {
                var name = (file.Path ?? "").Replace('\\', '/').ToLowerInvariant();

                if (!name.EndsWith("manifest.sii"))
                    continue;

                var category = ExtractManifestCategory(file.Text);

                if (!string.IsNullOrWhiteSpace(category))
                {
                    var c = category.Trim().ToLowerInvariant();

                    if (c.Contains("trailer")) return AtsDetectedModType.Trailer;
                    if (c.Contains("cargo") || c.Contains("freight") || c.Contains("economy")) return AtsDetectedModType.Cargo;
                    if (c.Contains("truck")) return AtsDetectedModType.Truck;
                    if (c.Contains("map")) return AtsDetectedModType.Map;
                    if (c.Contains("sound")) return AtsDetectedModType.Sound;
                    if (c.Contains("graphic") || c.Contains("weather") || c.Contains("texture")) return AtsDetectedModType.Graphics;
                }
            }

            var hasTrailer = false;
            var hasTruck = false;
            var hasCargo = false;
            var hasMap = false;
            var hasSound = false;
            var hasGraphics = false;

            foreach (var file in list)
            {
                var p = (file.Path ?? "").Replace('\\', '/').ToLowerInvariant();
                var t = (file.Text ?? "").ToLowerInvariant();

                if (LooksLikeTrailerFile(p, t)) hasTrailer = true;
                if (LooksLikeCargoFile(p, t)) hasCargo = true;

                if (p.Contains("/def/vehicle/truck/") ||
                    p.Contains("/def/vehicle/truck_dealer/") ||
                    t.Contains("truck_data:") ||
                    t.Contains("accessory_truck_data:"))
                    hasTruck = true;

                if (p.Contains("/map/") ||
                    p.Contains("/def/city/") ||
                    p.Contains("/def/country/") ||
                    p.Contains("/def/world/") ||
                    t.Contains("city_data:") ||
                    t.Contains("country_data:") ||
                    t.Contains("map_data:"))
                    hasMap = true;

                if (p.Contains("/def/sound/") || p.Contains("/sound/"))
                    hasSound = true;

                if (p.Contains("/material/") || p.Contains("/model/") || p.Contains("/automat/"))
                    hasGraphics = true;
            }

            if (hasTrailer) return AtsDetectedModType.Trailer;
            if (hasCargo) return AtsDetectedModType.Cargo;
            if (hasTruck) return AtsDetectedModType.Truck;
            if (hasMap) return AtsDetectedModType.Map;
            if (hasSound) return AtsDetectedModType.Sound;
            if (hasGraphics) return AtsDetectedModType.Graphics;

            return AtsDetectedModType.Unknown;
        }

        private static string ExtractManifestCategory(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            var m = Regex.Match(text, @"(?im)^\s*category\s*:\s*""?([^""\r\n]+)""?");
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        private static void ParseFileIntoMaps(
            AtsTextDefFile file,
            AtsDefinitionSource src,
            Dictionary<string, AtsResolvedCargoDef> cargoMap,
            Dictionary<string, AtsResolvedTrailerDef> trailerMap,
            Dictionary<string, AtsResolvedCompanyDef> companyMap,
            Dictionary<string, AtsResolvedCityDef> cityMap,
            List<string> warnings)
        {
            var path = file.Path.Replace('\\', '/');
            var norm = NormalizeText(file.Text);
            var lower = path.ToLowerInvariant();

            var looksCargo = LooksLikeCargoFile(lower, norm);
            var looksTrailer = LooksLikeTrailerFile(lower, norm);

            if (looksCargo)
            {
                var cargo = ParseCargo(path, norm, src);
                if (cargo != null)
                    cargoMap[cargo.Token] = cargo;
            }

            if (looksTrailer && !IsPaintJobDef(path, norm))
            {
                var trailer = ParseTrailer(path, norm, src);
                if (trailer != null)
                    trailerMap[trailer.Token] = trailer;
            }

            if (lower.Contains("/def/company/") && !lower.Contains("/#cargo_definitions/"))
            {
                var company = ParseCompany(path, norm, src);
                if (company != null)
                    companyMap[company.Token] = company;
            }

            if (lower.Contains("/def/city/"))
            {
                var city = ParseCity(path, norm, src);
                if (city != null)
                    cityMap[city.Token] = city;
            }

            if (looksCargo)
            {
                var token = ExtractToken(norm, "cargo_data")
                    ?? ExtractToken(norm, "freight_data")
                    ?? ExtractToken(norm, "cargo_def")
                    ?? BuildTokenFromDefPath(path);

                if (cargoMap.TryGetValue(token, out var cargo))
                {
                    AddTrailerTokensFromText(cargo, norm);
                }
            }
        }

        private static bool LooksLikeCargoFile(string path, string text)
        {
            path ??= "";
            text ??= "";

            path = path.Replace("\\", "/").ToLowerInvariant();
            text = text.ToLowerInvariant();

            // Cargo-only but wide enough for heavy/military/trailer cargo packs.
            // This keeps dogs/cars/truck accessories out while bringing back
            // Blackhawk/helicopter/heavy equipment style cargo definitions.
            var validCargoPath =
                path.Contains("/def/cargo/") ||
                path.Contains("/def/cargo_data/") ||
                path.Contains("/cargo.") ||
                path.Contains("/cargo_market/") ||
                path.Contains("/freight/") ||
                path.Contains("/trailer_cargo/") ||
                path.Contains("/cargo_pack/") ||
                path.Contains("/heavy_cargo/") ||
                path.Contains("/military/") ||
                path.Contains("/def/company/#cargo_definitions/");

            if (!validCargoPath)
                return false;

            return
                text.Contains("cargo_data") ||
                text.Contains("freight_data") ||
                text.Contains("cargo.") ||
                text.Contains("cargo_name") ||
                text.Contains("cargo_def") ||
                text.Contains("mass:") ||
                text.Contains("fragility:") ||
                text.Contains("cargo_market") ||
                text.Contains("cargo_mass") ||
                text.Contains("body_type:") ||
                text.Contains("unit_reward_per_km");
        }

        private static bool LooksLikeTrailerFile(string path, string text)
        {
            path ??= "";
            text ??= "";

            path = path.Replace("\\", "/").ToLowerInvariant();
            text = text.ToLowerInvariant();

            var validTrailerPath =
                path.Contains("/def/vehicle/trailer/") ||
                path.Contains("/def/vehicle/trailer_owned/") ||
                path.Contains("/def/vehicle/trailer_defs/") ||
                path.Contains("/def/vehicle/trailer_chains/") ||
                path.Contains("/def/vehicle/trailer_storage/") ||
                path.Contains("/def/vehicle/trailer_cargo/") ||
                path.Contains("/def/vehicle/trailer_defs") ||
                path.Contains("/trailer_defs/") ||
                path.Contains("/trailer_owned/") ||
                path.Contains("/trailer/");

            if (!validTrailerPath)
                return false;

            return
                text.Contains("trailer_definition") ||
                text.Contains("trailer_data") ||
                text.Contains("trailer_owned_data") ||
                text.Contains("trailer_def") ||
                text.Contains("trailer_defs") ||
                text.Contains("body_type:") ||
                text.Contains("chain_type:") ||
                text.Contains("trailer_body_type") ||
                text.Contains("accessory_trailer_data");
        }

        private static void AddTrailerTokensFromText(AtsResolvedCargoDef cargo, string text)
        {
            foreach (Match m in Regex.Matches(
                text ?? "",
                @"(?im)^\s*(trailer|trailer_defs|trailer_def|trailer_variant|trailer_variants|body_type|chain_type)(\[\d+\])?\s*:\s*([^\r\n]+)$"))
            {
                foreach (var t in ExtractQuotedOrBareTokens(m.Groups[3].Value))
                {
                    if (!cargo.AllowedTrailerTokens.Contains(t, StringComparer.OrdinalIgnoreCase))
                        cargo.AllowedTrailerTokens.Add(t);
                }
            }
        }

        private static bool IsExpansionMapMod(string path, string text)
        {
            var p = (path ?? "").Replace('\\', '/').ToLowerInvariant();
            var t = (text ?? "").ToLowerInvariant();

            return p.Contains("/map/")
                || p.Contains("/def/city/")
                || p.Contains("/def/country/")
                || p.Contains("/def/world/")
                || p.Contains("/def/ferry/")
                || p.Contains("/def/road_look/")
                || p.Contains("/def/sign/")
                || p.Contains("/def/terrain_profile/")
                || t.Contains("map_data")
                || t.Contains("city_data")
                || t.Contains("country_data")
                || t.Contains("ferry_data");
        }

        private static bool LooksLikeTrailerDefPath(string lowerPath)
        {
            lowerPath = (lowerPath ?? "").Replace('\\', '/').ToLowerInvariant();

            return lowerPath.Contains("/def/vehicle/trailer_defs/")
                || lowerPath.Contains("/def/vehicle/trailer_owned/")
                || lowerPath.Contains("/def/vehicle/trailer/");
        }

        private static bool IsPaintJobDef(string path, string text)
        {
            var p = (path ?? "").Replace('\\', '/').ToLowerInvariant();
            var t = (text ?? "").ToLowerInvariant();

            return p.Contains("paint_job")
                || p.Contains("paintjob")
                || t.Contains("paint_job_data")
                || t.Contains("accessory_paint_job_data")
                || t.Contains("paint_job:");
        }

        private static AtsResolvedCargoDef? ParseCargo(string path, string text, AtsDefinitionSource src)
        {
            var token =
                ExtractToken(text, "cargo_data") ??
                ExtractToken(text, "freight_data") ??
                ExtractToken(text, "cargo_def") ??
                BuildTokenFromDefPath(path);

            if (string.IsNullOrWhiteSpace(token))
                return null;

            var rawDisplay =
                ExtractString(text, "cargo_name") ??
                ExtractString(text, "name") ??
                ExtractString(text, "display_name") ??
                HumanizeToken(token);

            var cargo = new AtsResolvedCargoDef
            {
                Token = token,
                DisplayName = HumanizeToken(rawDisplay),
                SourceId = src.Id,
                SourceLabel = src.DisplayName,
                Priority = src.Priority,
                IsActiveInProfile = src.IsActiveInProfile,
                ActiveProfileNote = src.ActiveProfileNote
            };

            AddTrailerTokensFromText(cargo, text);

            return cargo;
        }

        private static string? ExtractToken(string text, string block)
        {
            var m = Regex.Match(
                text ?? "",
                $@"(?im)^\s*{Regex.Escape(block)}\s*:\s*""?([^""\s{{]+)");

            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        private static AtsResolvedTrailerDef? ParseTrailer(string path, string text, AtsDefinitionSource src)
        {
            if (IsPaintJobDef(path, text))
                return null;

            var token =
                ExtractToken(text, "trailer_definition") ??
                ExtractToken(text, "trailer_data") ??
                ExtractToken(text, "trailer_owned_data") ??
                ExtractToken(text, "accessory_trailer_data") ??
                ExtractTrailerToken(path, text);

            if (string.IsNullOrWhiteSpace(token))
                return null;

            var display =
                ExtractString(text, "trailer_name") ??
                ExtractString(text, "name") ??
                ExtractString(text, "display_name") ??
                HumanizeToken(token);

            return new AtsResolvedTrailerDef
            {
                Token = NormalizeTrailerToken(token),
                DisplayName = HumanizeToken(display),
                SourceId = src.Id,
                SourceLabel = src.DisplayName,
                ModGroup = HumanizeToken(src.DisplayName),
                Priority = src.Priority,
                IsActiveInProfile = src.IsActiveInProfile,
                ActiveProfileNote = src.ActiveProfileNote
            };
        }

        private static string NormalizeTrailerToken(string token)
        {
            token = (token ?? "").Trim();

            if (string.IsNullOrWhiteSpace(token))
                return "";

            if (token.StartsWith("trailer.", StringComparison.OrdinalIgnoreCase))
                return token;

            if (token.StartsWith("trailer_def.", StringComparison.OrdinalIgnoreCase))
                return "trailer." + token.Substring("trailer_def.".Length);

            if (token.StartsWith("trailer_defs.", StringComparison.OrdinalIgnoreCase))
                return "trailer." + token.Substring("trailer_defs.".Length);

            return "trailer." + token;
        }

        private static string ExtractTrailerToken(string path, string text)
        {
            foreach (Match m in Regex.Matches(text, @"(?im)^\s*(trailer_definition|trailer_data|trailer_owned_data|accessory_trailer_data)\s*:\s*([a-z0-9\._]+)"))
            {
                var token = m.Groups[2].Value.Trim();
                if (!string.IsNullOrWhiteSpace(token))
                    return NormalizeTrailerToken(token);
            }

            var lowerPath = (path ?? "").Replace('\\', '/').ToLowerInvariant();
            if (!LooksLikeTrailerFile(lowerPath, text))
                return "";

            var fromPath = BuildTokenFromDefPath(path);
            if (string.IsNullOrWhiteSpace(fromPath))
                return "";

            if (fromPath.StartsWith("def.", StringComparison.OrdinalIgnoreCase))
                fromPath = fromPath.Substring("def.".Length);

            if (fromPath.StartsWith("vehicle.trailer_defs.", StringComparison.OrdinalIgnoreCase))
                return "trailer." + fromPath.Substring("vehicle.trailer_defs.".Length);

            if (fromPath.StartsWith("vehicle.trailer_owned.", StringComparison.OrdinalIgnoreCase))
                return "trailer." + fromPath.Substring("vehicle.trailer_owned.".Length);

            if (fromPath.StartsWith("vehicle.trailer.", StringComparison.OrdinalIgnoreCase))
                return "trailer." + fromPath.Substring("vehicle.trailer.".Length);

            return NormalizeTrailerToken(fromPath);
        }

        private static AtsResolvedCompanyDef? ParseCompany(string path, string text, AtsDefinitionSource src)
        {
            var token = BuildTokenFromDefPath(path);
            if (string.IsNullOrWhiteSpace(token))
                return null;

            var display =
                ExtractString(text, "company_name") ??
                ExtractString(text, "name") ??
                HumanizeToken(token);

            var city = ExtractString(text, "city") ?? ExtractBareValue(text, "city") ?? "";

            return new AtsResolvedCompanyDef
            {
                Token = token,
                DisplayName = display,
                CityToken = city,
                SourceId = src.Id,
                SourceLabel = src.DisplayName,
                Priority = src.Priority,
                IsActiveInProfile = src.IsActiveInProfile,
                ActiveProfileNote = src.ActiveProfileNote
            };
        }

        private static AtsResolvedCityDef? ParseCity(string path, string text, AtsDefinitionSource src)
        {
            var token = BuildTokenFromDefPath(path);
            if (string.IsNullOrWhiteSpace(token))
                return null;

            var display =
                ExtractString(text, "city_name") ??
                ExtractString(text, "name") ??
                HumanizeToken(token);

            return new AtsResolvedCityDef
            {
                Token = token,
                DisplayName = display,
                SourceId = src.Id,
                SourceLabel = src.DisplayName,
                Priority = src.Priority,
                IsActiveInProfile = src.IsActiveInProfile,
                ActiveProfileNote = src.ActiveProfileNote
            };
        }

        private static void ApplyActiveProfileFlagsToAll(List<AtsDefinitionSource> sources, AtsActiveModsSnapshot activeProfile)
        {
            foreach (var source in sources)
                ApplyActiveProfileFlags(source, activeProfile);
        }

        private static void ApplyActiveProfileFlags(AtsDefinitionSource source, AtsActiveModsSnapshot activeProfile)
        {
            if (source == null)
                return;

            if (!string.IsNullOrWhiteSpace(source.FullPath) && File.Exists(source.FullPath))
            {
                var friendlyName = ResolveFriendlyModName(source.FullPath);
                if (!string.IsNullOrWhiteSpace(friendlyName) &&
                    !IsMostlyNumericText(friendlyName) &&
                    !friendlyName.Equals(Path.GetFileName(source.FullPath), StringComparison.OrdinalIgnoreCase))
                {
                    source.DisplayName = friendlyName;
                }
            }

            if (source.Kind == AtsDefinitionSourceKind.BuiltInSeed)
            {
                source.IsActiveInProfile = true;
                source.ActiveProfileNote = "Base ATS content";
                return;
            }

            var display = source.DisplayName ?? "";
            var fullPath = source.FullPath ?? "";
            var fileNoExt = Path.GetFileNameWithoutExtension(fullPath);
            var fileName = Path.GetFileName(fullPath);

            if (display.StartsWith("Active Steam Workshop", StringComparison.OrdinalIgnoreCase) ||
                source.Id.StartsWith("active-local:", StringComparison.OrdinalIgnoreCase))
            {
                source.IsActiveInProfile = true;
                source.ActiveProfileNote = "Active in ATS profile";
                return;
            }

            var workshopId = ExtractWorkshopIdFromText(source.Id + " " + source.FullPath + " " + source.DisplayName);
            if (!string.IsNullOrWhiteSpace(workshopId) &&
                activeProfile.WorkshopIds.Any(x => string.Equals(x, workshopId, StringComparison.OrdinalIgnoreCase)))
            {
                source.IsActiveInProfile = true;
                source.ActiveProfileNote = "Active Steam Workshop mod in ATS profile";
                return;
            }

            if (IsLocalSourceActiveInProfile(fileNoExt, activeProfile) ||
                IsLocalSourceActiveInProfile(fileName, activeProfile) ||
                IsLocalSourceActiveInProfile(display, activeProfile))
            {
                source.IsActiveInProfile = true;
                source.ActiveProfileNote = "Active local mod in ATS profile";
                return;
            }

            source.IsActiveInProfile = false;
            source.ActiveProfileNote = activeProfile.RawTokens.Count == 0
                ? "ATS active profile mods could not be read; treated as available but unverified"
                : "Installed but not active in the current ATS profile";
        }

        private static bool IsLocalSourceActiveInProfile(string? value, AtsActiveModsSnapshot activeProfile)
        {
            var normalized = NormalizeProfileToken(value);
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            return activeProfile.LocalPackageTokens.Any(x =>
            {
                var token = NormalizeProfileToken(x);
                return token.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
                       normalized.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                       token.Contains(normalized, StringComparison.OrdinalIgnoreCase);
            });
        }

        private static string ExtractWorkshopIdFromText(string? value)
        {
            var m = Regex.Match(value ?? "", @"([0-9]{6,})");
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        private static string NormalizeProfileToken(string? value)
        {
            value = (value ?? "").Trim().ToLowerInvariant();
            value = Regex.Replace(value, @"^active\-local:", "");
            value = Regex.Replace(value, @"^mod_package\.", "");
            value = Regex.Replace(value, @"^mod_workshop_package\.", "");
            value = Path.GetFileNameWithoutExtension(value);
            return Regex.Replace(value, @"[^a-z0-9]+", "_").Trim('_');
        }

        private static void EnsureHardcodedFallbacksIfEmpty(AtsResolvedDefinitionCache result)
        {
            if (result.Cargoes.Count == 0)
            {
                foreach (var x in new[]
                {
                    "General Goods",
                    "Food Products",
                    "Electronics",
                    "Machinery",
                    "Vehicles",
                    "Furniture",
                    "Medical Supplies",
                    "Construction Materials"
                })
                {
                    result.Cargoes.Add(new AtsResolvedCargoDef
                    {
                        Token = MakeSafeSeedToken(x),
                        DisplayName = x,
                        SourceId = "fallback-seed",
                        SourceLabel = "Fallback ATS Seed",
                        Priority = int.MaxValue,
                        IsActiveInProfile = true,
                        ActiveProfileNote = "Fallback content"
                    });
                }

                result.Warnings.Add("Fallback cargo seed was used because scanner returned no cargo definitions.");
            }

            if (result.Trailers.Count == 0)
            {
                foreach (var x in new[]
                {
                    "Dry Van",
                    "Reefer",
                    "Flatbed",
                    "Lowboy",
                    "Tanker",
                    "Container Chassis"
                })
                {
                    result.Trailers.Add(new AtsResolvedTrailerDef
                    {
                        Token = MakeSafeSeedToken(x),
                        DisplayName = x,
                        SourceId = "fallback-seed",
                        SourceLabel = "Fallback ATS Seed",
                        ModGroup = "Fallback ATS Seed",
                        Priority = int.MaxValue,
                        IsActiveInProfile = true,
                        ActiveProfileNote = "Fallback content"
                    });
                }

                result.Warnings.Add("Fallback trailer seed was used because scanner returned no trailer definitions.");
            }

            if (result.Companies.Count == 0)
            {
                foreach (var x in new[]
                {
                    "Wallbert",
                    "SellGoods",
                    "Charged",
                    "Bushnell Farms",
                    "Voltison",
                    "NAMIQ",
                    "Deepgrove"
                })
                {
                    result.Companies.Add(new AtsResolvedCompanyDef
                    {
                        Token = MakeSafeSeedToken(x),
                        DisplayName = x,
                        CityToken = "",
                        SourceId = "fallback-seed",
                        SourceLabel = "Fallback ATS Seed",
                        Priority = int.MaxValue,
                        IsActiveInProfile = true,
                        ActiveProfileNote = "Fallback content"
                    });
                }

                result.Warnings.Add("Fallback company seed was used because scanner returned no company definitions.");
            }

            if (result.Cities.Count == 0)
            {
                foreach (var x in new[]
                {
                    "Phoenix, AZ",
                    "Dallas, TX",
                    "Los Angeles, CA",
                    "Denver, CO",
                    "Seattle, WA",
                    "Oklahoma City, OK",
                    "Chicago, IL",
                    "Springfield, IL",
                    "Rockford, IL",
                    "Peoria, IL",
                    "Champaign, IL",
                    "Bloomington, IL",
                    "Moline, IL",
                    "Quincy, IL",
                    "Effingham, IL",
                    "Marion, IL",
                    "East St. Louis, IL",
                    "Joliet, IL",
                    "Decatur, IL",
                    "Calgary, AB",
                    "Monterrey, MX"
                })
                {
                    result.Cities.Add(new AtsResolvedCityDef
                    {
                        Token = MakeSafeSeedToken(x),
                        DisplayName = x,
                        SourceId = "fallback-seed",
                        SourceLabel = "Fallback ATS Seed",
                        Priority = int.MaxValue,
                        IsActiveInProfile = true,
                        ActiveProfileNote = "Fallback content"
                    });
                }

                result.Warnings.Add("Fallback city seed was used because scanner returned no city definitions.");
            }
        }

        private static string MakeSafeSeedToken(string value)
        {
            value = (value ?? "").Trim().ToLowerInvariant();

            var chars = value.Select(ch =>
                char.IsLetterOrDigit(ch) ? ch :
                ch == ',' ? '.' :
                ch == '-' ? '_' :
                char.IsWhiteSpace(ch) ? '_' :
                '_').ToArray();

            var token = new string(chars);

            while (token.Contains("__", StringComparison.Ordinal))
                token = token.Replace("__", "_", StringComparison.Ordinal);

            return token.Trim('_', '.');
        }

        private static void SeedFromAtsDataServiceIfEmpty(AtsResolvedDefinitionCache result)
        {
            if (result.Cargoes.Count == 0)
            {
                foreach (var x in AtsDataService.Cargos
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    result.Cargoes.Add(new AtsResolvedCargoDef
                    {
                        Token = x.Trim(),
                        DisplayName = x.Trim(),
                        SourceId = "vanilla",
                        SourceLabel = "Vanilla ATS",
                        Priority = int.MaxValue,
                        IsActiveInProfile = true,
                        ActiveProfileNote = "Fallback content"
                    });
                }
            }

            if (result.Trailers.Count == 0)
            {
                foreach (var x in AtsDataService.GetAllowedTrailers(null)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    result.Trailers.Add(new AtsResolvedTrailerDef
                    {
                        Token = x.Trim(),
                        DisplayName = x.Trim(),
                        SourceId = "vanilla",
                        SourceLabel = "Vanilla ATS",
                        Priority = int.MaxValue,
                        IsActiveInProfile = true,
                        ActiveProfileNote = "Fallback content"
                    });
                }
            }

            if (result.Companies.Count == 0)
            {
                foreach (var x in AtsDataService.Companies
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    result.Companies.Add(new AtsResolvedCompanyDef
                    {
                        Token = x.Trim(),
                        DisplayName = x.Trim(),
                        CityToken = "",
                        SourceId = "vanilla",
                        SourceLabel = "Vanilla ATS",
                        Priority = int.MaxValue,
                        IsActiveInProfile = true,
                        ActiveProfileNote = "Fallback content"
                    });
                }
            }

            if (result.Cities.Count == 0)
            {
                foreach (var x in AtsDataService.GetCitiesByState(null)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    result.Cities.Add(new AtsResolvedCityDef
                    {
                        Token = x.Trim(),
                        DisplayName = x.Trim(),
                        SourceId = "vanilla",
                        SourceLabel = "Vanilla ATS",
                        Priority = int.MaxValue,
                        IsActiveInProfile = true,
                        ActiveProfileNote = "Fallback content"
                    });
                }
            }
        }

        private static string NormalizeText(string text)
        {
            return (text ?? "")
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');
        }

        private static string MakeRelative(string root, string file)
        {
            try
            {
                return Path.GetRelativePath(root, file).Replace('\\', '/');
            }
            catch
            {
                return file.Replace('\\', '/');
            }
        }

        private static string BuildTokenFromDefPath(string path)
        {
            var p = (path ?? "").Replace('\\', '/').Trim('/');
            var lower = p.ToLowerInvariant();

            if (lower.Contains("/def/company/#cargo_definitions/") ||
                lower.Contains("/def/cargo/") ||
                lower.Contains("/def/cargoes/") ||
                lower.Contains("/cargo_market/") ||
                lower.Contains("/freight/") ||
                lower.Contains("/trailer_cargo/"))
            {
                var file = Path.GetFileNameWithoutExtension(p);
                if (!string.IsNullOrWhiteSpace(file))
                    return "cargo." + file.Trim().ToLowerInvariant();
            }

            if (lower.Contains("/def/company/"))
            {
                var file = Path.GetFileNameWithoutExtension(p);
                if (!string.IsNullOrWhiteSpace(file))
                    return "company." + file.Trim().ToLowerInvariant();
            }

            if (lower.Contains("/def/city/"))
            {
                var file = Path.GetFileNameWithoutExtension(p);
                if (!string.IsNullOrWhiteSpace(file))
                    return "city." + file.Trim().ToLowerInvariant();
            }

            p = Path.ChangeExtension(p, null) ?? p;
            return p.Replace('/', '.').Trim('.');
        }

        private static string? ExtractString(string text, string key)
        {
            var rx = new Regex($@"(?im)^\s*{Regex.Escape(key)}\s*:\s*""([^""]+)""\s*$");
            var m = rx.Match(text ?? "");
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        private static string? ExtractBareValue(string text, string key)
        {
            var rx = new Regex($@"(?im)^\s*{Regex.Escape(key)}\s*:\s*([^\r\n""]+)\s*$");
            var m = rx.Match(text ?? "");
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        private static IEnumerable<string> ExtractQuotedOrBareTokens(string text)
        {
            var results = new List<string>();
            var input = (text ?? "").Trim();

            foreach (Match m in Regex.Matches(input, @"""([^""]+)"""))
            {
                var value = m.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    results.Add(value);
            }

            if (results.Count == 0)
            {
                foreach (var piece in input.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var value = piece.Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                        results.Add(value);
                }
            }

            return results
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string HumanizeToken(string token)
        {
            var text = (token ?? "").Trim();

            if (string.IsNullOrWhiteSpace(text))
                return "ATS Cargo";

            text = text.Replace("@@", "");
            text = text.Replace("__", "_");

            if (text.Contains("."))
                text = text.Split('.').LastOrDefault() ?? text;

            text = text.Replace("_", " ");
            text = text.Replace("-", " ");

            text = Regex.Replace(
                text,
                @"^(cargo|trailer|company|city)\s+",
                "",
                RegexOptions.IgnoreCase);

            text = Regex.Replace(text, @"\s+", " ").Trim();

            if (string.IsNullOrWhiteSpace(text))
                return "ATS Cargo";

            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(w =>
                {
                    if (w.Length == 1)
                        return w.ToUpperInvariant();

                    return char.ToUpperInvariant(w[0]) + w.Substring(1).ToLowerInvariant();
                });

            return string.Join(" ", words);
        }

        private static string CultureInfoInvariantTitle(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.Length == 1
                    ? w.ToUpperInvariant()
                    : char.ToUpperInvariant(w[0]) + w.Substring(1).ToLowerInvariant());

            return string.Join(" ", words);
        }

        private sealed class AtsScanContext
        {
            public string ModFolder { get; set; } = "";
            public string ProfileName { get; set; } = "";
            public string CacheKey { get; set; } = "";
            public AtsActiveModsSnapshot ActiveProfile { get; set; } = new();
            public List<AtsDefinitionSource> Sources { get; set; } = new();
        }

        private sealed class AtsSelectedModsFile
        {
            public List<string> SelectedSourceIds { get; set; } = new();
        }

        private sealed class AtsTextDefFile
        {
            public string Path { get; set; } = "";
            public string Text { get; set; } = "";
        }
    }

    public enum AtsDetectedModType
    {
        Unknown = 0,
        Trailer = 1,
        Truck = 2,
        Cargo = 3,
        Map = 4,
        Sound = 5,
        Graphics = 6
    }

    public enum AtsDefinitionSourceKind
    {
        BuiltInSeed = 0,
        Directory = 1,
        ZipArchive = 2
    }

    public sealed class AtsDefinitionSource
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string FullPath { get; set; } = "";
        public AtsDefinitionSourceKind Kind { get; set; }
        public AtsDetectedModType DetectedType { get; set; } = AtsDetectedModType.Unknown;
        public int Priority { get; set; }
        public bool IsActiveInProfile { get; set; }
        public string ActiveProfileNote { get; set; } = "";
    }

    public sealed class AtsResolvedDefinitionCache
    {
        public DateTime GeneratedUtc { get; set; }
        public string ModFolder { get; set; } = "";
        public string ProfileName { get; set; } = "";
        public AtsActiveModsSnapshot ActiveProfile { get; set; } = new();
        public List<AtsDefinitionSource> Sources { get; set; } = new();
        public List<AtsResolvedCargoDef> Cargoes { get; set; } = new();
        public List<AtsResolvedTrailerDef> Trailers { get; set; } = new();
        public List<AtsResolvedCompanyDef> Companies { get; set; } = new();
        public List<AtsResolvedCityDef> Cities { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    public sealed class AtsResolvedCargoDef
    {
        public string Token { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string SourceId { get; set; } = "";
        public string SourceLabel { get; set; } = "";
        public int Priority { get; set; }
        public bool IsActiveInProfile { get; set; } = true;
        public string ActiveProfileNote { get; set; } = "";
        public List<string> AllowedTrailerTokens { get; set; } = new();
    }

    public sealed class AtsResolvedTrailerDef
    {
        public string Token { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string SourceId { get; set; } = "";
        public string SourceLabel { get; set; } = "";
        public string ModGroup { get; set; } = "";
        public int Priority { get; set; }
        public bool IsActiveInProfile { get; set; }
        public string ActiveProfileNote { get; set; } = "";
    }

    public sealed class AtsResolvedCompanyDef
    {
        public string Token { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string CityToken { get; set; } = "";
        public string SourceId { get; set; } = "";
        public string SourceLabel { get; set; } = "";
        public int Priority { get; set; }
        public bool IsActiveInProfile { get; set; }
        public string ActiveProfileNote { get; set; } = "";
    }

    public sealed class AtsResolvedCityDef
    {
        public string Token { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string SourceId { get; set; } = "";
        public string SourceLabel { get; set; } = "";
        public int Priority { get; set; }
        public bool IsActiveInProfile { get; set; }
        public string ActiveProfileNote { get; set; } = "";
    }
}