using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OverWatchELD.Services
{
    public static class AtsLoadBuilderService
    {
        public static List<AtsGeneratedLoad> BuildLoads(AtsResolvedDefinitionCache data)
        {
            var loads = new List<AtsGeneratedLoad>();

            var routes = BuildCompanyRoutes(data);

            foreach (var route in routes)
            {
                var cargo = data.Cargoes.FirstOrDefault(c =>
                    string.Equals(c.Token, route.CargoToken, StringComparison.OrdinalIgnoreCase));

                if (cargo == null)
                    continue;

                var trailers = data.Trailers
                    .Where(t =>
                        cargo.AllowedTrailerTokens.Count == 0 ||
                        cargo.AllowedTrailerTokens.Contains(t.Token, StringComparer.OrdinalIgnoreCase))
                    .ToList();

                if (trailers.Count == 0)
                    continue;

                foreach (var trailer in trailers)
                {
                    loads.Add(new AtsGeneratedLoad
                    {
                        Cargo = cargo.DisplayName,
                        CargoToken = cargo.Token,
                        CargoSource = cargo.SourceLabel,

                        Trailer = trailer.DisplayName,
                        TrailerToken = trailer.Token,
                        TrailerSource = trailer.SourceLabel,

                        OriginCompany = route.OriginCompany,
                        DestinationCompany = route.DestinationCompany
                    });
                }
            }

            return loads;
        }

        private static List<RouteEntry> BuildCompanyRoutes(AtsResolvedDefinitionCache data)
        {
            var routes = new List<RouteEntry>();

            // Group companies by token
            var companies = data.Companies.ToDictionary(x => x.Token, StringComparer.OrdinalIgnoreCase);

            // Extract IN / OUT logic from tokens
            foreach (var company in data.Companies)
            {
                var token = company.Token.ToLower();

                // OUT (origin)
                if (token.Contains(".out."))
                {
                    var cargoToken = ExtractCargoToken(token);
                    if (string.IsNullOrWhiteSpace(cargoToken))
                        continue;

                    var companyName = company.DisplayName;

                    // find matching IN
                    foreach (var dest in data.Companies)
                    {
                        if (!dest.Token.Contains(".in."))
                            continue;

                        var destCargo = ExtractCargoToken(dest.Token);

                        if (!string.Equals(destCargo, cargoToken, StringComparison.OrdinalIgnoreCase))
                            continue;

                        routes.Add(new RouteEntry
                        {
                            CargoToken = cargoToken,
                            OriginCompany = companyName,
                            DestinationCompany = dest.DisplayName
                        });
                    }
                }
            }

            return routes;
        }

        private static string ExtractCargoToken(string token)
        {
            // Example: company.walmart.out.cargo_tank
            var parts = token.Split('.');
            return parts.LastOrDefault() ?? "";
        }
    }

    public sealed class AtsGeneratedLoad
    {
        public string Cargo { get; set; } = "";
        public string CargoToken { get; set; } = "";
        public string CargoSource { get; set; } = "";

        public string Trailer { get; set; } = "";
        public string TrailerToken { get; set; } = "";
        public string TrailerSource { get; set; } = "";

        public string OriginCompany { get; set; } = "";
        public string DestinationCompany { get; set; } = "";
    }

    internal sealed class RouteEntry
    {
        public string CargoToken { get; set; } = "";
        public string OriginCompany { get; set; } = "";
        public string DestinationCompany { get; set; } = "";
    }
}