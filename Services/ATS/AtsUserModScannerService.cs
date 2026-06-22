using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

namespace OverWatchELD.Services.ATS
{
    /// <summary>
    /// Scans the logged-in Windows user's ATS mod folder for .scs/.zip mods and extracts useful cargo/trailer candidates.
    /// This intentionally avoids hardcoded mod names so public users can use their own mod folder.
    /// </summary>
    public sealed class AtsUserModScannerService
    {
        private readonly AtsUserFolderLocatorService _folders;

        private static readonly Regex SiiBlockRegex = new(
            @"(?<type>[a-zA-Z0-9_\.]+)\s*:\s*(?<token>[a-zA-Z0-9_\.\-]+)\s*\{(?<body>.*?)\}",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex NameRegex = new(
            @"(?:name|display_name|cargo_name)\s*:\s*""(?<name>[^""]+)""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex WeightRegex = new(
            @"(?:mass|weight|gross_weight|cargo_mass)\s*:\s*(?<num>[0-9]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public AtsUserModScannerService(AtsUserFolderLocatorService folders)
        {
            _folders = folders;
        }

        public IReadOnlyList<AtsModScanLoadCandidate> ScanUserMods()
        {
            var modFolder = _folders.GetModFolder();
            if (string.IsNullOrWhiteSpace(modFolder))
                return Array.Empty<AtsModScanLoadCandidate>();

            var modFiles = Directory.EnumerateFiles(modFolder, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => f.EndsWith(".scs", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => Path.GetFileName(f))
                .ToArray();

            var cargo = new List<AtsModScanLoadCandidate>();
            var trailers = new List<AtsModScanLoadCandidate>();

            foreach (var file in modFiles)
            {
                try
                {
                    ScanArchive(file, cargo, trailers);
                }
                catch
                {
                    // Keep scanning other mods. Bad/locked mods should not break the ELD.
                }
            }

            return BuildLoadCandidates(cargo, trailers);
        }

        private void ScanArchive(string archivePath, List<AtsModScanLoadCandidate> cargo, List<AtsModScanLoadCandidate> trailers)
        {
            if (!IsLikelyZipArchive(archivePath))
                return;

            ZipArchive zip;
            try
            {
                zip = ZipFile.OpenRead(archivePath);
            }
            catch
            {
                return;
            }

            using (zip)
            {
                var sourceModName = Path.GetFileNameWithoutExtension(archivePath);

                foreach (var entry in zip.Entries)
                {
                    string name;
                    try { name = entry.FullName.Replace('\\', '/'); }
                    catch { continue; }

                    if (!name.EndsWith(".sii", StringComparison.OrdinalIgnoreCase) &&
                        !name.EndsWith(".sui", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var lower = name.ToLowerInvariant();
                    var looksCargo = lower.Contains("/def/cargo/") || lower.Contains("cargo");
                    var looksTrailer = lower.Contains("/def/vehicle/trailer") || lower.Contains("trailer");

                    if (!looksCargo && !looksTrailer)
                        continue;

                    string text;
                    try
                    {
                        using var stream = entry.Open();
                        using var reader = new StreamReader(stream);
                        text = reader.ReadToEnd();
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (Match m in SiiBlockRegex.Matches(text))
                    {
                        var type = m.Groups["type"].Value;
                        var token = m.Groups["token"].Value;
                        var body = m.Groups["body"].Value;

                        var displayName = NameRegex.Match(body).Groups["name"].Value;
                        if (string.IsNullOrWhiteSpace(displayName))
                            displayName = TokenToTitle(token);

                        var weight = 0;
                        var wm = WeightRegex.Match(body);
                        if (wm.Success && int.TryParse(wm.Groups["num"].Value, out var parsed))
                            weight = parsed < 100000 ? (int)Math.Round(parsed * 2.20462) : parsed;

                        if (looksCargo || type.Contains("cargo", StringComparison.OrdinalIgnoreCase))
                        {
                            cargo.Add(new AtsModScanLoadCandidate
                            {
                                SourceFile = archivePath,
                                SourceModName = sourceModName,
                                CargoToken = token,
                                CargoName = displayName,
                                WeightLbs = weight > 0 ? weight : 42000
                            });
                        }

                        if (looksTrailer || type.Contains("trailer", StringComparison.OrdinalIgnoreCase))
                        {
                            trailers.Add(new AtsModScanLoadCandidate
                            {
                                SourceFile = archivePath,
                                SourceModName = sourceModName,
                                TrailerToken = token,
                                TrailerName = displayName
                            });
                        }
                    }
                }
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

                return fs.ReadByte() == 'P' && fs.ReadByte() == 'K';
            }
            catch
            {
                return false;
            }
        }

        private static IReadOnlyList<AtsModScanLoadCandidate> BuildLoadCandidates(
            List<AtsModScanLoadCandidate> cargo,
            List<AtsModScanLoadCandidate> trailers)
        {
            var results = new List<AtsModScanLoadCandidate>();
            var trailerPool = trailers
                .GroupBy(t => t.TrailerToken)
                .Select(g => g.First())
                .ToArray();

            foreach (var c in cargo.GroupBy(x => x.CargoToken).Select(g => g.First()))
            {
                var bestTrailer = trailerPool.FirstOrDefault(t =>
                    string.Equals(t.SourceModName, c.SourceModName, StringComparison.OrdinalIgnoreCase))
                    ?? trailerPool.FirstOrDefault();

                results.Add(new AtsModScanLoadCandidate
                {
                    SourceFile = c.SourceFile,
                    SourceModName = c.SourceModName,
                    CargoToken = c.CargoToken,
                    CargoName = c.CargoName,
                    TrailerToken = bestTrailer?.TrailerToken ?? "",
                    TrailerName = bestTrailer?.TrailerName ?? "Compatible trailer",
                    WeightLbs = c.WeightLbs > 0 ? c.WeightLbs : 42000,
                    PickupCity = "",
                    DeliveryCity = "",
                    CompanyFrom = "",
                    CompanyTo = ""
                });
            }

            return results
                .OrderBy(x => x.SourceModName)
                .ThenBy(x => x.CargoName)
                .ToArray();
        }

        private static string TokenToTitle(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return "ATS Load";
            return token.Replace("_", " ").Replace(".", " ").Replace("-", " ").Trim();
        }
    }
}
