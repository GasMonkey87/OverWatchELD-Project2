using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OverWatchELD.Services.ATS;

namespace OverWatchELD.Services
{
    public sealed class AtsInjectedJobResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string SavePath { get; set; } = "";
        public string BackupPath { get; set; } = "";
        public string InjectedUnitId { get; set; } = "";
        public List<string> Warnings { get; set; } = new();
    }

    public static class AtsSaveJobInjectionService
    {
        public static AtsInjectedJobResult InjectJobIntoLatestSave(DispatchJob? job)
        {
            var locate = AtsSaveGameLocatorService.LocateLatestSave();
            if (!locate.Success || string.IsNullOrWhiteSpace(locate.GameSiiPath))
            {
                return new AtsInjectedJobResult
                {
                    Success = false,
                    Message = locate.Message,
                    SavePath = locate.GameSiiPath ?? ""
                };
            }

            return InjectJobIntoSavePath(job, locate.GameSiiPath);
        }

        public static AtsInjectedJobResult InjectJobIntoProfileLatestSave(string? profileNameOrId, DispatchJob? job)
        {
            var locate = AtsSaveGameLocatorService.LocateLatestSaveForProfile(profileNameOrId);
            if (!locate.Success || string.IsNullOrWhiteSpace(locate.GameSiiPath))
            {
                return new AtsInjectedJobResult
                {
                    Success = false,
                    Message = locate.Message,
                    SavePath = locate.GameSiiPath ?? ""
                };
            }

            return InjectJobIntoSavePath(job, locate.GameSiiPath);
        }

        public static AtsInjectedJobResult InjectJobIntoSpecificSave(string? profileNameOrId, string? saveName, DispatchJob? job)
        {
            var locate = AtsSaveGameLocatorService.LocateSpecificSave(profileNameOrId, saveName);
            if (!locate.Success || string.IsNullOrWhiteSpace(locate.GameSiiPath))
            {
                return new AtsInjectedJobResult
                {
                    Success = false,
                    Message = locate.Message,
                    SavePath = locate.GameSiiPath ?? ""
                };
            }

            return InjectJobIntoSavePath(job, locate.GameSiiPath);
        }

        public static AtsInjectedJobResult InjectJobIntoSavePath(DispatchJob? job, string? gameSiiPath)
        {
            var result = new AtsInjectedJobResult
            {
                SavePath = gameSiiPath ?? ""
            };

            if (job == null)
            {
                result.Success = false;
                result.Message = "Dispatch job is null.";
                return result;
            }

            if (string.IsNullOrWhiteSpace(gameSiiPath))
            {
                result.Success = false;
                result.Message = "game.sii path is missing.";
                return result;
            }

            if (!File.Exists(gameSiiPath))
            {
                result.Success = false;
                result.Message = "game.sii file was not found.";
                return result;
            }

            if (!AtsSaveGameReadWriteService.IsSaveReadable(gameSiiPath))
            {
                result.Success = false;
                result.Message = "The ATS save is not readable/editable. Make sure the save is written in editable text format before injecting.";
                return result;
            }

            var mapped = AtsSaveJobMapperService.MapFromDispatchJob(job);
            if (!mapped.Success || mapped.Job == null)
            {
                result.Success = false;
                result.Message = mapped.Message;
                result.Warnings.AddRange(mapped.Errors);
                return result;
            }

            var saveText = AtsSaveGameReadWriteService.ReadSave(gameSiiPath);
            if (string.IsNullOrWhiteSpace(saveText))
            {
                result.Success = false;
                result.Message = "Failed to read game.sii.";
                return result;
            }

            if (!LooksLikeSii(saveText))
            {
                result.Success = false;
                result.Message = "game.sii does not appear to be a text SII save.";
                return result;
            }

            var blockId = BuildInjectedUnitId();
            var appendBlock = BuildJobAppendBlock(blockId, mapped.Job, job);

            if (string.IsNullOrWhiteSpace(appendBlock))
            {
                result.Success = false;
                result.Message = "Failed to build ATS injected job block.";
                return result;
            }

            var alreadyExists = saveText.Contains(blockId, StringComparison.OrdinalIgnoreCase);
            if (alreadyExists)
            {
                result.Success = false;
                result.Message = "Injected job ID already exists in save. Try again.";
                return result;
            }

            var newSaveText = AppendInjectedBlock(saveText, appendBlock);

            var nativeWrite = AtsNativeFreightOfferWriterService.TryAddNativeFreightOffer(newSaveText, mapped.Job);
            if (nativeWrite.Success)
            {
                newSaveText = nativeWrite.UpdatedSaveText;
                result.Warnings.AddRange(nativeWrite.Warnings);
                result.Warnings.Add($"Native offer unit added: {nativeWrite.OfferUnitId}");
            }
            else
            {
                result.Warnings.Add(nativeWrite.Message);
                result.Warnings.AddRange(nativeWrite.Warnings);

                var offersWrite = AtsSaveEconomyOfferWriterService.AddInjectedJobReference(newSaveText, blockId);
                if (!offersWrite.Success)
                {
                    result.Success = false;
                    result.Message = offersWrite.Message;
                    return result;
                }

                newSaveText = offersWrite.UpdatedSaveText;
            }

            var write = AtsSaveGameReadWriteService.WriteSave(gameSiiPath, newSaveText);
            result.BackupPath = write.BackupPath ?? "";
            result.InjectedUnitId = blockId;

            if (!write.Success)
            {
                result.Success = false;
                result.Message = write.Message;
                return result;
            }

            result.Success = true;
            result.Message = "Job block was written into game.sii successfully.";
            result.Warnings.Add("This is the safe MVP injector: it appends a mapped ATS-style block to the save but does not yet fully wire the job into every freight/economy offer list.");
            return result;
        }

        public static string GetLatestSaveDebugInfo()
        {
            var located = AtsSaveGameLocatorService.LocateLatestSave();

            if (!located.Success)
                return located.Message;

            var profileName =
                located.ProfileName ??
                located.ProfileId ??
                located.SelectedProfile ??
                "(unknown)";

            var saveName =
                located.SaveName ??
                located.SelectedSave ??
                "(unknown)";

            var path = located.GameSiiPath ?? "";

            return $"Profile: {profileName}{Environment.NewLine}" +
                   $"Save: {saveName}{Environment.NewLine}" +
                   $"Path: {path}";
        }

        private static bool LooksLikeSii(string text)
        {
            return !string.IsNullOrWhiteSpace(text) &&
                   text.Contains("SiiNunit", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildInjectedUnitId()
        {
            return $"_nameless.overwatcheld.job.{Guid.NewGuid():N}";
        }

        private static string AppendInjectedBlock(string originalText, string block)
        {
            if (string.IsNullOrWhiteSpace(originalText))
                return block;

            var normalized = originalText.Replace("\r\n", "\n");

            var endMarker = "\n}";
            var idx = normalized.LastIndexOf(endMarker, StringComparison.Ordinal);

            if (idx >= 0)
            {
                return normalized.Insert(idx, "\n\n" + block + "\n");
            }

            return normalized + "\n\n" + block + "\n";
        }

        private static string BuildJobAppendBlock(string unitId, AtsSaveFreightJob mapped, DispatchJob source)
        {
            var createdUnix = ToUnixSeconds(DateTime.UtcNow);
            var expiresUnix = ToUnixSeconds(mapped.ExpirationUtc);
            var deadlineUnix = ToUnixSeconds(mapped.DeadlineUtc);

            var notes = Safe(source.Notes);
            var postedBy = Safe(source.PostedBy);
            var assignedDriver = Safe(source.AssignedDriver);
            var assignedTruck = Safe(source.AssignedTruck);
            var loadNo = Safe(source.LoadNumber);

            var sb = new StringBuilder();

            sb.AppendLine($"overwatch_eld_injected_job : {unitId}");
            sb.AppendLine("{");
            sb.AppendLine($" cargo_token: \"{Escape(mapped.CargoToken)}\"");
            sb.AppendLine($" trailer_token: \"{Escape(mapped.TrailerToken)}\"");
            sb.AppendLine($" source_company: \"{Escape(mapped.SourceCompanyToken)}\"");
            sb.AppendLine($" destination_company: \"{Escape(mapped.DestinationCompanyToken)}\"");
            sb.AppendLine($" source_city: \"{Escape(mapped.SourceCityToken)}\"");
            sb.AppendLine($" destination_city: \"{Escape(mapped.DestinationCityToken)}\"");
            sb.AppendLine($" display_cargo: \"{Escape(mapped.DisplayCargo)}\"");
            sb.AppendLine($" display_trailer: \"{Escape(mapped.DisplayTrailer)}\"");
            sb.AppendLine($" display_source_company: \"{Escape(mapped.DisplaySourceCompany)}\"");
            sb.AppendLine($" display_destination_company: \"{Escape(mapped.DisplayDestinationCompany)}\"");
            sb.AppendLine($" display_source_city: \"{Escape(mapped.DisplaySourceCity)}\"");
            sb.AppendLine($" display_destination_city: \"{Escape(mapped.DisplayDestinationCity)}\"");
            sb.AppendLine($" display_source_state: \"{Escape(mapped.DisplaySourceState)}\"");
            sb.AppendLine($" display_destination_state: \"{Escape(mapped.DisplayDestinationState)}\"");
            sb.AppendLine($" income: {mapped.Income}");
            sb.AppendLine($" distance_miles: {mapped.DistanceMiles}");
            sb.AppendLine($" weight_lbs: {mapped.WeightLbs}");
            sb.AppendLine($" expiration_unix: {expiresUnix}");
            sb.AppendLine($" deadline_unix: {deadlineUnix}");
            sb.AppendLine($" created_unix: {createdUnix}");
            sb.AppendLine($" requires_lowboy: {(mapped.RequiresLowboy ? "true" : "false")}");
            sb.AppendLine($" requires_reefer: {(mapped.RequiresReefer ? "true" : "false")}");
            sb.AppendLine($" requires_tanker: {(mapped.RequiresTanker ? "true" : "false")}");
            sb.AppendLine($" requires_gas_tanker: {(mapped.RequiresGasTanker ? "true" : "false")}");
            sb.AppendLine($" is_hazmat_like: {(mapped.IsHazmatLike ? "true" : "false")}");
            sb.AppendLine($" is_oversize_like: {(mapped.IsOversizeLike ? "true" : "false")}");
            sb.AppendLine($" source_load_number: \"{Escape(loadNo)}\"");
            sb.AppendLine($" source_posted_by: \"{Escape(postedBy)}\"");
            sb.AppendLine($" source_assigned_driver: \"{Escape(assignedDriver)}\"");
            sb.AppendLine($" source_assigned_truck: \"{Escape(assignedTruck)}\"");
            sb.AppendLine($" source_notes: \"{Escape(notes)}\"");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static long ToUnixSeconds(DateTime valueUtc)
        {
            var utc = valueUtc.Kind == DateTimeKind.Utc ? valueUtc : valueUtc.ToUniversalTime();
            return new DateTimeOffset(utc).ToUnixTimeSeconds();
        }

        private static string Safe(string? value)
        {
            return value ?? "";
        }

        private static string Escape(string value)
        {
            return (value ?? "")
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);
        }
    }
}