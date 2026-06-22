using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace OverWatchELD.Services
{
    public static class AtsCargoMarketInjectorService
    {
        private const string InjectorFolderName = "OverWatchELD";
        private const string InjectorLogFileName = "ats_injector.log";

        // Cargo-market saves vary by ATS version/mod state.
        // We only inject if we can find a real template unit already in the decrypted save.
        private static readonly Regex CargoOfferBlockRegex =
            new Regex(
                @"(?<kind>cargo_offer_data|cargo_market_offer_data|trailer_job_offer_data)\s*:\s*(?<unit>[^\s{]+)\s*\{(?<body>.*?)^\}",
                RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Multiline | RegexOptions.Compiled);

        public static bool QueueSingleJob(DispatchJob job)
        {
            if (job == null)
                return false;

            try
            {
                var guard = AtsInjectionGuardService.Check();
                if (!guard.CanInject)
                {
                    Log("Cargo Market injection blocked: " + guard.Message);
                    return false;
                }

                var gameSiiPath = guard.GameSiiPath;
                if (string.IsNullOrWhiteSpace(gameSiiPath) || !File.Exists(gameSiiPath))
                {
                    Log("Cargo Market guard passed but game.sii path is invalid.");
                    return false;
                }

                var text = File.ReadAllText(gameSiiPath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(text) || !text.Contains("SiiNunit", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"Cargo Market game.sii is missing or not decrypted: {gameSiiPath}");
                    return false;
                }

                var template = FindTemplateOffer(text);
                if (template == null)
                {
                    Log("Cargo Market template was not found in game.sii. Open Cargo Market in ATS with an owned/leased trailer, save, decrypt game.sii, and retry.");
                    return false;
                }

                var sourceCompany = AtsSaveLookupService.ResolveBestCompany(
                    gameSiiPath,
                    job.Company,
                    job.OriginCity,
                    job.OriginState);

                var destinationCompany = AtsSaveLookupService.ResolveBestCompany(
                    gameSiiPath,
                    job.Company,
                    job.DestinationCity,
                    job.DestinationState);

                if (sourceCompany == null)
                {
                    Log($"Cargo Market source company resolve failed for load {job.LoadNumber}. Company={job.Company} City={job.OriginCity}");
                    return false;
                }

                if (destinationCompany == null)
                {
                    Log($"Cargo Market destination company resolve failed for load {job.LoadNumber}. Company={job.Company} City={job.DestinationCity}");
                    return false;
                }

                var unitName = BuildUnitName(job, template.Value.Kind);

                if (ContainsExistingInjectedUnit(text, template.Value.Kind, unitName, job))
                {
                    Log($"Cargo Market skipped duplicate load {job.LoadNumber} ({unitName}).");
                    return true;
                }

                string backupPath = "";
                if (guard.Options.RequireBackupBeforeInjection)
                {
                    backupPath = CreateBackup(gameSiiPath);
                    if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
                    {
                        Log("Cargo Market injection blocked because backup creation failed.");
                        return false;
                    }
                }

                var clonedBlock = BuildClonedOfferBlock(
                    job,
                    template.Value.Kind,
                    unitName,
                    template.Value.UnitName,
                    template.Value.Body,
                    sourceCompany,
                    destinationCompany);

                var updated = InjectSnippetIntoGameSii(text, clonedBlock);
                if (string.Equals(updated, text, StringComparison.Ordinal))
                {
                    Log($"Cargo Market injection failed for load {job.LoadNumber}. Could not locate final SiiNunit boundary.");
                    return false;
                }

                File.WriteAllText(gameSiiPath, updated, new UTF8Encoding(false));

                Log(
                    $"Injected load {job.LoadNumber} into Cargo Market. " +
                    $"Save={gameSiiPath} Backup={backupPath} Unit={unitName} Kind={template.Value.Kind} " +
                    $"Source={sourceCompany.UnitId} Destination={destinationCompany.UnitId}");

                return true;
            }
            catch (Exception ex)
            {
                Log($"Cargo Market QueueSingleJob failed for load {(job?.LoadNumber ?? "(null)")}: {ex}");
                return false;
            }
        }

        private static (string Kind, string UnitName, string Body)? FindTemplateOffer(string gameSii)
        {
            if (string.IsNullOrWhiteSpace(gameSii))
                return null;

            var matches = CargoOfferBlockRegex.Matches(gameSii);
            if (matches.Count == 0)
                return null;

            foreach (Match match in matches)
            {
                if (!match.Success)
                    continue;

                var kind = (match.Groups["kind"].Value ?? "").Trim();
                var unit = (match.Groups["unit"].Value ?? "").Trim();
                var body = match.Groups["body"].Value ?? "";

                if (string.IsNullOrWhiteSpace(kind) ||
                    string.IsNullOrWhiteSpace(unit) ||
                    string.IsNullOrWhiteSpace(body))
                    continue;

                return (kind, unit, body);
            }

            return null;
        }

        private static bool ContainsExistingInjectedUnit(string gameSii, string kind, string unitName, DispatchJob job)
        {
            if (string.IsNullOrWhiteSpace(gameSii))
                return false;

            if (gameSii.Contains($"{kind} : {unitName}", StringComparison.OrdinalIgnoreCase))
                return true;

            var loadNumber = SiiSafe(job.LoadNumber);
            if (!string.IsNullOrWhiteSpace(loadNumber) &&
                gameSii.Contains($"display_name: \"{loadNumber}\"", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private static string BuildClonedOfferBlock(
            DispatchJob job,
            string kind,
            string newUnitName,
            string templateUnitName,
            string templateBody,
            AtsCompanyUnitMatch sourceCompany,
            AtsCompanyUnitMatch destinationCompany)
        {
            var body = templateBody;

            var cargo = SiiSafe(job.Cargo);
            var trailer = SiiSafe(job.Trailer);
            var displayName = SiiSafe(
                string.IsNullOrWhiteSpace(job.LoadNumber)
                    ? $"{cargo}_{job.OriginCity}_{job.DestinationCity}"
                    : job.LoadNumber);

            var income = InferIncome(job);
            var weightKg = InferWeightKg(job);
            var expireEpoch = DateTimeOffset.UtcNow.AddDays(2).ToUnixTimeSeconds();
            var sourceCity = SiiSafe(AtsSaveLookupService.ResolveCityToken(job.OriginCity));
            var destinationCity = SiiSafe(AtsSaveLookupService.ResolveCityToken(job.DestinationCity));

            body = ReplaceOrInsertStringField(body, "cargo", cargo);
            body = ReplaceOrInsertStringField(body, "display_name", displayName);
            body = ReplaceOrInsertStringField(body, "trailer_variant", trailer);

            body = ReplaceOrInsertStringField(body, "source_city", sourceCity);
            body = ReplaceOrInsertStringField(body, "destination_city", destinationCity);

            body = ReplaceOrInsertStringField(body, "company", sourceCompany.UnitId);
            body = ReplaceOrInsertStringField(body, "source_company", sourceCompany.UnitId);
            body = ReplaceOrInsertStringField(body, "destination_company", destinationCompany.UnitId);
            body = ReplaceOrInsertStringField(body, "target_company", destinationCompany.UnitId);

            body = ReplaceOrInsertNumericField(body, "income", income.ToString(CultureInfo.InvariantCulture));
            body = ReplaceOrInsertNumericField(body, "cargo_mass", weightKg.ToString(CultureInfo.InvariantCulture));
            body = ReplaceOrInsertNumericField(body, "urgency", "0");
            body = ReplaceOrInsertNumericField(body, "exp_time", expireEpoch.ToString(CultureInfo.InvariantCulture));

            if (!string.IsNullOrWhiteSpace(templateUnitName))
                body = body.Replace(templateUnitName, newUnitName, StringComparison.OrdinalIgnoreCase);

            return
$@"{kind} : {newUnitName}
{{
{TrimBodyForWrite(body)}
}}";
        }

        private static string ReplaceOrInsertStringField(string body, string fieldName, string newValue)
        {
            var pattern = $@"(?im)^(?<indent>\s*){Regex.Escape(fieldName)}\s*:\s*""[^""]*"".*$";
            if (Regex.IsMatch(body, pattern))
            {
                return Regex.Replace(
                    body,
                    pattern,
                    m => $"{m.Groups["indent"].Value}{fieldName}: \"{newValue}\"");
            }

            return AppendField(body, $"{fieldName}: \"{newValue}\"");
        }

        private static string ReplaceOrInsertNumericField(string body, string fieldName, string newValue)
        {
            var pattern = $@"(?im)^(?<indent>\s*){Regex.Escape(fieldName)}\s*:\s*.*$";
            if (Regex.IsMatch(body, pattern))
            {
                return Regex.Replace(
                    body,
                    pattern,
                    m => $"{m.Groups["indent"].Value}{fieldName}: {newValue}");
            }

            return AppendField(body, $"{fieldName}: {newValue}");
        }

        private static string AppendField(string body, string line)
        {
            body ??= "";
            var trimmed = body.TrimEnd();

            if (string.IsNullOrWhiteSpace(trimmed))
                return $" {line}{Environment.NewLine}";

            return trimmed + Environment.NewLine + " " + line + Environment.NewLine;
        }

        private static string TrimBodyForWrite(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return "";

            var lines = body
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n')
                .Select(x => x.TrimEnd())
                .ToList();

            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
                lines.RemoveAt(0);

            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
                lines.RemoveAt(lines.Count - 1);

            return string.Join(Environment.NewLine, lines);
        }

        private static string CreateBackup(string gameSiiPath)
        {
            try
            {
                var dir = Path.GetDirectoryName(gameSiiPath) ?? "";
                var backupDir = Path.Combine(dir, "eld_backups");
                Directory.CreateDirectory(backupDir);

                var backupFile = Path.Combine(
                    backupDir,
                    $"game_{DateTime.Now:yyyyMMdd_HHmmss}.sii.bak");

                File.Copy(gameSiiPath, backupFile, true);
                return backupFile;
            }
            catch (Exception ex)
            {
                Log("Cargo Market backup failed: " + ex.Message);
                return "";
            }
        }

        private static string InjectSnippetIntoGameSii(string original, string snippet)
        {
            if (string.IsNullOrWhiteSpace(original))
                return original;

            var lastBrace = original.LastIndexOf('}');
            if (lastBrace <= 0)
                return original;

            return original.Insert(lastBrace, Environment.NewLine + snippet + Environment.NewLine);
        }

        private static string BuildUnitName(DispatchJob job, string kind)
        {
            var prefix = string.IsNullOrWhiteSpace(kind) ? "cargo" : kind.Replace("_data", "");
            var baseId = string.IsNullOrWhiteSpace(job.LoadNumber)
                ? $"oweld_{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}"
                : $"oweld_{prefix}_{SanitizeId(job.LoadNumber)}";

            return baseId.ToLowerInvariant();
        }

        private static int InferIncome(DispatchJob job)
        {
            var payoutText =
                FirstNonEmpty(
                    TryGetStringProperty(job, "Payout"),
                    TryGetStringProperty(job, "Revenue"),
                    TryGetStringProperty(job, "Pay"));

            if (decimal.TryParse(payoutText, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
                return Math.Max(0, decimal.ToInt32(decimal.Round(amount, 0)));

            if (decimal.TryParse(payoutText, NumberStyles.Any, CultureInfo.CurrentCulture, out amount))
                return Math.Max(0, decimal.ToInt32(decimal.Round(amount, 0)));

            return Math.Max(0, job.Miles * 55);
        }

        private static int InferWeightKg(DispatchJob job)
        {
            var weightText =
                FirstNonEmpty(
                    TryGetStringProperty(job, "CargoWeight"),
                    TryGetStringProperty(job, "CargoWeightLbs"),
                    TryGetStringProperty(job, "Weight"));

            if (double.TryParse(weightText, NumberStyles.Any, CultureInfo.InvariantCulture, out var pounds))
                return Math.Max(0, (int)Math.Round(pounds * 0.45359237));

            if (double.TryParse(weightText, NumberStyles.Any, CultureInfo.CurrentCulture, out pounds))
                return Math.Max(0, (int)Math.Round(pounds * 0.45359237));

            return 0;
        }

        private static string TryGetStringProperty(object obj, string propertyName)
        {
            try
            {
                var prop = obj.GetType().GetProperty(propertyName);
                var value = prop?.GetValue(obj);
                return value?.ToString()?.Trim() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }

        private static string SanitizeId(string value)
        {
            var chars = (value ?? "")
                .Trim()
                .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
                .ToArray();

            var result = new string(chars).Trim('_');
            return string.IsNullOrWhiteSpace(result) ? "job" : result;
        }

        private static string SiiSafe(string value)
        {
            return (value ?? "")
                .Trim()
                .Replace("\\", "_")
                .Replace("\"", "")
                .Replace("\r", " ")
                .Replace("\n", " ");
        }

        private static void Log(string message)
        {
            try
            {
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var folder = Path.Combine(docs, InjectorFolderName);
                Directory.CreateDirectory(folder);

                var path = Path.Combine(folder, InjectorLogFileName);
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
                File.AppendAllText(path, line, Encoding.UTF8);
            }
            catch
            {
            }
        }
    }
}