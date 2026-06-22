using System;
using System.Collections.Generic;
using System.Linq;

namespace OverWatchELD.Services
{
    public sealed class AtsSaveFreightJob
    {
        public string CargoToken { get; set; } = "";
        public string TrailerToken { get; set; } = "";
        public string SourceCompanyToken { get; set; } = "";
        public string DestinationCompanyToken { get; set; } = "";
        public string SourceCityToken { get; set; } = "";
        public string DestinationCityToken { get; set; } = "";

        public string DisplayCargo { get; set; } = "";
        public string DisplayTrailer { get; set; } = "";
        public string DisplaySourceCompany { get; set; } = "";
        public string DisplayDestinationCompany { get; set; } = "";
        public string DisplaySourceCity { get; set; } = "";
        public string DisplayDestinationCity { get; set; } = "";
        public string DisplaySourceState { get; set; } = "";
        public string DisplayDestinationState { get; set; } = "";

        public int Income { get; set; }
        public int DistanceMiles { get; set; }
        public int WeightLbs { get; set; }

        public DateTime ExpirationUtc { get; set; }
        public DateTime DeadlineUtc { get; set; }

        public bool RequiresLowboy { get; set; }
        public bool RequiresReefer { get; set; }
        public bool RequiresTanker { get; set; }
        public bool RequiresGasTanker { get; set; }
        public bool IsHazmatLike { get; set; }
        public bool IsOversizeLike { get; set; }
    }

    public sealed class AtsSaveJobMapResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public List<string> Errors { get; set; } = new();
        public AtsSaveFreightJob? Job { get; set; }
    }

    public static class AtsSaveJobMapperService
    {
        private static readonly Dictionary<string, string> CargoTokenMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Nitrogen"] = "nitrogen",
                ["Oxygen"] = "oxygen",
                ["Argon"] = "argon",
                ["Lng"] = "lng",
                ["Lng Tank"] = "lng",
                ["Propane"] = "propane",
                ["Industrial Gas"] = "industrial_gas",
                ["Cryogenic Liquid"] = "cryogenic_liquid",

                ["Bulldozer"] = "bulldozer",
                ["Excavator"] = "excavator",
                ["Transformer"] = "transformer",
                ["Roller"] = "roller",
                ["Crane"] = "crane",
                ["Large Machinery"] = "large_machinery",
                ["Oversize Equipment"] = "oversize_equipment",

                ["Lumber"] = "lumber",
                ["Steel Coils"] = "steel_coils",
                ["Steel Beams"] = "steel_beams",
                ["Pipes"] = "pipes",
                ["Bricks"] = "bricks",

                ["Fuel"] = "fuel",
                ["Diesel"] = "diesel",
                ["Gasoline"] = "gasoline",
                ["Milk"] = "milk",
                ["Chemicals"] = "chemicals",

                ["Cars"] = "cars",
                ["Vehicles"] = "vehicles",

                ["Refrigerated Goods"] = "refrigerated_goods",
                ["Frozen Food"] = "frozen_food",

                ["Furniture"] = "furniture",
                ["General Goods"] = "general_goods",
                ["Consumer Goods"] = "consumer_goods",

                ["Grain"] = "grain",
                ["Ore"] = "ore"
            };

        private static readonly Dictionary<string, string> TrailerTokenMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Lowboy"] = "lowboy",
                ["Lowbed"] = "lowbed",
                ["Flatbed"] = "flatbed",
                ["Curtainsider"] = "curtainsider",
                ["Dry Van"] = "dryvan",
                ["Reefer"] = "reefer",
                ["Tanker"] = "tanker",
                ["Chemical Tanker"] = "chemical_tanker",
                ["Gas Tanker"] = "gas_tanker",
                ["Cryogenic Tanker"] = "cryogenic_tanker",
                ["Car Hauler"] = "car_hauler",
                ["Hopper"] = "hopper"
            };

        private static readonly Dictionary<string, string> CompanyTokenMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Wallbert"] = "wallbert",
                ["Sellgoods"] = "sellgoods",
                ["SellGoods"] = "sellgoods",
                ["Charged"] = "charged",
                ["Bushnell Farms"] = "bushnell_farms",
                ["Bushnell"] = "bushnell_farms",
                ["Voltison"] = "voltison",
                ["Rail Export"] = "rail_export",
                ["Railexport"] = "rail_export",
                ["Port"] = "port"
            };

        public static AtsSaveJobMapResult MapFromDispatchJob(DispatchJob? source)
        {
            var result = new AtsSaveJobMapResult();

            if (source == null)
            {
                result.Success = false;
                result.Message = "Dispatch job is null.";
                result.Errors.Add("Dispatch job is null.");
                return result;
            }

            AtsDataService.EnsureLoaded();

            var cargo = Clean(source.Cargo);
            var trailer = Clean(source.Trailer);
            var sourceCompany = Clean(source.Company);
            var sourceCity = Clean(source.OriginCity);
            var sourceState = Clean(source.OriginState);
            var destinationCity = Clean(source.DestinationCity);
            var destinationState = Clean(source.DestinationState);

            if (string.IsNullOrWhiteSpace(cargo))
                result.Errors.Add("Cargo is required.");

            if (string.IsNullOrWhiteSpace(trailer))
                result.Errors.Add("Trailer is required.");

            if (string.IsNullOrWhiteSpace(sourceCompany))
                result.Errors.Add("Origin company is required.");

            if (string.IsNullOrWhiteSpace(sourceCity) || string.IsNullOrWhiteSpace(sourceState))
                result.Errors.Add("Origin city/state is required.");

            if (string.IsNullOrWhiteSpace(destinationCity) || string.IsNullOrWhiteSpace(destinationState))
                result.Errors.Add("Destination city/state is required.");

            if (result.Errors.Count > 0)
            {
                result.Success = false;
                result.Message = string.Join(" ", result.Errors);
                return result;
            }

            if (!AtsDataService.IsCargoTrailerCompatible(cargo, trailer))
            {
                result.Errors.Add($"Cargo '{cargo}' is not compatible with trailer '{trailer}'.");
                result.Success = false;
                result.Message = string.Join(" ", result.Errors);
                return result;
            }

            var mappedCargo = MapCargoToken(cargo);
            var mappedTrailer = MapTrailerToken(trailer);
            var mappedSourceCompany = MapCompanyToken(sourceCompany);
            var mappedSourceCity = MapCityToken(sourceCity);
            var mappedDestinationCity = MapCityToken(destinationCity);

            if (string.IsNullOrWhiteSpace(mappedCargo))
                result.Errors.Add($"No ATS cargo token mapping for '{cargo}'.");

            if (string.IsNullOrWhiteSpace(mappedTrailer))
                result.Errors.Add($"No ATS trailer token mapping for '{trailer}'.");

            if (string.IsNullOrWhiteSpace(mappedSourceCompany))
                result.Errors.Add($"No ATS company token mapping for '{sourceCompany}'.");

            if (string.IsNullOrWhiteSpace(mappedSourceCity))
                result.Errors.Add($"No ATS city token mapping for '{sourceCity}'.");

            if (string.IsNullOrWhiteSpace(mappedDestinationCity))
                result.Errors.Add($"No ATS city token mapping for '{destinationCity}'.");

            var destinationCompany = ResolveDestinationCompany(source);
            var mappedDestinationCompany = MapCompanyToken(destinationCompany);

            if (string.IsNullOrWhiteSpace(mappedDestinationCompany))
                mappedDestinationCompany = GuessDestinationCompanyToken(destinationCity, destinationState);

            if (string.IsNullOrWhiteSpace(mappedDestinationCompany))
                result.Errors.Add($"No ATS destination company token mapping for '{destinationCompany}'.");

            if (result.Errors.Count > 0)
            {
                result.Success = false;
                result.Message = string.Join(" ", result.Errors);
                return result;
            }

            var miles = source.Miles > 0
                ? source.Miles
                : AtsDataService.CalculateMiles(sourceCity, sourceState, destinationCity, destinationState);

            if (miles <= 0)
                miles = 100;

            var weight = ResolveWeight(source, cargo, trailer);
            var income = ResolveIncome(source, miles, weight, cargo, trailer);

            var pickupLocal = ResolvePickupDate(source);
            var deadlineLocal = ResolveDeadline(source, pickupLocal, miles);
            var expirationUtc = pickupLocal.ToUniversalTime();
            var deadlineUtc = deadlineLocal.ToUniversalTime();

            var job = new AtsSaveFreightJob
            {
                CargoToken = mappedCargo,
                TrailerToken = mappedTrailer,
                SourceCompanyToken = mappedSourceCompany,
                DestinationCompanyToken = mappedDestinationCompany,
                SourceCityToken = mappedSourceCity,
                DestinationCityToken = mappedDestinationCity,

                DisplayCargo = cargo,
                DisplayTrailer = trailer,
                DisplaySourceCompany = sourceCompany,
                DisplayDestinationCompany = destinationCompany,
                DisplaySourceCity = sourceCity,
                DisplayDestinationCity = destinationCity,
                DisplaySourceState = sourceState,
                DisplayDestinationState = destinationState,

                Income = income,
                DistanceMiles = miles,
                WeightLbs = weight,

                ExpirationUtc = expirationUtc,
                DeadlineUtc = deadlineUtc,

                RequiresLowboy = TokenContains(mappedTrailer, "lowboy", "lowbed"),
                RequiresReefer = TokenContains(mappedTrailer, "reefer"),
                RequiresTanker = TokenContains(mappedTrailer, "tanker", "chemical_tanker"),
                RequiresGasTanker = TokenContains(mappedTrailer, "gas_tanker", "cryogenic_tanker"),
                IsHazmatLike = IsHazmatLike(cargo),
                IsOversizeLike = IsOversizeLike(cargo)
            };

            result.Job = job;
            result.Success = true;
            result.Message = "Dispatch job mapped to ATS save job successfully.";
            return result;
        }

        public static string MapCargoToken(string? cargo)
        {
            var clean = Clean(cargo);
            if (string.IsNullOrWhiteSpace(clean))
                return "";

            if (CargoTokenMap.TryGetValue(clean, out var token))
                return token;

            if (ContainsAny(clean, "nitrogen")) return "nitrogen";
            if (ContainsAny(clean, "oxygen")) return "oxygen";
            if (ContainsAny(clean, "argon")) return "argon";
            if (ContainsAny(clean, "lng")) return "lng";
            if (ContainsAny(clean, "propane")) return "propane";
            if (ContainsAny(clean, "cryogenic")) return "cryogenic_liquid";
            if (ContainsAny(clean, "industrial gas")) return "industrial_gas";

            if (ContainsAny(clean, "bulldozer")) return "bulldozer";
            if (ContainsAny(clean, "excavator")) return "excavator";
            if (ContainsAny(clean, "transformer")) return "transformer";
            if (ContainsAny(clean, "roller")) return "roller";
            if (ContainsAny(clean, "crane")) return "crane";
            if (ContainsAny(clean, "machinery")) return "large_machinery";
            if (ContainsAny(clean, "oversize")) return "oversize_equipment";

            if (ContainsAny(clean, "lumber")) return "lumber";
            if (ContainsAny(clean, "steel coil")) return "steel_coils";
            if (ContainsAny(clean, "steel beam")) return "steel_beams";
            if (ContainsAny(clean, "pipe")) return "pipes";
            if (ContainsAny(clean, "brick")) return "bricks";

            if (ContainsAny(clean, "fuel")) return "fuel";
            if (ContainsAny(clean, "diesel")) return "diesel";
            if (ContainsAny(clean, "gasoline")) return "gasoline";
            if (ContainsAny(clean, "milk")) return "milk";
            if (ContainsAny(clean, "chemical")) return "chemicals";

            if (ContainsAny(clean, "car")) return "cars";
            if (ContainsAny(clean, "vehicle")) return "vehicles";

            if (ContainsAny(clean, "refrigerated")) return "refrigerated_goods";
            if (ContainsAny(clean, "frozen")) return "frozen_food";

            if (ContainsAny(clean, "furniture")) return "furniture";
            if (ContainsAny(clean, "general goods")) return "general_goods";
            if (ContainsAny(clean, "consumer goods")) return "consumer_goods";

            if (ContainsAny(clean, "grain")) return "grain";
            if (ContainsAny(clean, "ore")) return "ore";

            return Slugify(clean);
        }

        public static string MapTrailerToken(string? trailer)
        {
            var clean = Clean(trailer);
            if (string.IsNullOrWhiteSpace(clean))
                return "";

            var normalized = AtsDataService.Trailers
                .FirstOrDefault(x => TrailerLike(x, clean));

            if (!string.IsNullOrWhiteSpace(normalized) &&
                TrailerTokenMap.TryGetValue(normalized, out var mappedKnown))
                return mappedKnown;

            if (TrailerTokenMap.TryGetValue(clean, out var token))
                return token;

            if (ContainsAny(clean, "cryogenic")) return "cryogenic_tanker";
            if (ContainsAny(clean, "gas tanker", "gas tank")) return "gas_tanker";
            if (ContainsAny(clean, "chemical tanker", "chemical tank")) return "chemical_tanker";
            if (ContainsAny(clean, "lowboy")) return "lowboy";
            if (ContainsAny(clean, "lowbed", "low bed")) return "lowbed";
            if (ContainsAny(clean, "flatbed", "flat bed")) return "flatbed";
            if (ContainsAny(clean, "curtain")) return "curtainsider";
            if (ContainsAny(clean, "dry van", "dryvan", "box trailer", "boxtrailer", "van")) return "dryvan";
            if (ContainsAny(clean, "reefer")) return "reefer";
            if (ContainsAny(clean, "tanker")) return "tanker";
            if (ContainsAny(clean, "car hauler", "car trailer")) return "car_hauler";
            if (ContainsAny(clean, "hopper")) return "hopper";

            return Slugify(clean);
        }

        public static string MapCompanyToken(string? company)
        {
            var clean = Clean(company);
            if (string.IsNullOrWhiteSpace(clean))
                return "";

            if (CompanyTokenMap.TryGetValue(clean, out var token))
                return token;

            var known = AtsDataService.Companies.FirstOrDefault(x =>
                string.Equals(Clean(x), clean, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(known) &&
                CompanyTokenMap.TryGetValue(known, out var mappedKnown))
                return mappedKnown;

            if (ContainsAny(clean, "wallbert")) return "wallbert";
            if (ContainsAny(clean, "sellgoods")) return "sellgoods";
            if (ContainsAny(clean, "charged")) return "charged";
            if (ContainsAny(clean, "bushnell")) return "bushnell_farms";
            if (ContainsAny(clean, "voltison")) return "voltison";
            if (ContainsAny(clean, "rail export", "railexport")) return "rail_export";
            if (ContainsAny(clean, "port")) return "port";

            return Slugify(clean);
        }

        public static string MapCityToken(string? city)
        {
            var clean = Clean(city);
            if (string.IsNullOrWhiteSpace(clean))
                return "";

            var known = AtsDataService.Cities.FirstOrDefault(c =>
                string.Equals(Clean(c.City), clean, StringComparison.OrdinalIgnoreCase));

            if (known != null && !string.IsNullOrWhiteSpace(known.City))
                return Slugify(known.City);

            return Slugify(clean);
        }

        private static string ResolveDestinationCompany(DispatchJob source)
        {
            var destCompanyProp = TryReadStringProperty(source, "DestinationCompany");
            if (!string.IsNullOrWhiteSpace(destCompanyProp))
                return Clean(destCompanyProp);

            var deliveryCompanyProp = TryReadStringProperty(source, "DeliveryCompany");
            if (!string.IsNullOrWhiteSpace(deliveryCompanyProp))
                return Clean(deliveryCompanyProp);

            var companies = AtsDataService.GetCompaniesByCityState(source.DestinationCity, source.DestinationState);
            var first = companies.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(first))
                return first;

            return "Wallbert";
        }

        private static string GuessDestinationCompanyToken(string? city, string? state)
        {
            var companies = AtsDataService.GetCompaniesByCityState(city, state);
            var first = companies.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(first))
                return "";

            return MapCompanyToken(first);
        }

        private static int ResolveWeight(DispatchJob source, string cargo, string trailer)
        {
            var weight = 0;

            if (source.ActualCargoWeightLbs > 0)
                weight = SafeToInt(source.ActualCargoWeightLbs);
            else if (source.CargoWeight > 0)
                weight = SafeToInt(source.CargoWeight);

            if (weight > 0)
                return weight;

            var c = Normalize(cargo);
            var t = Normalize(trailer);

            if (ContainsAny(c, "bulldozer", "excavator", "transformer", "roller", "crane", "machinery"))
                return 68000;

            if (ContainsAny(c, "nitrogen", "oxygen", "argon", "lng", "propane", "cryogenic"))
                return 44000;

            if (ContainsAny(c, "fuel", "diesel", "gasoline", "milk", "chemical"))
                return 52000;

            if (ContainsAny(c, "steel", "beam", "pipe", "brick", "lumber"))
                return 32000;

            if (ContainsAny(c, "car", "vehicle"))
                return 18000;

            if (ContainsAny(t, "reefer"))
                return 26000;

            return 24000;
        }

        private static int ResolveIncome(DispatchJob source, int miles, int weight, string cargo, string trailer)
        {
            if (source.Payout > 0)
                return SafeToInt(source.Payout);

            if (source.RevenueUsd > 0)
                return SafeToInt(source.RevenueUsd);

            decimal rate = 3.10m;

            var c = Normalize(cargo);
            var t = Normalize(trailer);

            if (ContainsAny(c, "bulldozer", "excavator", "transformer", "roller", "crane", "machinery"))
                rate += 1.35m;

            if (ContainsAny(c, "nitrogen", "oxygen", "argon", "lng", "propane", "cryogenic", "chemical"))
                rate += 0.95m;

            if (ContainsAny(t, "reefer"))
                rate += 0.55m;

            if (weight >= 70000)
                rate += 0.85m;
            else if (weight >= 45000)
                rate += 0.35m;

            var total = ((decimal)miles * rate) + 175m;
            return SafeToInt(total);
        }

        private static DateTime ResolvePickupDate(DispatchJob source)
        {
            var pickup = TryReadDateTimeProperty(source, "PickupDate");
            if (pickup.HasValue)
                return pickup.Value;

            var created = TryReadDateTimeProperty(source, "CreatedUtc");
            if (created.HasValue)
                return created.Value.ToLocalTime();

            return DateTime.Now.AddHours(1);
        }

        private static DateTime ResolveDeadline(DispatchJob source, DateTime pickupLocal, int miles)
        {
            var deadline = TryReadDateTimeProperty(source, "DeliveryDeadline");
            if (deadline.HasValue)
                return deadline.Value;

            var driveHours = miles / 50.0;
            return pickupLocal.AddHours(driveHours + 12.0);
        }

        private static bool IsHazmatLike(string cargo)
        {
            var c = Normalize(cargo);
            return ContainsAny(c, "nitrogen", "oxygen", "argon", "lng", "propane", "cryogenic", "chemical");
        }

        private static bool IsOversizeLike(string cargo)
        {
            var c = Normalize(cargo);
            return ContainsAny(c, "bulldozer", "excavator", "transformer", "roller", "crane", "machinery", "oversize");
        }

        private static bool TrailerLike(string? trailerA, string? trailerB)
        {
            var a = Normalize(trailerA);
            var b = Normalize(trailerB);

            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                return false;

            return a == b || a.Contains(b, StringComparison.OrdinalIgnoreCase) || b.Contains(a, StringComparison.OrdinalIgnoreCase);
        }

        private static string? TryReadStringProperty(object source, string propertyName)
        {
            try
            {
                var prop = source.GetType().GetProperty(propertyName);
                if (prop == null)
                    return null;

                return prop.GetValue(source) as string;
            }
            catch
            {
                return null;
            }
        }

        private static DateTime? TryReadDateTimeProperty(object source, string propertyName)
        {
            try
            {
                var prop = source.GetType().GetProperty(propertyName);
                if (prop == null)
                    return null;

                var value = prop.GetValue(source);
                if (value == null)
                    return null;

                if (value is DateTime dt)
                    return dt;

                if (value is string s && DateTime.TryParse(s, out var parsed))
                    return parsed;

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static int SafeToInt(decimal value)
        {
            if (value <= 0)
                return 0;

            if (value > int.MaxValue)
                return int.MaxValue;

            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }

        private static int SafeToInt(double value)
        {
            if (value <= 0)
                return 0;

            if (value > int.MaxValue)
                return int.MaxValue;

            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }

        private static bool TokenContains(string value, params string[] needles)
        {
            var n = Normalize(value);
            foreach (var needle in needles)
            {
                if (n.Contains(Normalize(needle), StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string Clean(string? value)
        {
            return (value ?? "").Trim();
        }

        private static string Normalize(string? value)
        {
            return (value ?? "")
                .Trim()
                .Replace("_", "")
                .Replace("-", "")
                .ToLowerInvariant();
        }

        private static bool ContainsAny(string value, params string[] needles)
        {
            foreach (var needle in needles)
            {
                if (value.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string Slugify(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            var chars = value
                .Trim()
                .ToLowerInvariant()
                .Select(ch =>
                {
                    if (char.IsLetterOrDigit(ch))
                        return ch;
                    return '_';
                })
                .ToArray();

            var raw = new string(chars);

            while (raw.Contains("__", StringComparison.Ordinal))
                raw = raw.Replace("__", "_", StringComparison.Ordinal);

            return raw.Trim('_');
        }
    }
}