using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public static class LiveMapExpansionDepotMarkerService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public static string BuildDepotMarkersJson()
        {
            return JsonSerializer.Serialize(BuildDepotMarkers(), JsonOptions);
        }

        public static List<LiveMapExpansionDepotMarkerRow> BuildDepotMarkers()
        {
            var rows = new List<LiveMapExpansionDepotMarkerRow>();

            try
            {
                var resolved = AtsModScannerService.GetResolvedDefinitions();

                foreach (var company in resolved.Companies)
                {
                    var city = resolved.Cities.FirstOrDefault(c =>
                        Same(c.Token, company.CityToken) ||
                        Same(c.DisplayName, company.CityToken));

                    rows.Add(new LiveMapExpansionDepotMarkerRow
                    {
                        CompanyName = company.DisplayName,
                        CompanyToken = company.Token,
                        CityName = city?.DisplayName ?? company.CityToken ?? "",
                        CityToken = company.CityToken ?? "",
                        Source = company.SourceLabel,
                        Kind = "Company"
                    });
                }
            }
            catch
            {
            }

            return rows
                .Where(x => !string.IsNullOrWhiteSpace(x.CompanyName))
                .GroupBy(x => $"{x.CompanyName}|{x.CityName}|{x.Source}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(x => x.CityName)
                .ThenBy(x => x.CompanyName)
                .Take(1000)
                .ToList();
        }

        private static bool Same(string? a, string? b)
        {
            return !string.IsNullOrWhiteSpace(a) &&
                   !string.IsNullOrWhiteSpace(b) &&
                   string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class LiveMapExpansionDepotMarkerRow
    {
        public string CompanyName { get; set; } = "";
        public string CompanyToken { get; set; } = "";
        public string CityName { get; set; } = "";
        public string CityToken { get; set; } = "";
        public string Source { get; set; } = "";
        public string Kind { get; set; } = "";
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public double? WorldX { get; set; }
        public double? WorldZ { get; set; }
    }
}
