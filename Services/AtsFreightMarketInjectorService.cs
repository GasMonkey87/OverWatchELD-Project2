using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace OverWatchELD.Services
{
    public static class AtsFreightMarketInjectorService
    {
        private const string InjectorFolderName = "OverWatchELD";
        private const string InjectorLogFileName = "ats_injector.log";

        private static readonly Regex JobOfferBlockRegex =
            new Regex(
                @"job_offer_data\s*:\s*(?<unit>[^\s{]+)\s*\{(?<body>.*?)^\}",
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
                    Log("Injection blocked: " + guard.Message);
                    return false;
                }

                var gameSiiPath = guard.GameSiiPath;
                if (string.IsNullOrWhiteSpace(gameSiiPath) || !File.Exists(gameSiiPath))
                {
                    Log("Guard passed but game.sii path is invalid.");
                    return false;
                }

                var text = File.ReadAllText(gameSiiPath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(text) || !text.Contains("SiiNunit", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"game.sii is missing or not decrypted: {gameSiiPath}");
                    return false;
                }

                var template = FindTemplateOffer(text);
                if (template == null)
                {
                    Log("No existing job_offer_data template was found in game.sii.");
                    return false;
                }

                var targetCompany = ResolveTargetCompany(job, gameSiiPath);
                if (string.IsNullOrWhiteSpace(targetCompany))
                {
                    Log($"Failed to resolve target company for load {job.LoadNumber}. Company={job.Company} City={job.DestinationCity}");
                    return false;
                }

                var unitName = BuildUnitName(job);

                if (ContainsExistingInjectedUnit(text, unitName, job))
                {
                    Log($"Skipped duplicate Freight injection for load {job.LoadNumber} ({unitName}).");
                    return true;
                }

                string backupPath = "";
                if (guard.Options.RequireBackupBeforeInjection)
                {
                    backupPath = CreateBackup(gameSiiPath);
                    if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
                    {
                        Log("Injection blocked because backup creation failed.");
                        return false;
                    }
                }

                var snippet = BuildClonedOfferBlock(job, unitName, targetCompany, template.Value.UnitName, template.Value.Body);

                var updated = InjectSnippetIntoGameSii(text, snippet);
                if (string.Equals(updated, text, StringComparison.Ordinal))
                {
                    Log("Injection failed: could not locate final SiiNunit boundary.");
                    return false;
                }

                File.WriteAllText(gameSiiPath, updated, new UTF8Encoding(false));

                Log($"Injected Freight load {job.LoadNumber} into ATS save. Save={gameSiiPath} Backup={backupPath} Unit={unitName} Target={targetCompany}");
                return true;
            }
            catch (Exception ex)
            {
                Log($"QueueSingleJob failed for load {(job?.LoadNumber ?? "(null)")}: {ex}");
                return false;
            }
        }

        private static (string UnitName, string Body)? FindTemplateOffer(string gameSii)
        {
            var matches = JobOfferBlockRegex.Matches(gameSii);
            if (matches.Count == 0)
                return null;

            foreach (Match match in matches)
            {
                if (!match.Success)
                    continue;

                var unit = (match.Groups["unit"].Value ?? "").Trim();
                var body = match.Groups["body"].Value ?? "";

                if (string.IsNullOrWhiteSpace(unit) || string.IsNullOrWhiteSpace(body))
                    continue;

                if (body.Contains("target:", StringComparison.OrdinalIgnoreCase) &&
                    body.Contains("cargo:", StringComparison.OrdinalIgnoreCase) &&
                    body.Contains("trailer_variant:", StringComparison.OrdinalIgnoreCase))
                {
                    return (unit, body);
                }
            }

            return null;
        }

        private static string ResolveTargetCompany(DispatchJob job, string gameSiiPath)
        {
            var match = AtsSaveLookupService.ResolveBestCompany(
                gameSiiPath,
                job.Company,
                job.DestinationCity,
                job.DestinationState);

            if (match == null)
                return "";

            var unit = match.UnitId ?? "";
            var parts = unit.Split('.', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 2)
                return $"{parts[^2]}.{parts[^1]}";

            return unit;
        }

        private static bool ContainsExistingInjectedUnit(string gameSii, string unitName, DispatchJob job)
        {
            if (gameSii.Contains($"job_offer_data : {unitName}", StringComparison.OrdinalIgnoreCase))
                return true;

            var cargo = ResolveCargoToken(job.Cargo);
            var target = $"{NormalizeCompanyToken(job.Company)}.{NormalizeCityToken(job.DestinationCity)}";

            if (!string.IsNullOrWhiteSpace(cargo) &&
                !string.IsNullOrWhiteSpace(target) &&
                gameSii.Contains($"cargo: {cargo}", StringComparison.OrdinalIgnoreCase) &&
                gameSii.Contains($"target: \"{target}\"", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private static string BuildClonedOfferBlock(
            DispatchJob job,
            string newUnitName,
            string targetCompany,
            string templateUnitName,
            string templateBody)
        {
            var body = templateBody;

            var cargo = ResolveCargoToken(job.Cargo);
            var trailerVariant = ResolveTrailerVariant(job.Trailer);
            var trailerDefinition = ResolveTrailerDefinition(job.Trailer);
            var companyTruck = ResolveCompanyTruck(templateBody);

            var expirationTime = "49793";
            var urgency = "0";
            var shortestDistanceKm = Math.Max(1, (int)Math.Round(Math.Max(1, job.Miles) * 1.60934)).ToString(CultureInfo.InvariantCulture);
            var ferryTime = "0";
            var ferryPrice = "0";
            var unitsCount = "26";
            var fillRatio = "1";
            var trailerPlace = "0";

            body = ReplaceOrInsertBareField(body, "target", $"\"{targetCompany}\"");
            body = ReplaceOrInsertBareField(body, "expiration_time", expirationTime);
            body = ReplaceOrInsertBareField(body, "urgency", urgency);
            body = ReplaceOrInsertBareField(body, "shortest_distance_km", shortestDistanceKm);
            body = ReplaceOrInsertBareField(body, "ferry_time", ferryTime);
            body = ReplaceOrInsertBareField(body, "ferry_price", ferryPrice);
            body = ReplaceOrInsertBareField(body, "cargo", cargo);
            body = ReplaceOrInsertBareField(body, "company_truck", $"\"{companyTruck}\"");
            body = ReplaceOrInsertBareField(body, "trailer_variant", trailerVariant);
            body = ReplaceOrInsertBareField(body, "trailer_definition", trailerDefinition);
            body = ReplaceOrInsertBareField(body, "units_count", unitsCount);
            body = ReplaceOrInsertBareField(body, "fill_ratio", fillRatio);
            body = ReplaceOrInsertBareField(body, "trailer_place", trailerPlace);

            if (!string.IsNullOrWhiteSpace(templateUnitName))
                body = body.Replace(templateUnitName, newUnitName, StringComparison.OrdinalIgnoreCase);

            return
$@"job_offer_data : {newUnitName}
{{
{TrimBodyForWrite(body)}
}}";
        }

        private static string ResolveCargoToken(string? cargo)
        {
            var s = (cargo ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s))
                return "cargo.househd_appl";

            if (s.StartsWith("cargo.", StringComparison.OrdinalIgnoreCase))
                return s;

            return "cargo." + NormalizeTokenForDef(s);
        }

        private static string ResolveTrailerVariant(string? trailer)
        {
            var s = (trailer ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s))
                return "trailer.scs_ins2";

            if (s.StartsWith("trailer.", StringComparison.OrdinalIgnoreCase))
                return s;

            return "trailer." + NormalizeTokenForDef(s);
        }

        private static string ResolveTrailerDefinition(string? trailer)
        {
            var s = (trailer ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s))
                return "trailer_def.scs.box.double_p.insulated";

            if (s.StartsWith("trailer_def.", StringComparison.OrdinalIgnoreCase))
                return s;

            if (s.StartsWith("trailer.", StringComparison.OrdinalIgnoreCase))
                return "trailer_def." + s.Substring("trailer.".Length);

            return "trailer_def." + NormalizeTokenForDef(s);
        }

        private static string ResolveCompanyTruck(string templateBody)
        {
            var m = Regex.Match(templateBody ?? "", @"(?im)^\s*company_truck\s*:\s*""([^""]+)""\s*$");
            if (m.Success)
                return m.Groups[1].Value.Trim();

            return "double/freightliner_xl_d";
        }

        private static string ReplaceOrInsertBareField(string body, string fieldName, string value)
        {
            var pattern = $@"(?im)^(?<indent>\s*){Regex.Escape(fieldName)}\s*:\s*.*$";
            if (Regex.IsMatch(body, pattern))
            {
                return Regex.Replace(
                    body,
                    pattern,
                    m => $"{m.Groups["indent"].Value}{fieldName}: {value}");
            }

            return AppendField(body, $"{fieldName}: {value}");
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
                Log("Backup failed: " + ex.Message);
                return "";
            }
        }

        private static string InjectSnippetIntoGameSii(string original, string snippet)
        {
            var lastBrace = original.LastIndexOf('}');
            if (lastBrace <= 0)
                return original;

            return original.Insert(lastBrace, Environment.NewLine + snippet + Environment.NewLine);
        }

        private static string BuildUnitName(DispatchJob job)
        {
            var baseId = string.IsNullOrWhiteSpace(job.LoadNumber)
                ? $"oweld_freight_{DateTime.Now:yyyyMMdd_HHmmss}"
                : $"oweld_freight_{SanitizeId(job.LoadNumber)}";

            return baseId.ToLowerInvariant();
        }

        private static string NormalizeCompanyToken(string? value)
        {
            var s = (value ?? "").Trim().ToLowerInvariant().Replace(" ", "_").Replace("-", "_");
            if (s.Contains("wallbert") || s.Contains("walmart"))
                return "wal_mkt";
            return s;
        }

        private static string NormalizeCityToken(string? value)
        {
            return (value ?? "").Trim().ToLowerInvariant().Replace(" ", "_").Replace("-", "_");
        }

        private static string NormalizeTokenForDef(string? value)
        {
            return (value ?? "")
                .Trim()
                .ToLowerInvariant()
                .Replace(" ", "_")
                .Replace("-", "_")
                .Replace("/", "_");
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