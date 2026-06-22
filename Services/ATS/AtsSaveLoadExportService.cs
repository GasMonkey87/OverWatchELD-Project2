using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace OverWatchELD.Services.ATS
{
    /// <summary>
    /// Safe ATS load export layer.
    /// IMPORTANT:
    /// ATS save injection varies by profile/save format and whether the save is compressed/encrypted.
    /// This service creates a backup and an import-ready .sii-style export file first.
    /// Hook your existing save-game mapper/injector here if you already have one.
    /// </summary>
    public sealed class AtsSaveLoadExportService
    {
        private readonly AtsUserFolderLocatorService _folders;

        public AtsSaveLoadExportService(AtsUserFolderLocatorService folders)
        {
            _folders = folders;
        }

        public AtsSaveInjectionResult ExportForMostRecentSave(AtsCreatedLoad load)
        {
            var saveFolder = _folders.GetMostRecentSaveFolder();
            if (string.IsNullOrWhiteSpace(saveFolder))
            {
                return new AtsSaveInjectionResult
                {
                    Ok = false,
                    Message = "No ATS save folder found. Start ATS once, create/load a profile, then try again."
                };
            }

            var gameSii = Path.Combine(saveFolder, "game.sii");
            var backup = "";

            if (File.Exists(gameSii))
            {
                backup = Path.Combine(saveFolder, $"game.sii.overwatch-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.bak");
                File.Copy(gameSii, backup, overwrite: false);
            }

            var exportFolder = Path.Combine(saveFolder, "overwatch_eld_exports");
            Directory.CreateDirectory(exportFolder);

            var exportPath = Path.Combine(exportFolder, $"{Safe(load.LoadNumber)}.overwatch_load.sii");
            File.WriteAllText(exportPath, BuildSiiExport(load), Encoding.UTF8);

            var jsonPath = Path.ChangeExtension(exportPath, ".json");
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(load, new JsonSerializerOptions { WriteIndented = true }));

            return new AtsSaveInjectionResult
            {
                Ok = true,
                Message = "Load exported beside the user's latest ATS save. Existing game.sii was backed up when present. Connect this to your existing ATS save injector to write directly into game.sii.",
                SaveFolder = saveFolder,
                BackupPath = backup,
                ExportPath = exportPath
            };
        }

        private static string BuildSiiExport(AtsCreatedLoad load)
        {
            var cargo = load.AtsTokens.TryGetValue("cargo", out var c) ? c : "";
            var trailer = load.AtsTokens.TryGetValue("trailer", out var t) ? t : "";

            return $$"""
SiiNunit
{
# OverWatch ELD generated ATS load export
# LoadNumber: {{load.LoadNumber}}
# SourceMod: {{load.SourceModName}}
# This file is intentionally non-destructive. Your game.sii injector should map these tokens
# into the user's active company/job market format.

overwatch_eld_load : {{SafeToken(load.LoadNumber)}} {
 cargo_token: "{{cargo}}"
 trailer_token: "{{trailer}}"
 cargo_name: "{{Escape(load.CargoName)}}"
 trailer_name: "{{Escape(load.TrailerName)}}"
 weight_lbs: {{load.WeightLbs}}
 pickup_city: "{{Escape(load.PickupCity)}}"
 delivery_city: "{{Escape(load.DeliveryCity)}}"
 company_from: "{{Escape(load.CompanyFrom)}}"
 company_to: "{{Escape(load.CompanyTo)}}"
 created_utc: "{{load.CreatedUtc:o}}"
}
}
""";
        }

        private static string Safe(string value)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');
            return value;
        }

        private static string SafeToken(string value)
        {
            var sb = new StringBuilder();
            foreach (var ch in value.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch)) sb.Append(ch);
                else sb.Append('_');
            }
            return sb.ToString().Trim('_');
        }

        private static string Escape(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
