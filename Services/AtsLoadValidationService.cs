using System;
using System.Collections.Generic;
using System.Linq;

namespace OverWatchELD.Services
{
    public sealed class AtsLoadValidationResult
    {
        public bool IsValid { get; set; } = true;
        public List<string> Errors { get; set; } = new();
    }

    public static class AtsLoadValidationService
    {
        public static AtsLoadValidationResult Validate(
            string? cargo,
            string? company,
            string? trailer)
        {
            return ValidateInternal(cargo, company, trailer, null);
        }

        public static AtsLoadValidationResult Validate(
            string? cargo,
            string? company,
            string? trailer,
            AtsModScanResult? mods)
        {
            return ValidateInternal(cargo, company, trailer, mods);
        }

        public static bool TryValidate(
            string? cargo,
            string? company,
            string? trailer,
            out string errorMessage)
        {
            var result = Validate(cargo, company, trailer);
            errorMessage = result.IsValid
                ? string.Empty
                : string.Join(Environment.NewLine, result.Errors);

            return result.IsValid;
        }

        public static bool TryValidate(
            string? cargo,
            string? company,
            string? trailer,
            AtsModScanResult? mods,
            out string errorMessage)
        {
            var result = Validate(cargo, company, trailer, mods);
            errorMessage = result.IsValid
                ? string.Empty
                : string.Join(Environment.NewLine, result.Errors);

            return result.IsValid;
        }

        private static AtsLoadValidationResult ValidateInternal(
            string? cargo,
            string? company,
            string? trailer,
            AtsModScanResult? mods)
        {
            var result = new AtsLoadValidationResult();

            cargo = Normalize(cargo);
            company = Normalize(company);
            trailer = Normalize(trailer);

            if (string.IsNullOrWhiteSpace(cargo))
                result.Errors.Add("Cargo is required.");

            if (string.IsNullOrWhiteSpace(company))
                result.Errors.Add("Company is required.");

            if (string.IsNullOrWhiteSpace(trailer))
                result.Errors.Add("Trailer is required.");

            if (result.Errors.Count > 0)
            {
                result.IsValid = false;
                return result;
            }

            if (mods != null)
            {
                if (mods.Cargos.Count == 0 && mods.Companies.Count == 0 && mods.Trailers.Count == 0)
                {
                    result.Errors.Add("No ATS mod definitions were found in the Documents mod folder.");
                    result.IsValid = false;
                    return result;
                }

                if (mods.Cargos.Count > 0 && !ContainsNormalized(mods.Cargos, cargo))
                    result.Errors.Add($"Cargo '{cargo}' was not found in scanned ATS mods.");

                if (!string.Equals(company, "Any", StringComparison.OrdinalIgnoreCase) &&
                    mods.Companies.Count > 0 &&
                    !ContainsNormalized(mods.Companies, company))
                {
                    result.Errors.Add($"Company '{company}' was not found in scanned ATS mods.");
                }

                if (mods.Trailers.Count > 0 && !ContainsNormalized(mods.Trailers, trailer))
                    result.Errors.Add($"Trailer '{trailer}' was not found in scanned ATS mods.");

                var cargoDef = FindNormalized(mods.Cargos, cargo);
                var trailerDef = FindNormalized(mods.Trailers, trailer);
                var companyDef = FindNormalized(mods.Companies, company);

                if (cargoDef != null && !cargoDef.IsActiveInProfile)
                    result.Errors.Add($"Cargo '{cargo}' comes from an installed mod that is not active in the current ATS profile. {cargoDef.ActiveProfileNote}");

                if (trailerDef != null && !trailerDef.IsActiveInProfile)
                    result.Errors.Add($"Trailer '{trailer}' comes from an installed mod that is not active in the current ATS profile. {trailerDef.ActiveProfileNote}");

                if (companyDef != null && !companyDef.IsActiveInProfile &&
                    !string.Equals(company, "Any", StringComparison.OrdinalIgnoreCase))
                {
                    result.Errors.Add($"Company '{company}' comes from an installed mod that is not active in the current ATS profile. {companyDef.ActiveProfileNote}");
                }
            }
            else
            {
                AtsDataService.EnsureLoaded();

                if (AtsDataService.Cargos.Count > 0 &&
                    !AtsDataService.Cargos.Any(x => string.Equals(Normalize(x), cargo, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Errors.Add($"Unknown cargo '{cargo}'.");
                }

                if (AtsDataService.Companies.Count > 0 &&
                    !string.Equals(company, "Any", StringComparison.OrdinalIgnoreCase) &&
                    !AtsDataService.Companies.Any(x => string.Equals(Normalize(x), company, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Errors.Add($"Unknown company '{company}'.");
                }

                if (AtsDataService.Trailers.Count > 0 &&
                    !AtsDataService.Trailers.Any(x => string.Equals(Normalize(x), trailer, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Errors.Add($"Unknown trailer '{trailer}'.");
                }
            }

            if (result.Errors.Count == 0)
            {
                var modCompatible = mods != null
                    ? AtsModScannerService.IsTrailerAllowedForCargo(cargo, trailer)
                    : true;

                if (!modCompatible)
                {
                    result.Errors.Add($"Cargo '{cargo}' is not compatible with trailer '{trailer}' based on scanned ATS mod definitions.");
                }
                else if (!AtsDataService.IsCargoTrailerCompatible(cargo, trailer))
                {
                    var allowed = AtsDataService.GetAllowedTrailers(cargo);

                    if (allowed.Count == 0)
                    {
                        result.Errors.Add(
                            $"Cargo '{cargo}' has no valid trailer mapping, so the load cannot be created.");
                    }
                    else
                    {
                        var allowedNames = allowed
                            .Select(GetFriendly)
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        result.Errors.Add(
                            $"Cargo '{cargo}' is not compatible with trailer '{trailer}'. " +
                            $"Allowed trailers: {string.Join(", ", allowedNames)}.");
                    }
                }
            }

            result.IsValid = result.Errors.Count == 0;
            return result;
        }

        private static AtsResolvedCargoDef? FindNormalized(IEnumerable<AtsResolvedCargoDef> source, string target)
        {
            var normalized = Normalize(target);
            return source.FirstOrDefault(x =>
                Normalize(x.DisplayName) == normalized ||
                Normalize(x.Token) == normalized);
        }

        private static AtsResolvedCompanyDef? FindNormalized(IEnumerable<AtsResolvedCompanyDef> source, string target)
        {
            var normalized = Normalize(target);
            return source.FirstOrDefault(x =>
                Normalize(x.DisplayName) == normalized ||
                Normalize(x.Token) == normalized);
        }

        private static AtsResolvedTrailerDef? FindNormalized(IEnumerable<AtsResolvedTrailerDef> source, string target)
        {
            var normalized = Normalize(target);
            return source.FirstOrDefault(x =>
                Normalize(x.DisplayName) == normalized ||
                Normalize(x.Token) == normalized);
        }

        private static bool ContainsNormalized(IEnumerable<AtsResolvedCargoDef> source, string target)
        {
            var normalized = Normalize(target);
            return source.Any(x =>
                Normalize(x.DisplayName) == normalized ||
                Normalize(x.Token) == normalized);
        }

        private static bool ContainsNormalized(IEnumerable<AtsResolvedCompanyDef> source, string target)
        {
            var normalized = Normalize(target);
            return source.Any(x =>
                Normalize(x.DisplayName) == normalized ||
                Normalize(x.Token) == normalized);
        }

        private static bool ContainsNormalized(IEnumerable<AtsResolvedTrailerDef> source, string target)
        {
            var normalized = Normalize(target);
            return source.Any(x =>
                Normalize(x.DisplayName) == normalized ||
                Normalize(x.Token) == normalized);
        }

        private static string GetFriendly(object? item)
        {
            return item switch
            {
                null => string.Empty,
                string s => s,
                AtsResolvedCargoDef c => !string.IsNullOrWhiteSpace(c.DisplayName) ? c.DisplayName : c.Token,
                AtsResolvedCompanyDef c => !string.IsNullOrWhiteSpace(c.DisplayName) ? c.DisplayName : c.Token,
                AtsResolvedTrailerDef t => !string.IsNullOrWhiteSpace(t.DisplayName) ? t.DisplayName : t.Token,
                _ => item.ToString() ?? string.Empty
            };
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
    }
}