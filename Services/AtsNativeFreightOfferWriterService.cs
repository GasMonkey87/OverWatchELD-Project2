using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace OverWatchELD.Services
{
    public sealed class AtsNativeFreightOfferWriteResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string UpdatedSaveText { get; set; } = "";
        public string OfferUnitId { get; set; } = "";
        public List<string> Warnings { get; set; } = new();
    }

    public static class AtsNativeFreightOfferWriterService
    {
        public static AtsNativeFreightOfferWriteResult TryAddNativeFreightOffer(
            string? saveText,
            AtsSaveFreightJob? job)
        {
            var result = new AtsNativeFreightOfferWriteResult();

            if (string.IsNullOrWhiteSpace(saveText))
            {
                result.Success = false;
                result.Message = "Save text is empty.";
                return result;
            }

            if (job == null)
            {
                result.Success = false;
                result.Message = "Mapped ATS save job is null.";
                return result;
            }

            var normalized = NormalizeNewlines(saveText);

            if (!normalized.Contains("SiiNunit", StringComparison.OrdinalIgnoreCase))
            {
                result.Success = false;
                result.Message = "Save text does not look like a readable text SII save.";
                return result;
            }

            var inspection = AtsSaveStructureInspectorService.InspectSaveText(normalized);
            if (!inspection.Success)
            {
                result.Success = false;
                result.Message = inspection.Message;
                return result;
            }

            var offerListUnit = FindLikelyOfferListUnit(normalized);
            if (string.IsNullOrWhiteSpace(offerListUnit))
            {
                result.Success = false;
                result.Message = "No likely native ATS freight/economy offer list was found in game.sii.";
                result.Warnings.Add("The save stayed untouched because no confident native offer list match was found.");
                return result;
            }

            if (inspection.BestOfferListUnit == null && inspection.BestEconomyUnit == null)
            {
                result.Warnings.Add("No strong ATS offer-list candidate was detected; native write is using the best available fallback.");
            }

            var offerUnitId = BuildOfferUnitId();
            var offerBlock = BuildNativeOfferBlock(offerUnitId, offerListUnit, job);

            if (string.IsNullOrWhiteSpace(offerBlock))
            {
                result.Success = false;
                result.Message = "Failed to build native ATS freight offer block.";
                return result;
            }

            if (normalized.Contains(offerUnitId, StringComparison.OrdinalIgnoreCase))
            {
                result.Success = false;
                result.Message = "Generated offer unit ID already exists in save. Try again.";
                return result;
            }

            var updated = AppendUnitBlock(normalized, offerBlock);
            updated = TryAddOfferReference(updated, offerListUnit, offerUnitId, result.Warnings);

            result.Success = true;
            result.Message = $"Native-style freight offer block added using offer list '{offerListUnit}'.";
            result.UpdatedSaveText = updated;
            result.OfferUnitId = offerUnitId;
            return result;
        }

        private static string FindLikelyOfferListUnit(string saveText)
        {
            var inspection = AtsSaveStructureInspectorService.InspectSaveText(saveText);
            if (!inspection.Success)
                return "";

            return inspection.BestOfferListUnit?.UnitId
                ?? inspection.BestEconomyUnit?.UnitId
                ?? "";
        }

        private static string BuildOfferUnitId()
        {
            return $"_nameless.overwatcheld.native.offer.{Guid.NewGuid():N}";
        }

        private static string BuildNativeOfferBlock(string offerUnitId, string offerListUnit, AtsSaveFreightJob job)
        {
            var nowUnix = ToUnixSeconds(DateTime.UtcNow);
            var expUnix = ToUnixSeconds(job.ExpirationUtc);
            var deadlineUnix = ToUnixSeconds(job.DeadlineUtc);

            var sb = new StringBuilder();

            sb.AppendLine($"job_offer_data : {offerUnitId}");
            sb.AppendLine("{");
            sb.AppendLine($" offer_list: {offerListUnit}");
            sb.AppendLine($" cargo: \"{Escape(job.CargoToken)}\"");
            sb.AppendLine($" trailer_variant: \"{Escape(job.TrailerToken)}\"");
            sb.AppendLine($" source_company: \"{Escape(job.SourceCompanyToken)}\"");
            sb.AppendLine($" destination_company: \"{Escape(job.DestinationCompanyToken)}\"");
            sb.AppendLine($" source_city: \"{Escape(job.SourceCityToken)}\"");
            sb.AppendLine($" destination_city: \"{Escape(job.DestinationCityToken)}\"");
            sb.AppendLine($" revenue: {job.Income}");
            sb.AppendLine($" distance: {job.DistanceMiles}");
            sb.AppendLine($" weight: {job.WeightLbs}");
            sb.AppendLine($" created_time: {nowUnix}");
            sb.AppendLine($" expiration_time: {expUnix}");
            sb.AppendLine($" deadline_time: {deadlineUnix}");
            sb.AppendLine($" is_urgent: {(job.IsHazmatLike || job.IsOversizeLike ? "true" : "false")}");
            sb.AppendLine($" display_cargo: \"{Escape(job.DisplayCargo)}\"");
            sb.AppendLine($" display_trailer: \"{Escape(job.DisplayTrailer)}\"");
            sb.AppendLine($" display_source_company: \"{Escape(job.DisplaySourceCompany)}\"");
            sb.AppendLine($" display_destination_company: \"{Escape(job.DisplayDestinationCompany)}\"");
            sb.AppendLine($" display_source_city: \"{Escape(job.DisplaySourceCity)}\"");
            sb.AppendLine($" display_destination_city: \"{Escape(job.DisplayDestinationCity)}\"");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private static string TryAddOfferReference(
            string saveText,
            string offerListUnit,
            string offerUnitId,
            List<string> warnings)
        {
            var pattern = $@"(?is)([a-z0-9\._]+\s*:\s*{Regex.Escape(offerListUnit)}\s*\{{)(.*?)(\n\}})";
            var match = Regex.Match(saveText, pattern);

            if (!match.Success)
            {
                warnings.Add("Offer block was appended, but the native list block could not be reopened to add a reference.");
                return saveText;
            }

            var header = match.Groups[1].Value;
            var body = match.Groups[2].Value;
            var footer = match.Groups[3].Value;

            if (body.Contains(offerUnitId, StringComparison.OrdinalIgnoreCase))
                return saveText;

            var existingIndexes = Regex.Matches(body, @"(?im)^\s*(offer|job_offer|job)\[(\d+)\]\s*:")
                .Cast<Match>()
                .Select(m => int.TryParse(m.Groups[2].Value, out var idx) ? idx : -1)
                .Where(i => i >= 0)
                .ToList();

            var nextIndex = existingIndexes.Count == 0 ? 0 : existingIndexes.Max() + 1;

            var insertLine = $"\n job_offer[{nextIndex}]: {offerUnitId}";
            var newBody = body + insertLine;

            var replacement = header + newBody + footer;
            return saveText.Remove(match.Index, match.Length).Insert(match.Index, replacement);
        }

        private static string AppendUnitBlock(string saveText, string block)
        {
            var endMarker = "\n}";
            var idx = saveText.LastIndexOf(endMarker, StringComparison.Ordinal);

            if (idx >= 0)
                return saveText.Insert(idx, "\n\n" + block + "\n");

            return saveText.TrimEnd() + "\n\n" + block + "\n";
        }

        private static long ToUnixSeconds(DateTime valueUtc)
        {
            var utc = valueUtc.Kind == DateTimeKind.Utc ? valueUtc : valueUtc.ToUniversalTime();
            return new DateTimeOffset(utc).ToUnixTimeSeconds();
        }

        private static string NormalizeNewlines(string text)
        {
            return (text ?? "").Replace("\r\n", "\n");
        }

        private static string Escape(string value)
        {
            return (value ?? "")
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);
        }
    }
}