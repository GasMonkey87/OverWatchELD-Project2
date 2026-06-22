using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace OverWatchELD.Services
{
    public static class AtsSaveLookupService
    {
        private static readonly object Gate = new();
        private static AtsSaveLookupCache? _cache;
        private static string _cacheKey = "";

        private static readonly Regex CompanyRegex =
            new Regex(@"(?im)^\s*companies\[\d+\]\s*:\s*(company\.volatile\.[^\s]+)\s*$",
                RegexOptions.Compiled);

        public static AtsSaveLookupCache LoadFromGameSii(string gameSiiPath)
        {
            lock (Gate)
            {
                try
                {
                    var key = BuildCacheKey(gameSiiPath);
                    if (_cache != null && string.Equals(_cacheKey, key, StringComparison.Ordinal))
                        return _cache;

                    var cache = BuildCache(gameSiiPath);
                    _cache = cache;
                    _cacheKey = key;
                    return cache;
                }
                catch (Exception ex)
                {
                    return new AtsSaveLookupCache
                    {
                        GameSiiPath = gameSiiPath ?? "",
                        Warnings = new List<string> { "ATS save lookup failed: " + ex.Message }
                    };
                }
            }
        }

        public static AtsCompanyUnitMatch? ResolveBestCompany(
            string gameSiiPath,
            string? requestedCompanyName,
            string? requestedCityName,
            string? requestedStateName = null)
        {
            var cache = LoadFromGameSii(gameSiiPath);

            var cityKey = NormalizeToken(requestedCityName);
            var companyKey = NormalizeToken(requestedCompanyName);

            var candidates = cache.Companies.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(cityKey))
            {
                var cityMatches = candidates
                    .Where(x => string.Equals(x.CityTokenNormalized, cityKey, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (cityMatches.Count > 0)
                    candidates = cityMatches;
            }

            if (!string.IsNullOrWhiteSpace(companyKey))
            {
                var ranked = candidates
                    .Select(x => new
                    {
                        Company = x,
                        Score = ScoreCompany(x, companyKey)
                    })
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => x.Company.UnitId, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var best = ranked.FirstOrDefault();
                if (best != null && best.Score > 0)
                    return best.Company;
            }

            return candidates
                .OrderBy(x => x.UnitId, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        public static string ResolveCityToken(string? cityName)
        {
            return NormalizeToken(cityName);
        }

        public static void Invalidate()
        {
            lock (Gate)
            {
                _cache = null;
                _cacheKey = "";
            }
        }

        private static AtsSaveLookupCache BuildCache(string gameSiiPath)
        {
            var result = new AtsSaveLookupCache
            {
                GameSiiPath = gameSiiPath ?? ""
            };

            if (string.IsNullOrWhiteSpace(gameSiiPath) || !File.Exists(gameSiiPath))
            {
                result.Warnings.Add("game.sii path is missing or invalid.");
                return result;
            }

            var text = File.ReadAllText(gameSiiPath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(text) || !text.Contains("SiiNunit", StringComparison.OrdinalIgnoreCase))
            {
                result.Warnings.Add("game.sii is empty or not decrypted.");
                return result;
            }

            foreach (Match match in CompanyRegex.Matches(text))
            {
                if (!match.Success)
                    continue;

                var unitId = (match.Groups[1].Value ?? "").Trim();
                if (string.IsNullOrWhiteSpace(unitId))
                    continue;

                var parsed = ParseCompanyUnit(unitId);
                result.Companies.Add(parsed);
            }

            result.Companies = result.Companies
                .GroupBy(x => x.UnitId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(x => x.UnitId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (result.Companies.Count == 0)
                result.Warnings.Add("No company.volatile units were found in game.sii.");

            return result;
        }

        private static AtsCompanyUnitMatch ParseCompanyUnit(string unitId)
        {
            var parts = unitId.Split('.', StringSplitOptions.RemoveEmptyEntries);

            string companyToken = "";
            string cityToken = "";

            if (parts.Length >= 2)
            {
                cityToken = parts[^1];
                companyToken = parts[^2];
            }

            return new AtsCompanyUnitMatch
            {
                UnitId = unitId,
                CompanyToken = companyToken,
                CityToken = cityToken,
                CompanyTokenNormalized = NormalizeToken(companyToken),
                CityTokenNormalized = NormalizeToken(cityToken)
            };
        }

        private static int ScoreCompany(AtsCompanyUnitMatch company, string requestedCompanyKey)
        {
            if (string.IsNullOrWhiteSpace(requestedCompanyKey))
                return 0;

            var token = company.CompanyTokenNormalized ?? "";
            if (token.Length == 0)
                return 0;

            if (string.Equals(token, requestedCompanyKey, StringComparison.OrdinalIgnoreCase))
                return 1000;

            if (token.Contains(requestedCompanyKey, StringComparison.OrdinalIgnoreCase))
                return 800;

            if (requestedCompanyKey.Contains(token, StringComparison.OrdinalIgnoreCase))
                return 700;

            var requestedWords = SplitWords(requestedCompanyKey);
            var tokenWords = SplitWords(token);

            var overlap = requestedWords.Intersect(tokenWords, StringComparer.OrdinalIgnoreCase).Count();
            if (overlap > 0)
                return 100 + (overlap * 25);

            // Friendly-name shortcuts for common ATS companies
            foreach (var alias in GetCompanyAliases(requestedCompanyKey))
            {
                if (token.Contains(alias, StringComparison.OrdinalIgnoreCase))
                    return 600;
            }

            return 0;
        }

        private static IEnumerable<string> GetCompanyAliases(string requestedCompanyKey)
        {
            if (requestedCompanyKey.Contains("wallbert")) { yield return "wal"; yield return "mkt"; }
            if (requestedCompanyKey.Contains("walmart")) { yield return "wal"; yield return "mkt"; }

            if (requestedCompanyKey.Contains("bitumen")) { yield return "bit"; yield return "rd"; }
            if (requestedCompanyKey.Contains("road")) { yield return "rd"; }

            if (requestedCompanyKey.Contains("gallon")) { yield return "gal"; }
            if (requestedCompanyKey.Contains("oil")) { yield return "oil"; }

            if (requestedCompanyKey.Contains("sellgoods")) { yield return "sg"; yield return "whs"; }
            if (requestedCompanyKey.Contains("voltison")) { yield return "vp"; yield return "epw"; }

            if (requestedCompanyKey.Contains("home")) { yield return "hom"; }
            if (requestedCompanyKey.Contains("farm")) { yield return "farm"; }
            if (requestedCompanyKey.Contains("market")) { yield return "mkt"; }
            if (requestedCompanyKey.Contains("warehouse")) { yield return "whs"; }
            if (requestedCompanyKey.Contains("construction")) { yield return "con"; }
            if (requestedCompanyKey.Contains("service")) { yield return "svc"; }
        }

        private static IEnumerable<string> SplitWords(string value)
        {
            return (value ?? "")
                .Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0);
        }

        private static string NormalizeToken(string? value)
        {
            return (value ?? "")
                .Trim()
                .ToLowerInvariant()
                .Replace(" ", "_")
                .Replace("-", "_");
        }

        private static string BuildCacheKey(string gameSiiPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(gameSiiPath) || !File.Exists(gameSiiPath))
                    return gameSiiPath ?? "";

                var fi = new FileInfo(gameSiiPath);
                return $"{fi.FullName}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
            }
            catch
            {
                return gameSiiPath ?? "";
            }
        }
    }

    public sealed class AtsSaveLookupCache
    {
        public string GameSiiPath { get; set; } = "";
        public List<AtsCompanyUnitMatch> Companies { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    public sealed class AtsCompanyUnitMatch
    {
        public string UnitId { get; set; } = "";
        public string CompanyToken { get; set; } = "";
        public string CityToken { get; set; } = "";
        public string CompanyTokenNormalized { get; set; } = "";
        public string CityTokenNormalized { get; set; } = "";
    }
}