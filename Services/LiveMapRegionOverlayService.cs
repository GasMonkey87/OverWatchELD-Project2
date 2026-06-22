using System.Collections.Generic;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public static class LiveMapRegionOverlayService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public static string BuildRegionOverlaysJson()
        {
            return JsonSerializer.Serialize(BuildRegionOverlays(), JsonOptions);
        }

        public static List<LiveMapRegionOverlayRow> BuildRegionOverlays()
        {
            return new List<LiveMapRegionOverlayRow>
            {
                new()
                {
                    Name = "United States",
                    ShortName = "USA",
                    Stroke = "#4A91D0",
                    Fill = "#163B65",
                    Opacity = 0.045,
                    Points = new()
                    {
                        new() { Latitude = 49.4, Longitude = -124.8 },
                        new() { Latitude = 49.4, Longitude = -66.8 },
                        new() { Latitude = 24.4, Longitude = -66.8 },
                        new() { Latitude = 24.4, Longitude = -124.8 }
                    }
                },

                // Illinois ATS DLC overlay.
                // This makes Illinois visible as its own map region/layer on the live map.
                // It is intentionally approximate and visual-only; it does not alter driver
                // marker conversion, telemetry, or the locked expansion resolver.
                new()
                {
                    Name = "Illinois DLC",
                    ShortName = "Illinois",
                    Stroke = "#FFB454",
                    Fill = "#B7791F",
                    Opacity = 0.18,
                    Points = new()
                    {
                        // Approximate Illinois outline clockwise.
                        new() { Latitude = 42.5083, Longitude = -91.5131 },
                        new() { Latitude = 42.5083, Longitude = -87.0199 },
                        new() { Latitude = 41.7606, Longitude = -87.5250 },
                        new() { Latitude = 39.0010, Longitude = -87.5320 },
                        new() { Latitude = 37.8000, Longitude = -88.0710 },
                        new() { Latitude = 37.0660, Longitude = -89.1750 },
                        new() { Latitude = 36.9700, Longitude = -89.4170 },
                        new() { Latitude = 37.3390, Longitude = -89.5170 },
                        new() { Latitude = 38.8800, Longitude = -90.1800 },
                        new() { Latitude = 40.4200, Longitude = -91.4000 },
                        new() { Latitude = 42.5083, Longitude = -91.5131 }
                    }
                },

                new()
                {
                    Name = "Canada Expansion Region",
                    ShortName = "Canada",
                    Stroke = "#35B474",
                    Fill = "#1F7A4D",
                    Opacity = 0.08,
                    Points = new()
                    {
                        new() { Latitude = 70.0, Longitude = -141.0 },
                        new() { Latitude = 70.0, Longitude = -52.0 },
                        new() { Latitude = 49.0, Longitude = -52.0 },
                        new() { Latitude = 49.0, Longitude = -141.0 }
                    }
                },
                new()
                {
                    Name = "Mexico Expansion Region",
                    ShortName = "Mexico",
                    Stroke = "#FFB454",
                    Fill = "#B7791F",
                    Opacity = 0.08,
                    Points = new()
                    {
                        new() { Latitude = 33.2, Longitude = -118.5 },
                        new() { Latitude = 33.2, Longitude = -86.5 },
                        new() { Latitude = 14.0, Longitude = -86.5 },
                        new() { Latitude = 14.0, Longitude = -118.5 }
                    }
                }
            };
        }
    }

    public sealed class LiveMapRegionOverlayRow
    {
        public string Name { get; set; } = "";
        public string ShortName { get; set; } = "";
        public string Stroke { get; set; } = "#4A91D0";
        public string Fill { get; set; } = "#163B65";
        public double Opacity { get; set; } = 0.08;
        public List<LiveMapRegionOverlayPoint> Points { get; set; } = new();
    }

    public sealed class LiveMapRegionOverlayPoint
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }
}
