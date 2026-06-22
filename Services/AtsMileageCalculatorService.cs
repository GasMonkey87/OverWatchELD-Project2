using System;
using System.Collections.Generic;

namespace OverWatchELD.Services.ATS
{
    /// <summary>
    /// Lightweight ATS city mileage calculator for Create Load.
    /// This intentionally does not depend on external map APIs.
    /// It estimates ATS route miles from known city coordinates and adds a road-route factor.
    /// If a city is unknown, it falls back to a safe default so Create Load never breaks.
    /// </summary>
    public sealed class AtsMileageCalculatorService
    {
        private const double EarthRadiusMiles = 3958.7613;

        // ATS route distance is usually longer than straight-line distance.
        // 1.28 gives a practical road-route estimate for freight planning.
        private const double RouteFactor = 1.28;

        private static readonly Dictionary<string, (double Lat, double Lon)> CityCoordinates = new(StringComparer.OrdinalIgnoreCase)
        {
            // Arizona
            ["Phoenix|AZ"] = (33.4484, -112.0740),
            ["Tucson|AZ"] = (32.2226, -110.9747),
            ["Flagstaff|AZ"] = (35.1983, -111.6513),
            ["Yuma|AZ"] = (32.6927, -114.6277),
            ["Kingman|AZ"] = (35.1894, -114.0530),
            ["Page|AZ"] = (36.9147, -111.4558),
            ["Show Low|AZ"] = (34.2542, -110.0298),
            ["Sierra Vista|AZ"] = (31.5455, -110.2773),

            // California
            ["Los Angeles|CA"] = (34.0522, -118.2437),
            ["San Diego|CA"] = (32.7157, -117.1611),
            ["San Francisco|CA"] = (37.7749, -122.4194),
            ["Sacramento|CA"] = (38.5816, -121.4944),
            ["Bakersfield|CA"] = (35.3733, -119.0187),
            ["Fresno|CA"] = (36.7378, -119.7871),
            ["Redding|CA"] = (40.5865, -122.3917),
            ["Oakland|CA"] = (37.8044, -122.2712),
            ["Carlsbad|CA"] = (33.1581, -117.3506),
            ["Santa Cruz|CA"] = (36.9741, -122.0308),
            ["Santa Maria|CA"] = (34.9530, -120.4357),
            ["Huron|CA"] = (36.2027, -120.1029),
            ["Eureka|CA"] = (40.8021, -124.1637),
            ["Barstow|CA"] = (34.8958, -117.0173),
            ["El Centro|CA"] = (32.7920, -115.5631),
            ["Oxnard|CA"] = (34.1975, -119.1771),
            ["Stockton|CA"] = (37.9577, -121.2908),
            ["Truckee|CA"] = (39.3279, -120.1833),

            // Colorado
            ["Denver|CO"] = (39.7392, -104.9903),
            ["Colorado Springs|CO"] = (38.8339, -104.8214),
            ["Pueblo|CO"] = (38.2544, -104.6091),
            ["Grand Junction|CO"] = (39.0639, -108.5506),
            ["Fort Collins|CO"] = (40.5853, -105.0844),
            ["Durango|CO"] = (37.2753, -107.8801),
            ["Sterling|CO"] = (40.6255, -103.2077),
            ["Burlington|CO"] = (39.3061, -102.2694),
            ["Montrose|CO"] = (38.4783, -107.8762),
            ["Lamar|CO"] = (38.0872, -102.6208),
            ["Alamosa|CO"] = (37.4694, -105.8700),

            // Idaho
            ["Boise|ID"] = (43.6150, -116.2023),
            ["Idaho Falls|ID"] = (43.4927, -112.0408),
            ["Twin Falls|ID"] = (42.5558, -114.4701),
            ["Pocatello|ID"] = (42.8713, -112.4455),
            ["Coeur d'Alene|ID"] = (47.6777, -116.7805),
            ["Lewiston|ID"] = (46.4004, -117.0012),
            ["Sandpoint|ID"] = (48.2766, -116.5535),
            ["Salmon|ID"] = (45.1758, -113.8959),
            ["Nampa|ID"] = (43.5407, -116.5635),
            ["Grangeville|ID"] = (45.9266, -116.1224),
            ["Ketchum|ID"] = (43.6807, -114.3637),

            // Kansas
            ["Wichita|KS"] = (37.6872, -97.3301),
            ["Kansas City|KS"] = (39.1141, -94.6275),
            ["Topeka|KS"] = (39.0473, -95.6752),
            ["Dodge City|KS"] = (37.7528, -100.0171),
            ["Garden City|KS"] = (37.9717, -100.8727),
            ["Emporia|KS"] = (38.4039, -96.1817),
            ["Hays|KS"] = (38.8792, -99.3268),
            ["Pittsburg|KS"] = (37.4109, -94.7049),
            ["Colby|KS"] = (39.3958, -101.0524),
            ["Phillipsburg|KS"] = (39.7561, -99.3237),
            ["Marysville|KS"] = (39.8411, -96.6472),

            // Montana
            ["Billings|MT"] = (45.7833, -108.5007),
            ["Missoula|MT"] = (46.8721, -113.9940),
            ["Great Falls|MT"] = (47.5053, -111.3008),
            ["Butte|MT"] = (46.0038, -112.5348),
            ["Helena|MT"] = (46.5891, -112.0391),
            ["Kalispell|MT"] = (48.1919, -114.3168),
            ["Bozeman|MT"] = (45.6770, -111.0429),
            ["Miles City|MT"] = (46.4083, -105.8406),
            ["Glasgow|MT"] = (48.1969, -106.6367),
            ["Havre|MT"] = (48.5500, -109.6841),
            ["Thompson Falls|MT"] = (47.5974, -115.3435),
            ["Sidney|MT"] = (47.7167, -104.1563),
            ["Laurel|MT"] = (45.6691, -108.7715),
            ["Lewistown|MT"] = (47.0625, -109.4282),
            ["Glendive|MT"] = (47.1053, -104.7125),

            // Nebraska
            ["Omaha|NE"] = (41.2565, -95.9345),
            ["Lincoln|NE"] = (40.8136, -96.7026),
            ["Grand Island|NE"] = (40.9264, -98.3420),
            ["North Platte|NE"] = (41.1403, -100.7601),
            ["Scottsbluff|NE"] = (41.8666, -103.6672),
            ["Norfolk|NE"] = (42.0327, -97.4138),
            ["Columbus|NE"] = (41.4303, -97.3594),
            ["Valentine|NE"] = (42.8728, -100.5500),
            ["McCook|NE"] = (40.2019, -100.6257),
            ["Sidney|NE"] = (41.1428, -102.9774),

            // Nevada
            ["Las Vegas|NV"] = (36.1699, -115.1398),
            ["Reno|NV"] = (39.5296, -119.8138),
            ["Elko|NV"] = (40.8324, -115.7631),
            ["Carson City|NV"] = (39.1638, -119.7674),
            ["Winnemucca|NV"] = (40.9730, -117.7357),
            ["Tonopah|NV"] = (38.0672, -117.2301),
            ["Pioche|NV"] = (37.9297, -114.4525),
            ["Ely|NV"] = (39.2474, -114.8886),
            ["Primm|NV"] = (35.6125, -115.3886),

            // New Mexico
            ["Albuquerque|NM"] = (35.0844, -106.6504),
            ["Santa Fe|NM"] = (35.6870, -105.9378),
            ["Roswell|NM"] = (33.3943, -104.5230),
            ["Las Cruces|NM"] = (32.3199, -106.7637),
            ["Farmington|NM"] = (36.7281, -108.2187),
            ["Gallup|NM"] = (35.5281, -108.7426),
            ["Clovis|NM"] = (34.4048, -103.2052),
            ["Raton|NM"] = (36.9034, -104.4392),
            ["Tucumcari|NM"] = (35.1717, -103.7250),
            ["Hobbs|NM"] = (32.7026, -103.1360),
            ["Socorro|NM"] = (34.0584, -106.8914),

            // Oklahoma
            ["Oklahoma City|OK"] = (35.4676, -97.5164),
            ["Tulsa|OK"] = (36.1540, -95.9928),
            ["Lawton|OK"] = (34.6036, -98.3959),
            ["Enid|OK"] = (36.3956, -97.8784),
            ["Woodward|OK"] = (36.4336, -99.3904),
            ["Clinton|OK"] = (35.5156, -98.9673),
            ["McAlester|OK"] = (34.9334, -95.7697),
            ["Ardmore|OK"] = (34.1743, -97.1436),
            ["Guymon|OK"] = (36.6828, -101.4816),
            ["Idabel|OK"] = (33.8957, -94.8263),

            // Oregon
            ["Portland|OR"] = (45.5152, -122.6784),
            ["Salem|OR"] = (44.9429, -123.0351),
            ["Eugene|OR"] = (44.0521, -123.0868),
            ["Bend|OR"] = (44.0582, -121.3153),
            ["Medford|OR"] = (42.3265, -122.8756),
            ["Astoria|OR"] = (46.1879, -123.8313),
            ["Coos Bay|OR"] = (43.3665, -124.2179),
            ["Klamath Falls|OR"] = (42.2249, -121.7817),
            ["Ontario|OR"] = (44.0266, -116.9629),
            ["Pendleton|OR"] = (45.6721, -118.7886),
            ["The Dalles|OR"] = (45.5946, -121.1787),
            ["Burns|OR"] = (43.5863, -119.0541),
            ["Newport|OR"] = (44.6368, -124.0535),
            ["Lakeview|OR"] = (42.1888, -120.3458),

            // Texas
            ["Dallas|TX"] = (32.7767, -96.7970),
            ["Fort Worth|TX"] = (32.7555, -97.3308),
            ["Houston|TX"] = (29.7604, -95.3698),
            ["Austin|TX"] = (30.2672, -97.7431),
            ["San Antonio|TX"] = (29.4241, -98.4936),
            ["El Paso|TX"] = (31.7619, -106.4850),
            ["Lubbock|TX"] = (33.5779, -101.8552),
            ["Amarillo|TX"] = (35.2219, -101.8313),
            ["Odessa|TX"] = (31.8457, -102.3676),
            ["Corpus Christi|TX"] = (27.8006, -97.3964),
            ["Waco|TX"] = (31.5493, -97.1467),
            ["Longview|TX"] = (32.5007, -94.7405),
            ["Tyler|TX"] = (32.3513, -95.3011),
            ["Abilene|TX"] = (32.4487, -99.7331),
            ["Wichita Falls|TX"] = (33.9137, -98.4934),
            ["Victoria|TX"] = (28.8053, -97.0036),
            ["Brownsville|TX"] = (25.9017, -97.4975),
            ["Laredo|TX"] = (27.5306, -99.4803),
            ["McAllen|TX"] = (26.2034, -98.2300),
            ["Beaumont|TX"] = (30.0802, -94.1266),
            ["Texarkana|TX"] = (33.4251, -94.0477),
            ["Junction|TX"] = (30.4894, -99.7720),
            ["Del Rio|TX"] = (29.3709, -100.8959),

            // Utah
            ["Salt Lake City|UT"] = (40.7608, -111.8910),
            ["Ogden|UT"] = (41.2230, -111.9738),
            ["Provo|UT"] = (40.2338, -111.6585),
            ["St. George|UT"] = (37.0965, -113.5684),
            ["Vernal|UT"] = (40.4555, -109.5287),
            ["Moab|UT"] = (38.5733, -109.5498),
            ["Price|UT"] = (39.5994, -110.8107),
            ["Cedar City|UT"] = (37.6775, -113.0619),
            ["Logan|UT"] = (41.73698, -111.8338),
            ["Salina|UT"] = (38.9577, -111.8594),

            // Washington
            ["Seattle|WA"] = (47.6062, -122.3321),
            ["Tacoma|WA"] = (47.2529, -122.4443),
            ["Spokane|WA"] = (47.6588, -117.4260),
            ["Vancouver|WA"] = (45.6387, -122.6615),
            ["Everett|WA"] = (47.9790, -122.2021),
            ["Bellingham|WA"] = (48.7519, -122.4787),
            ["Yakima|WA"] = (46.6021, -120.5059),
            ["Kennewick|WA"] = (46.2112, -119.1372),
            ["Wenatchee|WA"] = (47.4235, -120.3103),
            ["Olympia|WA"] = (47.0379, -122.9007),
            ["Port Angeles|WA"] = (48.1181, -123.4307),
            ["Aberdeen|WA"] = (46.9754, -123.8157),
            ["Longview|WA"] = (46.1382, -122.9382),
            ["Omak|WA"] = (48.4108, -119.5276),

            // Wyoming
            ["Cheyenne|WY"] = (41.1400, -104.8202),
            ["Casper|WY"] = (42.8501, -106.3252),
            ["Rock Springs|WY"] = (41.5875, -109.2029),
            ["Gillette|WY"] = (44.2911, -105.5022),
            ["Jackson|WY"] = (43.4799, -110.7624),
            ["Cody|WY"] = (44.5263, -109.0565),
            ["Laramie|WY"] = (41.3114, -105.5911),
            ["Rawlins|WY"] = (41.7911, -107.2387),
            ["Riverton|WY"] = (43.02496, -108.3801),
            ["Sheridan|WY"] = (44.7972, -106.9562),
            ["Evanston|WY"] = (41.2683, -110.9632)
        };

        public int CalculateMiles(AtsCompanyOption? pickup, AtsCompanyOption? destination)
        {
            if (pickup == null || destination == null)
                return 0;

            return CalculateMiles(pickup.City, pickup.State, destination.City, destination.State);
        }

        public int CalculateMiles(string? originCity, string? originState, string? destinationCity, string? destinationState)
        {
            originCity = Clean(originCity);
            originState = CleanState(originState);
            destinationCity = Clean(destinationCity);
            destinationState = CleanState(destinationState);

            if (string.IsNullOrWhiteSpace(originCity) || string.IsNullOrWhiteSpace(destinationCity))
                return 0;

            if (Same(originCity, destinationCity) && Same(originState, destinationState))
                return 15;

            var origin = ResolveCoordinate(originCity, originState);
            var dest = ResolveCoordinate(destinationCity, destinationState);

            if (origin == null || dest == null)
                return EstimateFallbackMiles(originCity, originState, destinationCity, destinationState);

            var straightLine = HaversineMiles(origin.Value.Lat, origin.Value.Lon, dest.Value.Lat, dest.Value.Lon);
            var routeMiles = straightLine * RouteFactor;

            return Math.Max(25, (int)Math.Round(routeMiles));
        }

        public string BuildRouteSummary(AtsCompanyOption? pickup, AtsCompanyOption? destination)
        {
            if (pickup == null || destination == null)
                return "Select pickup and destination to calculate route.";

            var miles = CalculateMiles(pickup, destination);
            var origin = FormatCityState(pickup.City, pickup.State);
            var dest = FormatCityState(destination.City, destination.State);

            if (miles <= 0)
                return $"Route: {origin} → {dest} • distance unavailable";

            return $"Route: {origin} → {dest} • approx. {miles:n0} miles";
        }

        private static (double Lat, double Lon)? ResolveCoordinate(string city, string state)
        {
            var key = $"{city}|{state}";

            if (CityCoordinates.TryGetValue(key, out var exact))
                return exact;

            foreach (var item in CityCoordinates)
            {
                var parts = item.Key.Split('|');
                if (parts.Length == 2 && Same(parts[0], city))
                    return item.Value;
            }

            return null;
        }

        private static int EstimateFallbackMiles(string originCity, string originState, string destinationCity, string destinationState)
        {
            if (Same(originState, destinationState))
                return 180;

            if (string.IsNullOrWhiteSpace(originState) || string.IsNullOrWhiteSpace(destinationState))
                return 500;

            return 650;
        }

        private static double HaversineMiles(double lat1, double lon1, double lat2, double lon2)
        {
            var dLat = DegreesToRadians(lat2 - lat1);
            var dLon = DegreesToRadians(lon2 - lon1);

            lat1 = DegreesToRadians(lat1);
            lat2 = DegreesToRadians(lat2);

            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(lat1) * Math.Cos(lat2) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return EarthRadiusMiles * c;
        }

        private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

        private static string Clean(string? value)
        {
            return (value ?? "")
                .Replace("@@", "")
                .Replace("_", " ")
                .Replace(".", " ")
                .Trim();
        }

        private static string CleanState(string? value)
        {
            value = Clean(value).ToUpperInvariant();

            if (value.Length > 2)
            {
                value = value switch
                {
                    "ARIZONA" => "AZ",
                    "CALIFORNIA" => "CA",
                    "COLORADO" => "CO",
                    "IDAHO" => "ID",
                    "KANSAS" => "KS",
                    "MONTANA" => "MT",
                    "NEBRASKA" => "NE",
                    "NEVADA" => "NV",
                    "NEW MEXICO" => "NM",
                    "OKLAHOMA" => "OK",
                    "OREGON" => "OR",
                    "TEXAS" => "TX",
                    "UTAH" => "UT",
                    "WASHINGTON" => "WA",
                    "WYOMING" => "WY",
                    _ => value.Length >= 2 ? value.Substring(0, 2) : value
                };
            }

            return value;
        }

        private static string FormatCityState(string? city, string? state)
        {
            city = Clean(city);
            state = CleanState(state);

            if (!string.IsNullOrWhiteSpace(city) && !string.IsNullOrWhiteSpace(state))
                return $"{city}, {state}";

            return string.IsNullOrWhiteSpace(city) ? "Unknown" : city;
        }

        private static bool Same(string? a, string? b)
        {
            return string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
