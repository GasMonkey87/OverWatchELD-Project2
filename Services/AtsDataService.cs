using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

namespace OverWatchELD.Services
{
    public static class AtsDataService
    {
        public static List<AtsLocation> Cities { get; } = new();
        public static List<string> States { get; } = new();
        public static List<string> Companies { get; } = new();
        public static List<string> Cargoes { get; } = new();
        public static List<string> Trailers { get; } = new();

        // Optional alias in case any newer code references Cargos instead of Cargoes.
        public static IReadOnlyList<string> Cargos => Cargoes;

        private static readonly Dictionary<string, List<string>> _cargoTrailerMap =
            new(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, (double X, double Y)> _cityCoords =
            new(StringComparer.OrdinalIgnoreCase);

        // Hard allow-list for cargo -> trailer combos. This is checked before any guessed logic.
        private static readonly Dictionary<string, string[]> _hardCargoTrailerAllow =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Nitrogen"] = new[] { "Gas Tanker", "Cryogenic Tanker", "Chemical Tanker" },
                ["Oxygen"] = new[] { "Gas Tanker", "Cryogenic Tanker" },
                ["Argon"] = new[] { "Gas Tanker", "Cryogenic Tanker" },
                ["Lng"] = new[] { "Gas Tanker", "Cryogenic Tanker" },
                ["Lng Tank"] = new[] { "Gas Tanker", "Cryogenic Tanker" },
                ["Propane"] = new[] { "Gas Tanker", "Tanker" },
                ["Industrial Gas"] = new[] { "Gas Tanker", "Cryogenic Tanker" },
                ["Cryogenic Liquid"] = new[] { "Cryogenic Tanker" },

                ["Bulldozer"] = new[] { "Lowboy", "Lowbed" },
                ["Excavator"] = new[] { "Lowboy", "Lowbed" },
                ["Transformer"] = new[] { "Lowboy", "Lowbed" },
                ["Roller"] = new[] { "Lowboy", "Lowbed" },
                ["Crane"] = new[] { "Lowboy", "Lowbed" },
                ["Large Machinery"] = new[] { "Lowboy", "Lowbed" },
                ["Oversize Equipment"] = new[] { "Lowboy", "Lowbed" },

                ["Lumber"] = new[] { "Flatbed", "Curtainsider" },
                ["Steel Coils"] = new[] { "Flatbed", "Lowboy" },
                ["Steel Beams"] = new[] { "Flatbed" },
                ["Pipes"] = new[] { "Flatbed" },
                ["Bricks"] = new[] { "Flatbed" },

                ["Fuel"] = new[] { "Tanker" },
                ["Diesel"] = new[] { "Tanker" },
                ["Gasoline"] = new[] { "Tanker" },
                ["Milk"] = new[] { "Tanker" },
                ["Chemicals"] = new[] { "Tanker", "Chemical Tanker" },

                ["Cars"] = new[] { "Car Hauler" },
                ["Vehicles"] = new[] { "Car Hauler" },

                ["Refrigerated Goods"] = new[] { "Reefer" },
                ["Frozen Food"] = new[] { "Reefer" },

                ["Furniture"] = new[] { "Dry Van", "Curtainsider" },
                ["General Goods"] = new[] { "Dry Van", "Curtainsider" },
                ["Consumer Goods"] = new[] { "Dry Van", "Curtainsider" },

                ["Grain"] = new[] { "Hopper" },
                ["Ore"] = new[] { "Hopper" }
            };

        private static bool _loaded;

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            Reload();
        }

        public static void Reload()
        {
            Cities.Clear();
            States.Clear();
            Companies.Clear();
            Cargoes.Clear();
            Trailers.Clear();
            _cargoTrailerMap.Clear();
            _cityCoords.Clear();

            LoadBuiltInFallbacks();
            LoadFromAtsInstall();
            // Disabled: mod folder scanning was causing long startup/create-load delays.
            // Create Load now uses SCS/base ATS content only.
            // LoadFromModFolder();

            SortDistinct(States);
            SortDistinct(Companies);
            SortDistinct(Cargoes);
            SortDistinct(Trailers);

            Cities.Sort((a, b) =>
            {
                var s = string.Compare(a.State, b.State, StringComparison.OrdinalIgnoreCase);
                if (s != 0) return s;
                return string.Compare(a.City, b.City, StringComparison.OrdinalIgnoreCase);
            });

            _loaded = true;
        }

        public static List<string> GetCitiesByState(string? state)
        {
            EnsureLoaded();

            state = NormalizeName(state);

            if (string.IsNullOrWhiteSpace(state))
            {
                return Cities
                    .Select(c => c.City)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return Cities
                .Where(c => string.Equals(c.State, state, StringComparison.OrdinalIgnoreCase))
                .Select(c => c.City)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<string> GetCompaniesByCityState(string? city, string? state)
        {
            EnsureLoaded();

            city = NormalizeName(city);
            state = NormalizeName(state);

            return Cities
                .Where(c =>
                    (string.IsNullOrWhiteSpace(city) || string.Equals(c.City, city, StringComparison.OrdinalIgnoreCase)) &&
                    (string.IsNullOrWhiteSpace(state) || string.Equals(c.State, state, StringComparison.OrdinalIgnoreCase)))
                .SelectMany(c => c.Companies)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<string> GetAllowedTrailers(string? cargo)
        {
            EnsureLoaded();

            cargo = NormalizeName(cargo);
            if (string.IsNullOrWhiteSpace(cargo))
                return new List<string>();

            if (_hardCargoTrailerAllow.TryGetValue(cargo, out var hardAllowed) && hardAllowed.Length > 0)
            {
                return hardAllowed
                    .Select(NormalizeName)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (_cargoTrailerMap.TryGetValue(cargo, out var mapped) && mapped.Count > 0)
            {
                return mapped
                    .Select(NormalizeName)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return GuessTrailersForCargo(cargo)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static bool IsCargoTrailerCompatible(string? cargo, string? trailer)
        {
            EnsureLoaded();

            var c = NormalizeName(cargo);
            var t = NormalizeName(trailer);

            if (string.IsNullOrWhiteSpace(c) || string.IsNullOrWhiteSpace(t))
                return false;

            // Explicit hard blocks first.
            if (IsExplicitlyBlocked(c, t))
                return false;

            var allowed = GetAllowedTrailers(c);
            if (allowed.Count == 0)
                return false;

            return allowed.Any(x => TrailerMatches(x, t));
        }

        public static int CalculateMiles(string? originCity, string? originState, string? destCity, string? destState)
        {
            EnsureLoaded();

            var ok1 = TryGetCoord(originCity, originState, out var p1);
            var ok2 = TryGetCoord(destCity, destState, out var p2);

            if (!ok1 || !ok2) return 0;

            var dx = p2.X - p1.X;
            var dy = p2.Y - p1.Y;
            var euclidean = Math.Sqrt(dx * dx + dy * dy);

            var miles = (int)Math.Round(euclidean * 7.5);
            if (miles < 1) miles = 1;
            return miles;
        }

        private static bool TryGetCoord(string? city, string? state, out (double X, double Y) p)
        {
            city = NormalizeName(city);
            state = NormalizeName(state);

            var key = $"{city}|{state}";
            if (_cityCoords.TryGetValue(key, out p))
                return true;

            p = (0, 0);
            return false;
        }

        private static void LoadBuiltInFallbacks()
        {
            AddLocation("Phoenix", "AZ", 120, 220, "Wallbert", "SellGoods", "Bushnell Farms");
            AddLocation("Tucson", "AZ", 135, 250, "Charged", "Rail Export");
            AddLocation("Denver", "CO", 300, 150, "Wallbert", "Charged", "Rail Export");
            AddLocation("Dallas", "TX", 430, 280, "Wallbert", "Charged", "Voltison");
            AddLocation("Houston", "TX", 470, 335, "Port", "Wallbert", "Bushnell Farms");
            AddLocation("Jacksonville", "FL", 760, 360, "Wallbert", "Port", "Charged");
            AddLocation("Miami", "FL", 805, 470, "Port", "SellGoods");
            AddLocation("Seattle", "WA", 40, 10, "Port", "Wallbert");
            AddLocation("Los Angeles", "CA", 20, 210, "Wallbert", "Port");

            // Illinois DLC seed locations.
            // Real ATS def files are still scanned when the DLC is installed, but these
            // keep Load Board pickup / delivery dropdowns and fallback routing populated.
            //
            // Official Illinois DLC city set from SCS page:
            // Marion, Rockford, Quincy, Moline, Peoria, Champaign, Bloomington,
            // Effingham, East St. Louis, Springfield, Chicago.
            AddLocation("Chicago", "IL", 640, 190,
                "Wallbert", "SellGoods", "Charged", "Rail Export", "Chicago Airport", "Intermodal Hub");

            AddLocation("Springfield", "IL", 620, 235,
                "Wallbert", "Bushnell Farms", "SellGoods", "NAMIQ");

            AddLocation("Rockford", "IL", 590, 170,
                "Wallbert", "Charged", "SellGoods", "Construction Depot");

            AddLocation("Peoria", "IL", 605, 210,
                "Wallbert", "Voltison", "SellGoods", "Heavy Machinery");

            AddLocation("Champaign", "IL", 645, 225,
                "Wallbert", "NAMIQ", "SellGoods", "Farm Center");

            AddLocation("Bloomington", "IL", 620, 215,
                "Wallbert", "Deepgrove", "Bushnell Farms", "Farm Center");

            AddLocation("Moline", "IL", 565, 205,
                "Wallbert", "SellGoods", "Charged", "Agriculture Equipment");

            AddLocation("Quincy", "IL", 570, 250,
                "Bushnell Farms", "Wallbert", "SellGoods", "River Port");

            AddLocation("Effingham", "IL", 645, 260,
                "Wallbert", "SellGoods", "Charged", "Truck Stop");

            AddLocation("Marion", "IL", 650, 305,
                "Wallbert", "Bushnell Farms", "SellGoods", "Construction Depot");

            AddLocation("East St. Louis", "IL", 610, 285,
                "Port", "Wallbert", "SellGoods", "Rail Export");

            // Extra Illinois fallback locations requested for manual dispatch coverage.
            AddLocation("Joliet", "IL", 635, 180,
                "Wallbert", "SellGoods", "Charged");

            AddLocation("Decatur", "IL", 630, 245,
                "Bushnell Farms", "SellGoods", "Wallbert");


            AddCargoWithTrailers("Lumber", "Flatbed", "Curtainsider");
            AddCargoWithTrailers("Steel Coils", "Flatbed", "Lowboy");
            AddCargoWithTrailers("Bulldozer", "Lowboy", "Lowbed");
            AddCargoWithTrailers("Refrigerated Goods", "Reefer");
            AddCargoWithTrailers("Fuel", "Tanker");
            AddCargoWithTrailers("Cars", "Car Hauler");
            AddCargoWithTrailers("Furniture", "Dry Van", "Curtainsider");
            AddCargoWithTrailers("Grain", "Hopper");
            AddCargoWithTrailers("Nitrogen", "Gas Tanker", "Cryogenic Tanker", "Chemical Tanker");
        }

        private static void LoadFromAtsInstall()
        {
            foreach (var root in GetLikelyAtsRoots())
            {
                if (!Directory.Exists(root))
                    continue;

                foreach (var file in Directory.GetFiles(root, "*.scs", SearchOption.TopDirectoryOnly)
                             .Concat(Directory.GetFiles(root, "*.zip", SearchOption.TopDirectoryOnly)))
                {
                    TryScanArchive(file);
                }

                var baseDir = Path.Combine(root, "base");
                if (Directory.Exists(baseDir))
                {
                    foreach (var file in Directory.GetFiles(baseDir, "*.scs", SearchOption.AllDirectories)
                                 .Concat(Directory.GetFiles(baseDir, "*.zip", SearchOption.AllDirectories)))
                    {
                        TryScanArchive(file);
                    }
                }
            }
        }

        private static void LoadFromModFolder()
        {
            var modPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "American Truck Simulator",
                "mod");

            if (!Directory.Exists(modPath))
                return;

            foreach (var file in Directory.GetFiles(modPath)
                         .Where(f => f.EndsWith(".scs", StringComparison.OrdinalIgnoreCase) ||
                                     f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
            {
                TryScanArchive(file);
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

        private static void TryScanArchive(string archivePath)
        {
            try
            {
                if (!IsLikelyZipArchive(archivePath))
                    return;

                using var zip = ZipFile.OpenRead(archivePath);

                foreach (var entry in zip.Entries)
                {
                    var full = entry.FullName.Replace('\\', '/');

                    if (!full.EndsWith(".sii", StringComparison.OrdinalIgnoreCase) &&
                        !full.EndsWith(".sui", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string text;
                    try
                    {
                        using var reader = new StreamReader(entry.Open());
                        text = reader.ReadToEnd();
                    }
                    catch
                    {
                        continue;
                    }

                    ParseStateFile(full, text);
                    ParseCityFile(full, text);
                    ParseCompanyFile(full, text);
                    ParseCargoFile(full, text);
                    ParseTrailerFile(full, text);
                }
            }
            catch
            {
                // skip unreadable archives
            }
        }

        private static void ParseStateFile(string fullPath, string text)
        {
            if (!fullPath.Contains("/state/", StringComparison.OrdinalIgnoreCase) &&
                !fullPath.Contains("/states/", StringComparison.OrdinalIgnoreCase))
                return;

            var matches = Regex.Matches(text, @"\bname:\s*""([^""]+)""", RegexOptions.IgnoreCase);
            foreach (Match m in matches)
            {
                var state = NormalizeStateName(m.Groups[1].Value);
                if (!string.IsNullOrWhiteSpace(state))
                    AddDistinct(States, state);
            }
        }

        private static void ParseCityFile(string fullPath, string text)
        {
            if (!fullPath.Contains("/city/", StringComparison.OrdinalIgnoreCase))
                return;

            var city = "";
            var state = "";

            var cityNameQuoted = Regex.Match(text, @"\bcity_name:\s*""([^""]+)""", RegexOptions.IgnoreCase);
            var cityNamePlain = Regex.Match(text, @"\bcity_name:\s*([a-z0-9_\.]+)", RegexOptions.IgnoreCase);
            var stateQuoted = Regex.Match(text, @"\bstate:\s*""([^""]+)""", RegexOptions.IgnoreCase);
            var statePlain = Regex.Match(text, @"\bstate:\s*([a-z0-9_\.]+)", RegexOptions.IgnoreCase);

            if (cityNameQuoted.Success) city = NormalizeName(cityNameQuoted.Groups[1].Value);
            else if (cityNamePlain.Success) city = FriendlyFromToken(cityNamePlain.Groups[1].Value);

            if (stateQuoted.Success) state = NormalizeStateName(stateQuoted.Groups[1].Value);
            else if (statePlain.Success) state = NormalizeStateName(statePlain.Groups[1].Value);

            if (string.IsNullOrWhiteSpace(city))
            {
                var nameFromPath = Path.GetFileNameWithoutExtension(fullPath);
                city = FriendlyFromToken(nameFromPath);
            }

            if (string.IsNullOrWhiteSpace(state))
                state = GuessStateFromPathOrText(fullPath, text);

            if (string.IsNullOrWhiteSpace(city) || string.IsNullOrWhiteSpace(state))
                return;

            var key = $"{city}|{state}";
            if (!_cityCoords.ContainsKey(key))
            {
                var idx = Cities.Count + 1;
                _cityCoords[key] = (50 + idx * 6, 50 + idx * 4);
            }

            AddLocation(city, state, _cityCoords[key].X, _cityCoords[key].Y);
        }

        private static void ParseCompanyFile(string fullPath, string text)
        {
            if (!fullPath.Contains("/company/", StringComparison.OrdinalIgnoreCase))
                return;

            var company = "";
            var city = "";
            var state = "";

            var companyQuoted = Regex.Match(text, @"\bname:\s*""([^""]+)""", RegexOptions.IgnoreCase);
            if (companyQuoted.Success)
                company = NormalizeName(companyQuoted.Groups[1].Value);

            if (string.IsNullOrWhiteSpace(company))
                company = FriendlyFromToken(Path.GetFileNameWithoutExtension(fullPath));

            var cityQuoted = Regex.Match(text, @"\bcity_name:\s*""([^""]+)""", RegexOptions.IgnoreCase);
            var cityPlain = Regex.Match(text, @"\bcity_name:\s*([a-z0-9_\.]+)", RegexOptions.IgnoreCase);
            var stateQuoted = Regex.Match(text, @"\bstate:\s*""([^""]+)""", RegexOptions.IgnoreCase);
            var statePlain = Regex.Match(text, @"\bstate:\s*([a-z0-9_\.]+)", RegexOptions.IgnoreCase);

            if (cityQuoted.Success) city = NormalizeName(cityQuoted.Groups[1].Value);
            else if (cityPlain.Success) city = FriendlyFromToken(cityPlain.Groups[1].Value);

            if (stateQuoted.Success) state = NormalizeStateName(stateQuoted.Groups[1].Value);
            else if (statePlain.Success) state = NormalizeStateName(statePlain.Groups[1].Value);

            if (string.IsNullOrWhiteSpace(state))
                state = GuessStateFromPathOrText(fullPath, text);

            if (!string.IsNullOrWhiteSpace(city) && !string.IsNullOrWhiteSpace(state))
            {
                var key = $"{city}|{state}";
                if (!_cityCoords.ContainsKey(key))
                {
                    var idx = Cities.Count + 1;
                    _cityCoords[key] = (50 + idx * 6, 50 + idx * 4);
                }

                AddLocation(city, state, _cityCoords[key].X, _cityCoords[key].Y, company);
            }
            else
            {
                AddDistinct(Companies, company);
            }
        }

        private static void ParseCargoFile(string fullPath, string text)
        {
            if (!fullPath.Contains("/cargo/", StringComparison.OrdinalIgnoreCase))
                return;

            var cargo = "";

            var cargoQuoted = Regex.Match(text, @"\bname:\s*""([^""]+)""", RegexOptions.IgnoreCase);
            var cargoData = Regex.Match(text, @"cargo_data:\s*([a-z0-9_\.]+)", RegexOptions.IgnoreCase);

            if (cargoQuoted.Success) cargo = NormalizeName(cargoQuoted.Groups[1].Value);
            else if (cargoData.Success) cargo = FriendlyFromToken(cargoData.Groups[1].Value);
            else cargo = FriendlyFromToken(Path.GetFileNameWithoutExtension(fullPath));

            if (string.IsNullOrWhiteSpace(cargo))
                return;

            AddDistinct(Cargoes, cargo);

            // Hard rules win.
            if (_hardCargoTrailerAllow.TryGetValue(cargo, out var hardAllowed) && hardAllowed.Length > 0)
            {
                AddCargoWithTrailers(cargo, hardAllowed);
                return;
            }

            // If the file itself names known trailers, use them.
            var parsedTrailerNames = ExtractTrailerNamesFromCargoText(text);
            if (parsedTrailerNames.Count > 0)
            {
                AddCargoWithTrailers(cargo, parsedTrailerNames.ToArray());
                return;
            }

            // Fall back to tighter guessing. Unknown cargo returns no trailers.
            var guessed = GuessTrailersForCargo(cargo);
            if (guessed.Count > 0)
            {
                AddCargoWithTrailers(cargo, guessed.ToArray());
            }
        }

        private static void ParseTrailerFile(string fullPath, string text)
        {
            if (!fullPath.Contains("/trailer/", StringComparison.OrdinalIgnoreCase))
                return;

            var trailer = "";

            var trailerQuoted = Regex.Match(text, @"\bname:\s*""([^""]+)""", RegexOptions.IgnoreCase);
            var trailerDef = Regex.Match(text, @"trailer_def:\s*([a-z0-9_\.]+)", RegexOptions.IgnoreCase);

            if (trailerQuoted.Success) trailer = NormalizeName(trailerQuoted.Groups[1].Value);
            else if (trailerDef.Success) trailer = FriendlyTrailerName(trailerDef.Groups[1].Value);
            else trailer = FriendlyTrailerName(Path.GetFileNameWithoutExtension(fullPath));

            AddDistinct(Trailers, trailer);
        }

        private static IEnumerable<string> GetLikelyAtsRoots()
        {
            var results = new List<string>();

            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            AddIfExists(results, Path.Combine(pf86, "Steam", "steamapps", "common", "American Truck Simulator"));
            AddIfExists(results, Path.Combine(pf, "Steam", "steamapps", "common", "American Truck Simulator"));
            AddIfExists(results, @"C:\SteamLibrary\steamapps\common\American Truck Simulator");
            AddIfExists(results, @"D:\SteamLibrary\steamapps\common\American Truck Simulator");
            AddIfExists(results, @"E:\SteamLibrary\steamapps\common\American Truck Simulator");
            AddIfExists(results, @"F:\SteamLibrary\steamapps\common\American Truck Simulator");

            return results.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static void AddIfExists(List<string> list, string path)
        {
            if (Directory.Exists(path))
                list.Add(path);
        }

        private static void AddLocation(string city, string state, double x, double y, params string[] companies)
        {
            city = NormalizeName(city);
            state = NormalizeStateName(state);

            if (string.IsNullOrWhiteSpace(city) || string.IsNullOrWhiteSpace(state))
                return;

            var existing = Cities.FirstOrDefault(c =>
                string.Equals(c.City, city, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.State, state, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                existing = new AtsLocation
                {
                    City = city,
                    State = state
                };
                Cities.Add(existing);
            }

            foreach (var company in companies ?? Array.Empty<string>())
            {
                var clean = NormalizeName(company);
                if (string.IsNullOrWhiteSpace(clean)) continue;

                if (!existing.Companies.Contains(clean, StringComparer.OrdinalIgnoreCase))
                    existing.Companies.Add(clean);

                AddDistinct(Companies, clean);
            }

            AddDistinct(States, state);
            _cityCoords[$"{city}|{state}"] = (x, y);
        }

        private static void AddCargoWithTrailers(string cargo, params string[] trailers)
        {
            cargo = NormalizeName(cargo);
            if (string.IsNullOrWhiteSpace(cargo)) return;

            AddDistinct(Cargoes, cargo);

            if (!_cargoTrailerMap.TryGetValue(cargo, out var list))
            {
                list = new List<string>();
                _cargoTrailerMap[cargo] = list;
            }

            foreach (var trailer in trailers ?? Array.Empty<string>())
            {
                var clean = NormalizeTrailerName(trailer);
                if (string.IsNullOrWhiteSpace(clean)) continue;

                if (!list.Contains(clean, StringComparer.OrdinalIgnoreCase))
                    list.Add(clean);

                AddDistinct(Trailers, clean);
            }
        }

        private static List<string> ExtractTrailerNamesFromCargoText(string text)
        {
            var results = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
                return results;

            var patterns = new[]
            {
                @"\btrailer(?:s)?\s*:\s*([^\r\n]+)",
                @"\bbody_type\s*:\s*([^\r\n]+)",
                @"\btrailer_def\s*:\s*([a-z0-9_\.\s,""-]+)",
                @"\btrailer_chain\s*:\s*([a-z0-9_\.\s,""-]+)"
            };

            foreach (var pattern in patterns)
            {
                foreach (Match match in Regex.Matches(text, pattern, RegexOptions.IgnoreCase))
                {
                    var raw = match.Groups[1].Value;
                    foreach (var piece in SplitTrailerTokens(raw))
                    {
                        var normalized = NormalizeTrailerName(piece);
                        if (!string.IsNullOrWhiteSpace(normalized))
                            AddDistinct(results, normalized);
                    }
                }
            }

            return results
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IEnumerable<string> SplitTrailerTokens(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                yield break;

            foreach (var piece in Regex.Split(raw, @"[,;|]"))
            {
                var trimmed = piece.Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(trimmed))
                    yield return trimmed;
            }
        }

        private static List<string> GuessTrailersForCargo(string cargo)
        {
            cargo = NormalizeName(cargo);
            var results = new List<string>();

            if (string.IsNullOrWhiteSpace(cargo))
                return results;

            if (cargo.Contains("Nitrogen", StringComparison.OrdinalIgnoreCase) ||
                cargo.Contains("Oxygen", StringComparison.OrdinalIgnoreCase) ||
                cargo.Contains("Argon", StringComparison.OrdinalIgnoreCase) ||
                cargo.Contains("Lng", StringComparison.OrdinalIgnoreCase) ||
                cargo.Contains("Propane", StringComparison.OrdinalIgnoreCase) ||
                cargo.Contains("Cryogenic", StringComparison.OrdinalIgnoreCase) ||
                cargo.Contains("Industrial Gas", StringComparison.OrdinalIgnoreCase))
            {
                results.Add("Gas Tanker");
                results.Add("Cryogenic Tanker");
            }

            if (cargo.Contains("Frozen", StringComparison.OrdinalIgnoreCase) ||
                cargo.Contains("Refrigerated", StringComparison.OrdinalIgnoreCase) ||
                cargo.Contains("Food", StringComparison.OrdinalIgnoreCase))
                results.Add("Reefer");

            if (cargo.Contains("Fuel", StringComparison.OrdinalIgnoreCase) ||
                cargo.Contains("Milk", StringComparison.OrdinalIgnoreCase) ||
                cargo.Contains("Chemical", StringComparison.OrdinalIgnoreCase) ||
                cargo.Contains("Diesel", StringComparison.OrdinalIgnoreCase) ||
                cargo.Contains("Gasoline", StringComparison.OrdinalIgnoreCase))
                results.Add("Tanker");

            if (cargo.Contains("Car", StringComparison.OrdinalIgnoreCase) ||
                cargo.Contains("Vehicle", StringComparison.OrdinalIgnoreCase))
                results.Add("Car Hauler");

            if (cargo.Contains("Grain", StringComparison.OrdinalIgnoreCase) ||
                cargo.Contains("Ore", StringComparison.OrdinalIgnoreCase))
                results.Add("Hopper");

            if (cargo.Contains("Bulldozer", StringComparison.OrdinalIgnoreCase) ||
                cargo.Contains("Excavator", StringComparison.OrdinalIgnoreCase) ||
                cargo.Contains("Machinery", StringComparison.OrdinalIgnoreCase) ||
                cargo.Contains("Transformer", StringComparison.OrdinalIgnoreCase) ||
                cargo.Contains("Roller", StringComparison.OrdinalIgnoreCase) ||
                cargo.Contains("Crane", StringComparison.OrdinalIgnoreCase))
            {
                results.Add("Lowboy");
                results.Add("Lowbed");
            }

            if (cargo.Contains("Steel", StringComparison.OrdinalIgnoreCase) ||
                cargo.Contains("Lumber", StringComparison.OrdinalIgnoreCase) ||
                cargo.Contains("Pipe", StringComparison.OrdinalIgnoreCase) ||
                cargo.Contains("Beam", StringComparison.OrdinalIgnoreCase) ||
                cargo.Contains("Brick", StringComparison.OrdinalIgnoreCase))
                results.Add("Flatbed");

            if (cargo.Contains("Furniture", StringComparison.OrdinalIgnoreCase) ||
                cargo.Contains("General Goods", StringComparison.OrdinalIgnoreCase) ||
                cargo.Contains("Consumer Goods", StringComparison.OrdinalIgnoreCase))
            {
                results.Add("Dry Van");
                results.Add("Curtainsider");
            }

            // Fail closed: if nothing matched, return empty.
            return results
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsExplicitlyBlocked(string cargo, string trailer)
        {
            // Main issue you called out.
            if (cargo.Equals("Nitrogen", StringComparison.OrdinalIgnoreCase) &&
                (trailer.Equals("Lowboy", StringComparison.OrdinalIgnoreCase) ||
                 trailer.Equals("Lowbed", StringComparison.OrdinalIgnoreCase) ||
                 trailer.Contains("Lowboy", StringComparison.OrdinalIgnoreCase) ||
                 trailer.Contains("Lowbed", StringComparison.OrdinalIgnoreCase)))
                return true;

            // Gases should never ride lowboy/flatbed/dry van/reefer/car hauler.
            if ((cargo.Contains("Nitrogen", StringComparison.OrdinalIgnoreCase) ||
                 cargo.Contains("Oxygen", StringComparison.OrdinalIgnoreCase) ||
                 cargo.Contains("Argon", StringComparison.OrdinalIgnoreCase) ||
                 cargo.Contains("Lng", StringComparison.OrdinalIgnoreCase) ||
                 cargo.Contains("Propane", StringComparison.OrdinalIgnoreCase) ||
                 cargo.Contains("Cryogenic", StringComparison.OrdinalIgnoreCase) ||
                 cargo.Contains("Industrial Gas", StringComparison.OrdinalIgnoreCase)) &&
                (trailer.Contains("Lowboy", StringComparison.OrdinalIgnoreCase) ||
                 trailer.Contains("Lowbed", StringComparison.OrdinalIgnoreCase) ||
                 trailer.Contains("Flatbed", StringComparison.OrdinalIgnoreCase) ||
                 trailer.Contains("Dry Van", StringComparison.OrdinalIgnoreCase) ||
                 trailer.Contains("Curtain", StringComparison.OrdinalIgnoreCase) ||
                 trailer.Contains("Reefer", StringComparison.OrdinalIgnoreCase) ||
                 trailer.Contains("Car Hauler", StringComparison.OrdinalIgnoreCase)))
                return true;

            // Heavy machinery should not go on normal enclosed/tank trailers.
            if ((cargo.Contains("Bulldozer", StringComparison.OrdinalIgnoreCase) ||
                 cargo.Contains("Excavator", StringComparison.OrdinalIgnoreCase) ||
                 cargo.Contains("Transformer", StringComparison.OrdinalIgnoreCase) ||
                 cargo.Contains("Roller", StringComparison.OrdinalIgnoreCase) ||
                 cargo.Contains("Crane", StringComparison.OrdinalIgnoreCase) ||
                 cargo.Contains("Machinery", StringComparison.OrdinalIgnoreCase)) &&
                (trailer.Contains("Dry Van", StringComparison.OrdinalIgnoreCase) ||
                 trailer.Contains("Curtain", StringComparison.OrdinalIgnoreCase) ||
                 trailer.Contains("Reefer", StringComparison.OrdinalIgnoreCase) ||
                 trailer.Contains("Tanker", StringComparison.OrdinalIgnoreCase) ||
                 trailer.Contains("Gas Tanker", StringComparison.OrdinalIgnoreCase) ||
                 trailer.Contains("Cryogenic", StringComparison.OrdinalIgnoreCase)))
                return true;

            return false;
        }

        private static bool TrailerMatches(string allowed, string actual)
        {
            allowed = NormalizeTrailerName(allowed);
            actual = NormalizeTrailerName(actual);

            if (string.Equals(allowed, actual, StringComparison.OrdinalIgnoreCase))
                return true;

            if (actual.Contains(allowed, StringComparison.OrdinalIgnoreCase))
                return true;

            if (allowed.Contains(actual, StringComparison.OrdinalIgnoreCase))
                return true;

            // Family-style matches for SCS/custom trailer names.
            if (allowed.Equals("Lowboy", StringComparison.OrdinalIgnoreCase) &&
                (actual.Contains("Lowboy", StringComparison.OrdinalIgnoreCase) ||
                 actual.Contains("Low Bed", StringComparison.OrdinalIgnoreCase) ||
                 actual.Contains("Lowbed", StringComparison.OrdinalIgnoreCase)))
                return true;

            if (allowed.Equals("Lowbed", StringComparison.OrdinalIgnoreCase) &&
                (actual.Contains("Lowboy", StringComparison.OrdinalIgnoreCase) ||
                 actual.Contains("Low Bed", StringComparison.OrdinalIgnoreCase) ||
                 actual.Contains("Lowbed", StringComparison.OrdinalIgnoreCase)))
                return true;

            if (allowed.Equals("Gas Tanker", StringComparison.OrdinalIgnoreCase) &&
                (actual.Contains("Gas", StringComparison.OrdinalIgnoreCase) ||
                 actual.Contains("Cryogenic", StringComparison.OrdinalIgnoreCase)))
                return true;

            if (allowed.Equals("Cryogenic Tanker", StringComparison.OrdinalIgnoreCase) &&
                (actual.Contains("Cryogenic", StringComparison.OrdinalIgnoreCase) ||
                 actual.Contains("Gas", StringComparison.OrdinalIgnoreCase)))
                return true;

            return false;
        }

        private static string FriendlyFromToken(string token)
        {
            token = token?.Split('.').LastOrDefault() ?? "";
            token = token.Replace('_', ' ').Replace('-', ' ').Trim();
            return NormalizeName(token);
        }

        private static string FriendlyTrailerName(string token)
        {
            var t = FriendlyFromToken(token);
            return NormalizeTrailerName(t);
        }

        private static string NormalizeTrailerName(string? value)
        {
            var t = NormalizeName(value);

            if (t.Contains("Cryogenic", StringComparison.OrdinalIgnoreCase)) return "Cryogenic Tanker";
            if (t.Contains("Gas Tank", StringComparison.OrdinalIgnoreCase)) return "Gas Tanker";
            if (t.Contains("Gas Trailer", StringComparison.OrdinalIgnoreCase)) return "Gas Tanker";
            if (t.Contains("Chemical Tank", StringComparison.OrdinalIgnoreCase)) return "Chemical Tanker";
            if (t.Contains("Chemical", StringComparison.OrdinalIgnoreCase) && t.Contains("Tank", StringComparison.OrdinalIgnoreCase)) return "Chemical Tanker";
            if (t.Contains("Reefer", StringComparison.OrdinalIgnoreCase)) return "Reefer";
            if (t.Contains("Flat", StringComparison.OrdinalIgnoreCase)) return "Flatbed";
            if (t.Contains("Lowboy", StringComparison.OrdinalIgnoreCase)) return "Lowboy";
            if (t.Contains("Lowbed", StringComparison.OrdinalIgnoreCase)) return "Lowbed";
            if (t.Contains("Low Bed", StringComparison.OrdinalIgnoreCase)) return "Lowbed";
            if (t.Contains("Tanker", StringComparison.OrdinalIgnoreCase)) return "Tanker";
            if (t.Contains("Hopper", StringComparison.OrdinalIgnoreCase)) return "Hopper";
            if (t.Contains("Curtain", StringComparison.OrdinalIgnoreCase)) return "Curtainsider";
            if (t.Contains("Dry", StringComparison.OrdinalIgnoreCase)) return "Dry Van";
            if (t.Contains("Van", StringComparison.OrdinalIgnoreCase) && !t.Contains("Reefer", StringComparison.OrdinalIgnoreCase)) return "Dry Van";
            if (t.Contains("Car", StringComparison.OrdinalIgnoreCase)) return "Car Hauler";

            return t;
        }

        private static string GuessStateFromPathOrText(string fullPath, string text)
        {
            var m1 = Regex.Match(text, @"\bstate:\s*([a-z0-9_\.]+)", RegexOptions.IgnoreCase);
            if (m1.Success)
                return NormalizeStateName(m1.Groups[1].Value);

            var m2 = Regex.Match(fullPath, @"/([a-z]{2})/", RegexOptions.IgnoreCase);
            if (m2.Success)
                return NormalizeStateName(m2.Groups[1].Value);

            return "MOD";
        }

        private static string NormalizeStateName(string? value)
        {
            value = (value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value)) return "";

            var shortMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AZ"] = "AZ",
                ["CA"] = "CA",
                ["CO"] = "CO",
                ["FL"] = "FL",
                ["ID"] = "ID",
                ["IL"] = "IL",
                ["Illinois"] = "IL",
                ["KS"] = "KS",
                ["MT"] = "MT",
                ["NE"] = "NE",
                ["NM"] = "NM",
                ["NV"] = "NV",
                ["OK"] = "OK",
                ["OR"] = "OR",
                ["TX"] = "TX",
                ["UT"] = "UT",
                ["WA"] = "WA",
                ["WY"] = "WY"
            };

            var upper = value.ToUpperInvariant();
            if (shortMap.TryGetValue(upper, out var abbr))
                return abbr;

            return NormalizeName(value);
        }

        private static string NormalizeName(string? value)
        {
            value = (value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value)) return "";

            value = Regex.Replace(value, @"\s+", " ");
            return string.Join(" ",
                value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                     .Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1).ToLowerInvariant()));
        }

        private static void AddDistinct(List<string> list, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (!list.Contains(value, StringComparer.OrdinalIgnoreCase))
                list.Add(value);
        }

        private static void SortDistinct(List<string> list)
        {
            var copy = list.Distinct(StringComparer.OrdinalIgnoreCase)
                           .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                           .ToList();
            list.Clear();
            list.AddRange(copy);
        }
    }

    public sealed class AtsLocation
    {
        public string City { get; set; } = "";
        public string State { get; set; } = "";
        public List<string> Companies { get; set; } = new();
    }
}