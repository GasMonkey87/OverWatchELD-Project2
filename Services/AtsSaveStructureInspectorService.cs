using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OverWatchELD.Services.ATS;

namespace OverWatchELD.Services
{
    public sealed class AtsSaveStructureInspectionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool LooksLikeReadableSii { get; set; }

        public List<AtsSiiUnitInfo> Units { get; set; } = new();
        public List<AtsSiiUnitInfo> LikelyEconomyUnits { get; set; } = new();
        public List<AtsSiiUnitInfo> LikelyOfferUnits { get; set; } = new();
        public List<AtsSiiUnitInfo> LikelyCompanyUnits { get; set; } = new();

        public AtsSiiUnitInfo? BestEconomyUnit { get; set; }
        public AtsSiiUnitInfo? BestOfferListUnit { get; set; }
    }

    public sealed class AtsSiiUnitInfo
    {
        public string UnitType { get; set; } = "";
        public string UnitId { get; set; } = "";
        public string Body { get; set; } = "";

        public bool HasOfferRefs { get; set; }
        public bool HasJobRefs { get; set; }
        public bool HasCompanyRefs { get; set; }
        public bool HasCityRefs { get; set; }

        public int OfferRefCount { get; set; }
        public int JobRefCount { get; set; }
        public int CompanyRefCount { get; set; }
        public int CityRefCount { get; set; }

        public override string ToString() => $"{UnitType} : {UnitId}";
    }

    public static class AtsSaveStructureInspectorService
    {
        public static AtsSaveStructureInspectionResult InspectLatestSave()
        {
            var located = AtsSaveGameLocatorService.LocateLatestSave();
            if (!located.Success || string.IsNullOrWhiteSpace(located.GameSiiPath))
            {
                return new AtsSaveStructureInspectionResult
                {
                    Success = false,
                    Message = located.Message
                };
            }

            return InspectSavePath(located.GameSiiPath);
        }

        public static AtsSaveStructureInspectionResult InspectSavePath(string? gameSiiPath)
        {
            if (string.IsNullOrWhiteSpace(gameSiiPath))
            {
                return new AtsSaveStructureInspectionResult
                {
                    Success = false,
                    Message = "game.sii path is missing."
                };
            }

            var text = AtsSaveGameReadWriteService.ReadSave(gameSiiPath);
            if (string.IsNullOrWhiteSpace(text))
            {
                return new AtsSaveStructureInspectionResult
                {
                    Success = false,
                    Message = "Failed to read game.sii."
                };
            }

            return InspectSaveText(text);
        }

        public static AtsSaveStructureInspectionResult InspectSaveText(string? saveText)
        {
            var result = new AtsSaveStructureInspectionResult();

            if (string.IsNullOrWhiteSpace(saveText))
            {
                result.Success = false;
                result.Message = "Save text is empty.";
                return result;
            }

            var normalized = saveText.Replace("\r\n", "\n");
            result.LooksLikeReadableSii = normalized.Contains("SiiNunit", StringComparison.OrdinalIgnoreCase);

            if (!result.LooksLikeReadableSii)
            {
                result.Success = false;
                result.Message = "Save does not look like readable text SII.";
                return result;
            }

            var units = ParseUnits(normalized);
            result.Units = units;

            result.LikelyEconomyUnits = units
                .Where(IsLikelyEconomyUnit)
                .OrderByDescending(ScoreEconomyUnit)
                .ToList();

            result.LikelyOfferUnits = units
                .Where(IsLikelyOfferListUnit)
                .OrderByDescending(ScoreOfferUnit)
                .ToList();

            result.LikelyCompanyUnits = units
                .Where(IsLikelyCompanyUnit)
                .OrderBy(u => u.UnitType, StringComparer.OrdinalIgnoreCase)
                .ThenBy(u => u.UnitId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            result.BestEconomyUnit = result.LikelyEconomyUnits.FirstOrDefault();
            result.BestOfferListUnit = result.LikelyOfferUnits.FirstOrDefault()
                ?? result.BestEconomyUnit;

            result.Success = true;
            result.Message = BuildSummaryMessage(result);
            return result;
        }

        public static string GetLatestSaveDebugSummary()
        {
            var inspection = InspectLatestSave();
            if (!inspection.Success)
                return inspection.Message;

            var bestEconomy = inspection.BestEconomyUnit?.ToString() ?? "(none)";
            var bestOffer = inspection.BestOfferListUnit?.ToString() ?? "(none)";

            return $"Units: {inspection.Units.Count}{Environment.NewLine}" +
                   $"Best economy unit: {bestEconomy}{Environment.NewLine}" +
                   $"Best offer unit: {bestOffer}{Environment.NewLine}" +
                   $"Likely offer units: {inspection.LikelyOfferUnits.Count}";
        }

        private static List<AtsSiiUnitInfo> ParseUnits(string text)
        {
            var results = new List<AtsSiiUnitInfo>();

            var matches = Regex.Matches(
                text,
                @"(?ms)^\s*([a-zA-Z0-9\._]+)\s*:\s*([a-zA-Z0-9\._]+)\s*\{(.*?)^\}",
                RegexOptions.Multiline);

            foreach (Match match in matches)
            {
                var unitType = match.Groups[1].Value.Trim();
                var unitId = match.Groups[2].Value.Trim();
                var body = match.Groups[3].Value;

                if (string.IsNullOrWhiteSpace(unitType) || string.IsNullOrWhiteSpace(unitId))
                    continue;

                var info = new AtsSiiUnitInfo
                {
                    UnitType = unitType,
                    UnitId = unitId,
                    Body = body,
                    OfferRefCount = CountMatches(body, @"(?im)\b(offer|job_offer)\[?\d*\]?\s*:"),
                    JobRefCount = CountMatches(body, @"(?im)\bjob\[?\d*\]?\s*:"),
                    CompanyRefCount = CountMatches(body, @"(?im)\b(company|source_company|target_company|destination_company)\b"),
                    CityRefCount = CountMatches(body, @"(?im)\b(city|source_city|target_city|destination_city)\b")
                };

                info.HasOfferRefs = info.OfferRefCount > 0;
                info.HasJobRefs = info.JobRefCount > 0;
                info.HasCompanyRefs = info.CompanyRefCount > 0;
                info.HasCityRefs = info.CityRefCount > 0;

                results.Add(info);
            }

            return results;
        }

        private static bool IsLikelyEconomyUnit(AtsSiiUnitInfo u)
        {
            var combined = $"{u.UnitType} {u.UnitId} {u.Body}";
            return ContainsAny(combined,
                "economy",
                "freight_market",
                "job_offer",
                "freight_offers",
                "company_offer");
        }

        private static bool IsLikelyOfferListUnit(AtsSiiUnitInfo u)
        {
            var combined = $"{u.UnitType} {u.UnitId}";
            if (ContainsAny(combined,
                "freight_market",
                "job_offer",
                "freight_offers",
                "company_offer"))
                return true;

            return u.HasOfferRefs || u.HasJobRefs;
        }

        private static bool IsLikelyCompanyUnit(AtsSiiUnitInfo u)
        {
            var combined = $"{u.UnitType} {u.UnitId}";
            return ContainsAny(combined, "company", "prefab", "garage")
                   && (u.HasCityRefs || u.HasCompanyRefs || u.Body.Contains("city", StringComparison.OrdinalIgnoreCase));
        }

        private static int ScoreEconomyUnit(AtsSiiUnitInfo u)
        {
            var score = 0;
            var combined = $"{u.UnitType} {u.UnitId}";

            if (ContainsAny(combined, "economy")) score += 8;
            if (ContainsAny(combined, "freight_market")) score += 10;
            if (ContainsAny(combined, "job_offer")) score += 10;
            if (u.HasOfferRefs) score += 10;
            if (u.HasJobRefs) score += 8;
            if (u.HasCompanyRefs) score += 3;
            if (u.HasCityRefs) score += 2;

            score += Math.Min(u.OfferRefCount, 20);
            score += Math.Min(u.JobRefCount, 20);

            return score;
        }

        private static int ScoreOfferUnit(AtsSiiUnitInfo u)
        {
            var score = 0;
            var combined = $"{u.UnitType} {u.UnitId}";

            if (ContainsAny(combined, "freight_market")) score += 15;
            if (ContainsAny(combined, "job_offer")) score += 12;
            if (ContainsAny(combined, "company_offer")) score += 10;
            if (u.HasOfferRefs) score += 12;
            if (u.HasJobRefs) score += 10;

            score += Math.Min(u.OfferRefCount, 25);
            score += Math.Min(u.JobRefCount, 25);

            return score;
        }

        private static int CountMatches(string text, string pattern)
        {
            return Regex.Matches(text ?? "", pattern).Count;
        }

        private static bool ContainsAny(string text, params string[] needles)
        {
            foreach (var needle in needles)
            {
                if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string BuildSummaryMessage(AtsSaveStructureInspectionResult result)
        {
            var bestEconomy = result.BestEconomyUnit?.ToString() ?? "(none)";
            var bestOffer = result.BestOfferListUnit?.ToString() ?? "(none)";

            return $"Parsed {result.Units.Count} SII units. " +
                   $"Best economy candidate: {bestEconomy}. " +
                   $"Best offer-list candidate: {bestOffer}.";
        }
    }
}