using System;
using System.Collections.Generic;
using System.Linq;

namespace OverWatchELD.Services
{
    /// <summary>
    /// Resolves a live map longitude/latitude point to the nearest known ATS expansion city/company.
    /// This does not change ATS telemetry conversion. It only gives map markers a nearby readable location.
    /// </summary>
    public static class AtsLocationResolverService
    {
        public static AtsResolvedLiveLocation ResolveNearest(double longitude, double latitude, double maxMiles = 85)
        {
            try
            {
                var resolved = AtsModScannerService.GetResolvedDefinitions();
                var locations = AtsExpansionLiveMapService.BuildExpansionLocations(resolved);
                return ResolveNearest(longitude, latitude, locations, maxMiles);
            }
            catch
            {
                return AtsResolvedLiveLocation.Empty;
            }
        }

        public static AtsResolvedLiveLocation ResolveNearest(
            double longitude,
            double latitude,
            IEnumerable<AtsExpansionLiveMapLocation> locations,
            double maxMiles = 85)
        {
            if (!IsValid(longitude, latitude) || locations == null)
                return AtsResolvedLiveLocation.Empty;

            AtsExpansionLiveMapLocation? best = null;
            var bestMiles = double.MaxValue;

            foreach (var location in locations)
            {
                if (!IsValid(location.Longitude, location.Latitude))
                    continue;

                var miles = DistanceMiles(latitude, longitude, location.Latitude, location.Longitude);
                if (miles < bestMiles)
                {
                    bestMiles = miles;
                    best = location;
                }
            }

            if (best == null || bestMiles > maxMiles)
                return AtsResolvedLiveLocation.Empty;

            return new AtsResolvedLiveLocation
            {
                Found = true,
                Kind = best.Kind,
                Name = best.Name,
                City = best.City,
                Source = best.Source,
                Token = best.Token,
                Longitude = best.Longitude,
                Latitude = best.Latitude,
                DistanceMiles = bestMiles
            };
        }

        private static bool IsValid(double longitude, double latitude)
        {
            return !double.IsNaN(longitude) &&
                   !double.IsNaN(latitude) &&
                   !double.IsInfinity(longitude) &&
                   !double.IsInfinity(latitude) &&
                   Math.Abs(longitude) > 0.01 &&
                   Math.Abs(latitude) > 0.01;
        }

        private static double DistanceMiles(double lat1, double lon1, double lat2, double lon2)
        {
            const double earthMiles = 3958.7613;

            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);
            var rLat1 = ToRadians(lat1);
            var rLat2 = ToRadians(lat2);

            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(rLat1) * Math.Cos(rLat2) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return earthMiles * c;
        }

        private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
    }

    public sealed class AtsResolvedLiveLocation
    {
        public static AtsResolvedLiveLocation Empty { get; } = new();

        public bool Found { get; set; }
        public string Kind { get; set; } = "";
        public string Name { get; set; } = "";
        public string City { get; set; } = "";
        public string Source { get; set; } = "";
        public string Token { get; set; } = "";
        public double Longitude { get; set; }
        public double Latitude { get; set; }
        public double DistanceMiles { get; set; }

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(City) && !string.IsNullOrWhiteSpace(Name) &&
                    !string.Equals(City, Name, StringComparison.OrdinalIgnoreCase))
                    return $"{City} - {Name}";

                if (!string.IsNullOrWhiteSpace(City))
                    return City;

                return Name;
            }
        }
    }
}
