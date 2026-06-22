using System;
using System.Collections.Generic;
using System.Linq;

namespace OverWatchELD.Services
{
    public sealed class AtsGenerateLoadOptions
    {
        public string Trailer { get; set; } = "";
        public string OriginState { get; set; } = "";
        public string OriginCity { get; set; } = "";
        public string DestinationState { get; set; } = "";
        public string DestinationCity { get; set; } = "";
        public string AssignedDriver { get; set; } = "Unassigned";
        public string AssignedTruck { get; set; } = "";
        public string Priority { get; set; } = "Normal";
        public string TrailerOwner { get; set; } = "Company";
        public bool IsConvoyLoad { get; set; }
        public string ConvoyName { get; set; } = "";
        public bool AutoFleetSync { get; set; } = true;
        public bool PreferModsFolderData { get; set; } = true;
        public DateTime? PickupDateLocal { get; set; }
        public int Count { get; set; } = 1;
    }

    public sealed class AtsGeneratedLoadBatch
    {
        public List<DispatchJob> Jobs { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    public static class AtsSmartLoadGeneratorService
    {
        private static readonly Random Rng = new();

        public static DispatchJob? GenerateOne(AtsGenerateLoadOptions? options = null)
        {
            options ??= new AtsGenerateLoadOptions();

            AtsDataService.EnsureLoaded();
            var mods = options.PreferModsFolderData ? AtsModScannerService.ScanDefault() : null;

            var trailer = PickTrailer(options.Trailer, mods);
            if (string.IsNullOrWhiteSpace(trailer))
                return null;

            var compatibleCargoes = GetCompatibleCargoesForTrailer(trailer, mods);
            if (compatibleCargoes.Count == 0)
                return null;

            for (var attempt = 0; attempt < 25; attempt++)
            {
                var cargo = PickRandom(compatibleCargoes);
                if (string.IsNullOrWhiteSpace(cargo))
                    continue;

                var origin = PickOrigin(options);
                if (origin == null)
                    continue;

                var destination = PickDestination(options, origin);
                if (destination == null)
                    continue;

                var miles = AtsDataService.CalculateMiles(
                    origin.City,
                    origin.State,
                    destination.City,
                    destination.State);

                if (miles <= 0)
                    miles = EstimateMilesFallback(origin, destination);

                var weight = EstimateWeightLbs(cargo, trailer);
                var payout = EstimatePayoutUsd(cargo, trailer, miles, weight);
                var pickupLocal = options.PickupDateLocal ?? DateTime.Now.AddHours(Rng.Next(1, 8));
                var deadlineLocal = EstimateDeadline(pickupLocal, miles, cargo, trailer);

                var job = new DispatchJob
                {
                    Id = Guid.NewGuid().ToString("N"),
                    LoadNumber = DispatchService.NextLoadNumber(),

                    Company = origin.Company,
                    OriginCity = origin.City,
                    OriginState = origin.State,
                    DestinationCity = destination.City,
                    DestinationState = destination.State,

                    Miles = miles,
                    Cargo = cargo,
                    Trailer = trailer,

                    AssignedDriver = string.IsNullOrWhiteSpace(options.AssignedDriver) ? "Unassigned" : options.AssignedDriver,
                    AssignedTruck = options.AssignedTruck ?? "",
                    Status = "Available",
                    Notes = BuildNotes(cargo, trailer, weight, miles),

                    CreatedUtc = DateTime.UtcNow,
                    UpdatedUtc = DateTime.UtcNow,
                    PostedUtc = DateTime.UtcNow,
                    PostedBy = "Dispatcher",

                    PickupDate = pickupLocal,
                    DeliveryDeadline = deadlineLocal,

                    Payout = payout,
                    RevenueUsd = payout,
                    CargoWeight = weight,
                    ActualCargoWeightLbs = weight,

                    Priority = string.IsNullOrWhiteSpace(options.Priority) ? "Normal" : options.Priority,
                    TrailerOwner = string.IsNullOrWhiteSpace(options.TrailerOwner) ? "Company" : options.TrailerOwner,
                    IsConvoyLoad = options.IsConvoyLoad,
                    ConvoyName = options.ConvoyName ?? "",
                    AutoFleetSync = options.AutoFleetSync,

                    DispatchMode = "Open",
                    ClaimedBy = "",
                    IsClaimLocked = false
                };

                var validation = AtsLoadValidationService.Validate(job.Cargo, job.Company, job.Trailer, mods);
                if (!validation.IsValid)
                    continue;

                return job;
            }

            return null;
        }

        public static AtsGeneratedLoadBatch GenerateBatch(AtsGenerateLoadOptions? options = null)
        {
            options ??= new AtsGenerateLoadOptions();

            var result = new AtsGeneratedLoadBatch();
            var count = options.Count <= 0 ? 1 : options.Count;

            for (var i = 0; i < count; i++)
            {
                var job = GenerateOne(options);
                if (job == null)
                {
                    result.Warnings.Add($"Failed to generate valid ATS load #{i + 1}.");
                    continue;
                }

                result.Jobs.Add(job);
            }

            return result;
        }

        public static List<string> GetCompatibleCargoesForTrailer(string? trailer, AtsModScanResult? mods = null)
        {
            AtsDataService.EnsureLoaded();

            var normalizedTrailer = Normalize(trailer);
            if (string.IsNullOrWhiteSpace(normalizedTrailer))
                return new List<string>();

            var allCargoes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var c in AtsDataService.Cargos)
            {
                if (!string.IsNullOrWhiteSpace(c))
                    allCargoes.Add(c);
            }

            if (mods != null)
            {
                foreach (var c in mods.Cargos)
                {
                    var clean = Friendly(c);
                    if (!string.IsNullOrWhiteSpace(clean))
                        allCargoes.Add(clean);
                }
            }

            return allCargoes
                .Where(c => AtsDataService.IsCargoTrailerCompatible(c, trailer))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<string> GetAvailableTrailers(AtsModScanResult? mods = null)
        {
            AtsDataService.EnsureLoaded();

            var trailers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var t in AtsDataService.Trailers)
            {
                if (!string.IsNullOrWhiteSpace(t))
                    trailers.Add(t);
            }

            if (mods != null)
            {
                foreach (var t in mods.Trailers)
                {
                    var clean = Friendly(t);
                    if (!string.IsNullOrWhiteSpace(clean))
                        trailers.Add(clean);
                }
            }

            return trailers
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static void GenerateAndAddToDispatch(AtsGenerateLoadOptions? options = null)
        {
            var job = GenerateOne(options);
            if (job == null)
                return;

            DispatchService.AddJob(job);
            AtsJobExportService.ExportPendingJob(job);
            AtsFreightMarketInjectorService.QueueSingleJob(job);
        }

        public static void GenerateBatchAndAddToDispatch(AtsGenerateLoadOptions? options = null)
        {
            var batch = GenerateBatch(options);
            foreach (var job in batch.Jobs)
            {
                DispatchService.AddJob(job);
                AtsJobExportService.ExportPendingJob(job);
                AtsFreightMarketInjectorService.QueueSingleJob(job);
            }
        }

        private static string PickTrailer(string? requestedTrailer, AtsModScanResult? mods)
        {
            if (!string.IsNullOrWhiteSpace(requestedTrailer) &&
                !requestedTrailer.Equals("Any", StringComparison.OrdinalIgnoreCase))
            {
                return requestedTrailer.Trim();
            }

            var trailers = GetAvailableTrailers(mods)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (trailers.Count == 0)
                return string.Empty;

            return PickRandom(trailers) ?? string.Empty;
        }

        private static OriginPick? PickOrigin(AtsGenerateLoadOptions options)
        {
            var candidateCities = AtsDataService.Cities
                .Where(c =>
                    (string.IsNullOrWhiteSpace(options.OriginState) || string.Equals(c.State, options.OriginState.Trim(), StringComparison.OrdinalIgnoreCase)) &&
                    (string.IsNullOrWhiteSpace(options.OriginCity) || string.Equals(c.City, options.OriginCity.Trim(), StringComparison.OrdinalIgnoreCase)) &&
                    c.Companies != null &&
                    c.Companies.Count > 0)
                .ToList();

            if (candidateCities.Count == 0)
                return null;

            var city = PickRandom(candidateCities);
            if (city == null)
                return null;

            var company = PickRandom(city.Companies);
            if (string.IsNullOrWhiteSpace(company))
                company = AtsDataService.Companies.FirstOrDefault() ?? "Wallbert";

            return new OriginPick
            {
                City = city.City,
                State = city.State,
                Company = company
            };
        }

        private static DestinationPick? PickDestination(AtsGenerateLoadOptions options, OriginPick origin)
        {
            var candidates = AtsDataService.Cities
                .Where(c =>
                    c.Companies != null &&
                    c.Companies.Count > 0 &&
                    !(string.Equals(c.City, origin.City, StringComparison.OrdinalIgnoreCase) &&
                      string.Equals(c.State, origin.State, StringComparison.OrdinalIgnoreCase)) &&
                    (string.IsNullOrWhiteSpace(options.DestinationState) || string.Equals(c.State, options.DestinationState.Trim(), StringComparison.OrdinalIgnoreCase)) &&
                    (string.IsNullOrWhiteSpace(options.DestinationCity) || string.Equals(c.City, options.DestinationCity.Trim(), StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (candidates.Count == 0)
                return null;

            var city = PickRandom(candidates);
            if (city == null)
                return null;

            var company = PickRandom(city.Companies);
            if (string.IsNullOrWhiteSpace(company))
                company = AtsDataService.Companies.FirstOrDefault() ?? "Wallbert";

            return new DestinationPick
            {
                City = city.City,
                State = city.State,
                Company = company
            };
        }

        private static int EstimateMilesFallback(OriginPick origin, DestinationPick destination)
        {
            var cityDiff = !string.Equals(origin.City, destination.City, StringComparison.OrdinalIgnoreCase);
            var stateDiff = !string.Equals(origin.State, destination.State, StringComparison.OrdinalIgnoreCase);

            if (stateDiff) return Rng.Next(450, 1400);
            if (cityDiff) return Rng.Next(90, 400);
            return Rng.Next(25, 80);
        }

        private static double EstimateWeightLbs(string cargo, string trailer)
        {
            var c = Normalize(cargo);
            var t = Normalize(trailer);

            if (ContainsAny(c, "bulldozer", "excavator", "transformer", "roller", "crane", "machinery"))
                return Rng.Next(28000, 98001);

            if (ContainsAny(c, "nitrogen", "oxygen", "argon", "lng", "propane", "cryogenic"))
                return Rng.Next(26000, 62001);

            if (ContainsAny(c, "fuel", "diesel", "gasoline", "milk", "chemical"))
                return Rng.Next(34000, 76001);

            if (ContainsAny(c, "steel", "beam", "pipe", "brick", "lumber"))
                return Rng.Next(18000, 56001);

            if (ContainsAny(c, "car", "vehicle"))
                return Rng.Next(9000, 42001);

            if (ContainsAny(t, "reefer"))
                return Rng.Next(12000, 44001);

            if (ContainsAny(t, "flatbed", "stepdeck", "lowboy", "low bed"))
                return Rng.Next(18000, 80001);

            return Rng.Next(8000, 46001);
        }

        private static decimal EstimatePayoutUsd(string cargo, string trailer, int miles, double weightLbs)
        {
            decimal baseRate = 2.10m;
            var c = Normalize(cargo);
            var t = Normalize(trailer);

            if (ContainsAny(c, "hazmat", "chemical", "fuel", "propane", "lng", "oxygen"))
                baseRate += 0.95m;

            if (ContainsAny(c, "machinery", "crane", "transformer", "excavator", "bulldozer"))
                baseRate += 1.10m;

            if (ContainsAny(t, "reefer"))
                baseRate += 0.35m;

            if (ContainsAny(t, "lowboy", "low bed", "stepdeck"))
                baseRate += 0.65m;

            if (weightLbs >= 45000)
                baseRate += 0.40m;

            var urgency = Rng.Next(0, 100);
            if (urgency >= 80)
                baseRate += 0.30m;

            return Math.Round((decimal)miles * baseRate, 2);
        }

        private static DateTime EstimateDeadline(DateTime pickupLocal, int miles, string cargo, string trailer)
        {
            var driveHours = Math.Max(4, miles / 50.0);
            var extraHours = 10.0;

            if (ContainsAny(Normalize(cargo), "machinery", "oversize", "transformer"))
                extraHours += 8.0;

            if (ContainsAny(Normalize(trailer), "reefer"))
                extraHours += 4.0;

            return pickupLocal.AddHours(driveHours + extraHours);
        }

        private static string BuildNotes(string cargo, string trailer, double weightLbs, int miles)
        {
            return $"{cargo} on {trailer} · {Math.Round(weightLbs):N0} lbs · {miles:N0} mi";
        }

        private static string Friendly(AtsResolvedCargoDef cargo)
        {
            return !string.IsNullOrWhiteSpace(cargo.DisplayName) ? cargo.DisplayName : cargo.Token;
        }

        private static string Friendly(AtsResolvedTrailerDef trailer)
        {
            return !string.IsNullOrWhiteSpace(trailer.DisplayName) ? trailer.DisplayName : trailer.Token;
        }

        private static string PickRandom(List<string> items)
        {
            if (items == null || items.Count == 0)
                return string.Empty;

            return items[Rng.Next(items.Count)];
        }

        private static T? PickRandom<T>(List<T> items) where T : class
        {
            if (items == null || items.Count == 0)
                return null;

            return items[Rng.Next(items.Count)];
        }

        private static bool ContainsAny(string input, params string[] values)
        {
            foreach (var v in values)
            {
                if (input.Contains(Normalize(v), StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string Normalize(string? value)
        {
            return (value ?? string.Empty)
                .Trim()
                .Replace(" ", "")
                .Replace("-", "")
                .Replace("_", "")
                .ToLowerInvariant();
        }

        private sealed class OriginPick
        {
            public string City { get; set; } = "";
            public string State { get; set; } = "";
            public string Company { get; set; } = "";
        }

        private sealed class DestinationPick
        {
            public string City { get; set; } = "";
            public string State { get; set; } = "";
            public string Company { get; set; } = "";
        }
    }
}