using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public static class AtsExpansionLiveMapService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public static string BuildExpansionLocationsJson()
        {
            try
            {
                var resolved = AtsModScannerService.GetResolvedDefinitions();
                var rows = BuildExpansionLocations(resolved);
                return JsonSerializer.Serialize(rows, JsonOptions);
            }
            catch
            {
                return "[]";
            }
        }

        public static List<AtsExpansionLiveMapLocation> BuildExpansionLocations(AtsResolvedDefinitionCache resolved)
        {
            var rows = new List<AtsExpansionLiveMapLocation>();
            var cityByToken = resolved.Cities
                .Where(x => !string.IsNullOrWhiteSpace(x.Token))
                .GroupBy(x => x.Token, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var city in resolved.Cities)
            {
                if (string.IsNullOrWhiteSpace(city.DisplayName))
                    continue;

                if (!TryGetCityCoordinate(city.DisplayName, city.Token, out var lon, out var lat))
                    continue;

                rows.Add(new AtsExpansionLiveMapLocation
                {
                    Kind = "City",
                    Name = city.DisplayName,
                    City = city.DisplayName,
                    Source = city.SourceLabel,
                    Token = city.Token,
                    Longitude = lon,
                    Latitude = lat
                });
            }

            var companyIndex = 0;
            foreach (var company in resolved.Companies)
            {
                if (string.IsNullOrWhiteSpace(company.DisplayName))
                    continue;

                var cityName = "";
                if (!string.IsNullOrWhiteSpace(company.CityToken) &&
                    cityByToken.TryGetValue(company.CityToken, out var cityDef))
                {
                    cityName = cityDef.DisplayName;
                }
                else if (!string.IsNullOrWhiteSpace(company.CityToken))
                {
                    cityName = HumanizeToken(company.CityToken);
                }

                if (!TryGetCityCoordinate(cityName, company.CityToken, out var lon, out var lat))
                    continue;

                var offset = GetCompanyOffset(companyIndex++);

                rows.Add(new AtsExpansionLiveMapLocation
                {
                    Kind = "Company",
                    Name = company.DisplayName,
                    City = cityName,
                    Source = company.SourceLabel,
                    Token = company.Token,
                    Longitude = lon + offset.lon,
                    Latitude = lat + offset.lat
                });
            }

            foreach (var discovered in AtsExpansionLocationDiscoveryStore.Load())
            {
                if (discovered.DiscoveredUtc == default)
                    discovered.DiscoveredUtc = DateTime.UtcNow;

                rows.Add(discovered);
            }

            return rows
                .Where(x => !string.IsNullOrWhiteSpace(x.Name) || !string.IsNullOrWhiteSpace(x.City))
                .GroupBy(x => $"{x.Kind}|{x.Name}|{x.City}|{Math.Round(x.Longitude, 4)}|{Math.Round(x.Latitude, 4)}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.DiscoveredUtc).First())
                .OrderBy(x => x.Kind)
                .ThenBy(x => x.City, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Take(1000)
                .ToList();
        }

        private static (double lon, double lat) GetCompanyOffset(int index)
        {
            var ring = index % 12;
            var radius = 0.035;
            var angle = (Math.PI * 2.0 / 12.0) * ring;
            return (Math.Cos(angle) * radius, Math.Sin(angle) * radius);
        }

        private static bool TryGetCityCoordinate(string? displayName, string? token, out double lon, out double lat)
        {
            lon = 0;
            lat = 0;

            foreach (var key in BuildCityKeys(displayName, token))
            {
                if (KnownCityCoordinates.TryGetValue(key, out var pt))
                {
                    lon = pt.lon;
                    lat = pt.lat;
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<string> BuildCityKeys(string? displayName, string? token)
        {
            foreach (var value in new[] { displayName, token, HumanizeToken(token ?? "") })
            {
                var key = NormalizeCityKey(value);
                if (!string.IsNullOrWhiteSpace(key))
                    yield return key;
            }
        }

        private static string NormalizeCityKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            var s = value.Trim();
            if (s.Contains('.'))
                s = s.Split('.').Last();

            s = s.Replace("_", " ").Replace("-", " ").Trim().ToLowerInvariant();
            return string.Join(" ", s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        private static string HumanizeToken(string? token)
        {
            var key = NormalizeCityKey(token);
            if (string.IsNullOrWhiteSpace(key))
                return "";

            return string.Join(" ", key.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.Length <= 1 ? w.ToUpperInvariant() : char.ToUpperInvariant(w[0]) + w.Substring(1)));
        }

        private static readonly Dictionary<string, (double lon, double lat)> KnownCityCoordinates = new(StringComparer.OrdinalIgnoreCase)
        {
            ["seattle"] = (-122.3321, 47.6062),
            ["tacoma"] = (-122.4443, 47.2529),
            ["olympia"] = (-122.9007, 47.0379),
            ["spokane"] = (-117.4260, 47.6588),
            ["bellingham"] = (-122.4787, 48.7519),
            ["portland"] = (-122.6765, 45.5231),
            ["eugene"] = (-123.0868, 44.0521),
            ["salem"] = (-123.0351, 44.9429),
            ["medford"] = (-122.8719, 42.3265),
            ["boise"] = (-116.2023, 43.6150),
            ["twin falls"] = (-114.4609, 42.5629),
            ["pocatello"] = (-112.4455, 42.8713),
            ["idaho falls"] = (-112.0339, 43.4917),
            ["missoula"] = (-113.9940, 46.8721),
            ["butte"] = (-112.5347, 46.0038),
            ["billings"] = (-108.5007, 45.7833),
            ["helena"] = (-112.0361, 46.5884),
            ["great falls"] = (-111.3008, 47.5053),
            ["las vegas"] = (-115.1398, 36.1699),
            ["reno"] = (-119.8138, 39.5296),
            ["elko"] = (-115.7631, 40.8324),
            ["sacramento"] = (-121.4944, 38.5816),
            ["san francisco"] = (-122.4194, 37.7749),
            ["san jose"] = (-121.8863, 37.3382),
            ["fresno"] = (-119.7871, 36.7378),
            ["bakersfield"] = (-119.0187, 35.3733),
            ["los angeles"] = (-118.2437, 34.0522),
            ["san diego"] = (-117.1611, 32.7157),
            ["flagstaff"] = (-111.6513, 35.1983),
            ["phoenix"] = (-112.0740, 33.4484),
            ["tucson"] = (-110.9747, 32.2226),
            ["yuma"] = (-114.6277, 32.6927),
            ["salt lake city"] = (-111.8910, 40.7608),
            ["ogden"] = (-111.9738, 41.2230),
            ["provo"] = (-111.6585, 40.2338),
            ["moab"] = (-109.5498, 38.5733),
            ["denver"] = (-104.9903, 39.7392),
            ["colorado springs"] = (-104.8214, 38.8339),
            ["pueblo"] = (-104.6091, 38.2544),
            ["grand junction"] = (-108.5506, 39.0639),
            ["fort collins"] = (-105.0844, 40.5853),
            ["cheyenne"] = (-104.8202, 41.1400),
            ["casper"] = (-106.3131, 42.8501),
            ["rock springs"] = (-109.2207, 41.5875),
            ["albuquerque"] = (-106.6504, 35.0844),
            ["santa fe"] = (-105.9378, 35.6870),
            ["roswell"] = (-104.5230, 33.3943),
            ["farmington"] = (-108.2187, 36.7281),
            ["el paso"] = (-106.4850, 31.7619),
            ["lubbock"] = (-101.8552, 33.5779),
            ["amarillo"] = (-101.8313, 35.2220),
            ["dallas"] = (-96.7970, 32.7767),
            ["fort worth"] = (-97.3308, 32.7555),
            ["austin"] = (-97.7431, 30.2672),
            ["san antonio"] = (-98.4936, 29.4241),
            ["houston"] = (-95.3698, 29.7604),
            ["corpus christi"] = (-97.3964, 27.8006),
            ["brownsville"] = (-97.4975, 25.9017),
            ["oklahoma city"] = (-97.5164, 35.4676),
            ["tulsa"] = (-95.9928, 36.1540),
            ["wichita"] = (-97.3301, 37.6872),
            ["kansas city"] = (-94.5786, 39.0997),
            ["topeka"] = (-95.6890, 39.0473),
            ["omaha"] = (-95.9345, 41.2565),
            ["lincoln"] = (-96.7026, 40.8136),
            ["north platte"] = (-100.7654, 41.1239),
            ["sioux falls"] = (-96.7311, 43.5460),
            ["rapid city"] = (-103.2310, 44.0805),
            ["fargo"] = (-96.7898, 46.8772),
            ["bismarck"] = (-100.7837, 46.8083),
            ["minneapolis"] = (-93.2650, 44.9778),
            ["duluth"] = (-92.1005, 46.7867),
            ["des moines"] = (-93.6091, 41.6005),
            ["cedar rapids"] = (-91.6656, 41.9779),
            ["st louis"] = (-90.1994, 38.6270),
            ["springfield"] = (-93.2923, 37.2089),
            ["little rock"] = (-92.2896, 34.7465),
            ["new orleans"] = (-90.0715, 29.9511),
            ["shreveport"] = (-93.7502, 32.5252),
            ["calgary"] = (-114.0719, 51.0447),
            ["edmonton"] = (-113.4938, 53.5461),
            ["vancouver"] = (-123.1207, 49.2827),
            ["kamloops"] = (-120.3273, 50.6745),
            ["kelowna"] = (-119.4960, 49.8880),
            ["prince george"] = (-122.7497, 53.9171),
            ["mexicali"] = (-115.4523, 32.6245),
            ["tijuana"] = (-117.0382, 32.5149),
            ["ensenada"] = (-116.5964, 31.8667),
            ["hermosillo"] = (-110.9559, 29.0729),
            ["chihuahua"] = (-106.0691, 28.6320),
            ["monterrey"] = (-100.3161, 25.6866),
            ["torreon"] = (-103.4068, 25.5428),
            ["durango"] = (-104.6532, 24.0277),
            ["mazatlan"] = (-106.4245, 23.2494)
        };
    }

    public sealed class AtsExpansionLiveMapLocation
    {
        public string Kind { get; set; } = "";
        public string Name { get; set; } = "";
        public string City { get; set; } = "";
        public string Source { get; set; } = "";
        public string Token { get; set; } = "";
        public double Longitude { get; set; }
        public double Latitude { get; set; }
        public DateTime DiscoveredUtc { get; set; }
    }
}
