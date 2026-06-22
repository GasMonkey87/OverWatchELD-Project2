using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OverWatchELD.Services
{
    public sealed class AtsEconomyOfferWriteResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string UpdatedSaveText { get; set; } = "";
        public List<string> Warnings { get; set; } = new();
    }

    public static class AtsSaveEconomyOfferWriterService
    {
        private const string StartMarker = "// OVERWATCH_ELD_OFFERS_BEGIN";
        private const string EndMarker = "// OVERWATCH_ELD_OFFERS_END";
        private const string RootUnitId = "_nameless.overwatcheld.offers.root";

        public static AtsEconomyOfferWriteResult AddInjectedJobReference(string? saveText, string? injectedUnitId)
        {
            var result = new AtsEconomyOfferWriteResult();

            if (string.IsNullOrWhiteSpace(saveText))
            {
                result.Success = false;
                result.Message = "Save text is empty.";
                return result;
            }

            if (string.IsNullOrWhiteSpace(injectedUnitId))
            {
                result.Success = false;
                result.Message = "Injected unit ID is empty.";
                return result;
            }

            var normalized = NormalizeNewlines(saveText);
            var refs = ReadExistingRefs(normalized);

            if (!refs.Contains(injectedUnitId, StringComparer.OrdinalIgnoreCase))
                refs.Add(injectedUnitId);

            var block = BuildOffersBlock(refs);

            if (HasManagedOffersSection(normalized))
            {
                normalized = ReplaceManagedOffersSection(normalized, block);
            }
            else
            {
                normalized = AppendManagedOffersSection(normalized, block);
                result.Warnings.Add("Managed OverWatch offers section was created in game.sii.");
            }

            result.Success = true;
            result.Message = "Injected job reference added to managed offers section.";
            result.UpdatedSaveText = normalized;
            return result;
        }

        public static AtsEconomyOfferWriteResult RemoveInjectedJobReference(string? saveText, string? injectedUnitId)
        {
            var result = new AtsEconomyOfferWriteResult();

            if (string.IsNullOrWhiteSpace(saveText))
            {
                result.Success = false;
                result.Message = "Save text is empty.";
                return result;
            }

            if (string.IsNullOrWhiteSpace(injectedUnitId))
            {
                result.Success = false;
                result.Message = "Injected unit ID is empty.";
                return result;
            }

            var normalized = NormalizeNewlines(saveText);
            if (!HasManagedOffersSection(normalized))
            {
                result.Success = true;
                result.Message = "Managed offers section does not exist. Nothing to remove.";
                result.UpdatedSaveText = normalized;
                return result;
            }

            var refs = ReadExistingRefs(normalized)
                .Where(x => !string.Equals(x, injectedUnitId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var block = BuildOffersBlock(refs);
            normalized = ReplaceManagedOffersSection(normalized, block);

            result.Success = true;
            result.Message = "Injected job reference removed from managed offers section.";
            result.UpdatedSaveText = normalized;
            return result;
        }

        public static List<string> GetInjectedJobReferences(string? saveText)
        {
            if (string.IsNullOrWhiteSpace(saveText))
                return new List<string>();

            return ReadExistingRefs(NormalizeNewlines(saveText));
        }

        public static bool HasManagedOffersSection(string? saveText)
        {
            if (string.IsNullOrWhiteSpace(saveText))
                return false;

            var s = NormalizeNewlines(saveText);
            return s.Contains(StartMarker, StringComparison.Ordinal) &&
                   s.Contains(EndMarker, StringComparison.Ordinal);
        }

        private static List<string> ReadExistingRefs(string saveText)
        {
            var refs = new List<string>();

            var start = saveText.IndexOf(StartMarker, StringComparison.Ordinal);
            var end = saveText.IndexOf(EndMarker, StringComparison.Ordinal);

            if (start < 0 || end <= start)
                return refs;

            var section = saveText.Substring(start, end - start);

            foreach (var line in section.Split('\n'))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("job_ref:", StringComparison.OrdinalIgnoreCase))
                    continue;

                var value = trimmed.Substring("job_ref:".Length).Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(value) &&
                    !refs.Contains(value, StringComparer.OrdinalIgnoreCase))
                {
                    refs.Add(value);
                }
            }

            return refs;
        }

        private static string BuildOffersBlock(List<string> refs)
        {
            refs = refs
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine(StartMarker);
            sb.AppendLine($"overwatch_eld_offer_root : {RootUnitId}");
            sb.AppendLine("{");
            sb.AppendLine($" offers_count: {refs.Count}");

            for (var i = 0; i < refs.Count; i++)
            {
                sb.AppendLine($" job_ref[{i}]: \"{Escape(refs[i])}\"");
            }

            sb.AppendLine("}");
            sb.AppendLine(EndMarker);

            return sb.ToString().TrimEnd();
        }

        private static string ReplaceManagedOffersSection(string saveText, string newBlock)
        {
            var start = saveText.IndexOf(StartMarker, StringComparison.Ordinal);
            var end = saveText.IndexOf(EndMarker, StringComparison.Ordinal);

            if (start < 0 || end <= start)
                return AppendManagedOffersSection(saveText, newBlock);

            end += EndMarker.Length;

            var before = saveText.Substring(0, start).TrimEnd('\n');
            var after = saveText.Substring(end).TrimStart('\n');

            var sb = new StringBuilder();
            sb.Append(before);
            sb.Append("\n\n");
            sb.Append(newBlock);

            if (!string.IsNullOrWhiteSpace(after))
            {
                sb.Append("\n\n");
                sb.Append(after);
            }

            return sb.ToString();
        }

        private static string AppendManagedOffersSection(string saveText, string block)
        {
            var endMarker = "\n}";
            var idx = saveText.LastIndexOf(endMarker, StringComparison.Ordinal);

            if (idx >= 0)
                return saveText.Insert(idx, "\n\n" + block + "\n");

            return saveText.TrimEnd() + "\n\n" + block + "\n";
        }

        private static string NormalizeNewlines(string value)
        {
            return (value ?? "").Replace("\r\n", "\n");
        }

        private static string Escape(string value)
        {
            return (value ?? "")
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);
        }
    }
}