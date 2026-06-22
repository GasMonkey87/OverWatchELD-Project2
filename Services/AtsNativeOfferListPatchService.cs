using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OverWatchELD.Services
{
    public sealed class AtsNativeOfferListPatchResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string UpdatedSaveText { get; set; } = "";
        public string ReferenceKeyUsed { get; set; } = "";
        public int AddedIndex { get; set; } = -1;
        public List<string> Warnings { get; set; } = new();
    }

    public static class AtsNativeOfferListPatchService
    {
        public static AtsNativeOfferListPatchResult AddOfferReference(
            string? saveText,
            string? offerListUnitId,
            string? offerUnitId)
        {
            var result = new AtsNativeOfferListPatchResult();

            if (string.IsNullOrWhiteSpace(saveText))
            {
                result.Success = false;
                result.Message = "Save text is empty.";
                return result;
            }

            if (string.IsNullOrWhiteSpace(offerListUnitId))
            {
                result.Success = false;
                result.Message = "Offer list unit ID is empty.";
                return result;
            }

            if (string.IsNullOrWhiteSpace(offerUnitId))
            {
                result.Success = false;
                result.Message = "Offer unit ID is empty.";
                return result;
            }

            var normalized = saveText.Replace("\r\n", "\n");

            var blockMatch = Regex.Match(
                normalized,
                $@"(?is)([a-z0-9\._]+\s*:\s*{Regex.Escape(offerListUnitId)}\s*\{{)(.*?)(\n\}})");

            if (!blockMatch.Success)
            {
                result.Success = false;
                result.Message = $"Could not reopen native offer list block '{offerListUnitId}'.";
                return result;
            }

            var header = blockMatch.Groups[1].Value;
            var body = blockMatch.Groups[2].Value;
            var footer = blockMatch.Groups[3].Value;

            if (body.Contains(offerUnitId, StringComparison.OrdinalIgnoreCase))
            {
                result.Success = true;
                result.Message = "Offer reference already exists in native list.";
                result.UpdatedSaveText = normalized;
                result.ReferenceKeyUsed = DetectReferenceKey(body);
                result.AddedIndex = DetectNextIndex(body) - 1;
                return result;
            }

            var referenceKey = DetectReferenceKey(body);
            var nextIndex = DetectNextIndex(body);

            var insertLine = $"\n {referenceKey}[{nextIndex}]: {offerUnitId}";
            var newBody = body + insertLine;

            var replacement = header + newBody + footer;
            var updated = normalized.Remove(blockMatch.Index, blockMatch.Length)
                                    .Insert(blockMatch.Index, replacement);

            result.Success = true;
            result.Message = $"Native offer reference added using '{referenceKey}[{nextIndex}]'.";
            result.UpdatedSaveText = updated;
            result.ReferenceKeyUsed = referenceKey;
            result.AddedIndex = nextIndex;

            if (referenceKey == "job_offer")
                result.Warnings.Add("Reference key defaulted to 'job_offer'. If your ATS save uses a different native list key, inspect the target block.");

            return result;
        }

        public static string DetectReferenceKey(string? body)
        {
            var text = body ?? "";

            var offerMatches = Regex.Matches(text, @"(?im)^\s*offer\[(\d+)\]\s*:");
            var jobOfferMatches = Regex.Matches(text, @"(?im)^\s*job_offer\[(\d+)\]\s*:");
            var jobMatches = Regex.Matches(text, @"(?im)^\s*job\[(\d+)\]\s*:");

            if (offerMatches.Count > 0 && offerMatches.Count >= jobOfferMatches.Count && offerMatches.Count >= jobMatches.Count)
                return "offer";

            if (jobMatches.Count > 0 && jobMatches.Count >= jobOfferMatches.Count)
                return "job";

            return "job_offer";
        }

        public static int DetectNextIndex(string? body)
        {
            var text = body ?? "";

            var matches = Regex.Matches(
                text,
                @"(?im)^\s*(offer|job_offer|job)\[(\d+)\]\s*:")
                .Cast<Match>()
                .Select(m => int.TryParse(m.Groups[2].Value, out var idx) ? idx : -1)
                .Where(i => i >= 0)
                .ToList();

            if (matches.Count == 0)
                return 0;

            return matches.Max() + 1;
        }
    }
}