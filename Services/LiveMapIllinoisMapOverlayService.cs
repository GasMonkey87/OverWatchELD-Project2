using System.Collections.Generic;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public static class LiveMapIllinoisMapOverlayService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public static string BuildIllinoisMapJson()
        {
            return JsonSerializer.Serialize(BuildIllinoisMap(), JsonOptions);
        }

        public static LiveMapIllinoisMapOverlayPayload BuildIllinoisMap()
        {
            var cities = new List<LiveMapIllinoisCityMarker>
            {
                City("Chicago", "IL", 41.8781, -87.6298, "Major City"),
                City("Springfield", "IL", 39.7817, -89.6501, "Capital"),
                City("Rockford", "IL", 42.2711, -89.0937, "City"),
                City("Peoria", "IL", 40.6936, -89.5890, "City"),
                City("Champaign", "IL", 40.1164, -88.2434, "City"),
                City("Bloomington", "IL", 40.4842, -88.9937, "City"),
                City("Moline", "IL", 41.5067, -90.5151, "City"),
                City("Quincy", "IL", 39.9356, -91.4099, "City"),
                City("Effingham", "IL", 39.1200, -88.5434, "City"),
                City("Marion", "IL", 37.7306, -88.9331, "City"),
                City("East St. Louis", "IL", 38.6245, -90.1509, "City"),
                City("Joliet", "IL", 41.5250, -88.0817, "Extra"),
                City("Decatur", "IL", 39.8403, -88.9548, "Extra")
            };

            var roads = new List<LiveMapIllinoisRoadLine>
            {
                Road("I-55", "#4A91D0", "Interstate", new()
                {
                    Point(41.8781, -87.6298),
                    Point(41.5250, -88.0817),
                    Point(40.4842, -88.9937),
                    Point(39.7817, -89.6501),
                    Point(38.6245, -90.1509)
                }),

                Road("I-57", "#4A91D0", "Interstate", new()
                {
                    Point(41.8781, -87.6298),
                    Point(40.1164, -88.2434),
                    Point(39.1200, -88.5434),
                    Point(37.7306, -88.9331)
                }),

                Road("I-74", "#35B474", "Interstate", new()
                {
                    Point(41.5067, -90.5151),
                    Point(40.6936, -89.5890),
                    Point(40.4842, -88.9937),
                    Point(40.1164, -88.2434)
                }),

                Road("I-80 / I-88 Corridor", "#FFB454", "Interstate", new()
                {
                    Point(42.2711, -89.0937),
                    Point(41.5250, -88.0817),
                    Point(41.8781, -87.6298)
                }),

                Road("US-24 / Western Illinois", "#9FB3CC", "US Route", new()
                {
                    Point(39.9356, -91.4099),
                    Point(40.6936, -89.5890),
                    Point(40.4842, -88.9937)
                }),

                Road("I-72", "#9FB3CC", "Interstate", new()
                {
                    Point(39.9356, -91.4099),
                    Point(39.7817, -89.6501),
                    Point(39.8403, -88.9548),
                    Point(40.1164, -88.2434)
                })
            };

            return new LiveMapIllinoisMapOverlayPayload
            {
                Cities = cities,
                Roads = roads
            };
        }

        private static LiveMapIllinoisCityMarker City(string name, string state, double lat, double lon, string kind)
        {
            return new LiveMapIllinoisCityMarker
            {
                Name = name,
                State = state,
                Latitude = lat,
                Longitude = lon,
                Kind = kind
            };
        }

        private static LiveMapIllinoisRoadLine Road(
            string name,
            string color,
            string kind,
            List<LiveMapIllinoisMapPoint> points)
        {
            return new LiveMapIllinoisRoadLine
            {
                Name = name,
                Color = color,
                Kind = kind,
                Points = points
            };
        }

        private static LiveMapIllinoisMapPoint Point(double lat, double lon)
        {
            return new LiveMapIllinoisMapPoint
            {
                Latitude = lat,
                Longitude = lon
            };
        }
    }

    public sealed class LiveMapIllinoisMapOverlayPayload
    {
        public List<LiveMapIllinoisCityMarker> Cities { get; set; } = new();
        public List<LiveMapIllinoisRoadLine> Roads { get; set; } = new();
    }

    public sealed class LiveMapIllinoisCityMarker
    {
        public string Name { get; set; } = "";
        public string State { get; set; } = "";
        public string Kind { get; set; } = "";
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }

    public sealed class LiveMapIllinoisRoadLine
    {
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Color { get; set; } = "#4A91D0";
        public List<LiveMapIllinoisMapPoint> Points { get; set; } = new();
    }

    public sealed class LiveMapIllinoisMapPoint
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }
}
