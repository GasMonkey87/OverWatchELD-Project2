using System;
using System.Collections.Generic;

namespace OverWatchELD.Services
{
    public sealed class AtsSendToGameRequest
    {
        public DispatchJob? Job { get; set; }
        public string ProfileNameOrId { get; set; } = "";
        public string SaveName { get; set; } = "";
        public bool UseLatestSave { get; set; } = true;
        public bool UseLatestSaveForProfile { get; set; }
    }

    public sealed class AtsSendToGameResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string SavePath { get; set; } = "";
        public string BackupPath { get; set; } = "";
        public string InjectedUnitId { get; set; } = "";
        public List<string> Warnings { get; set; } = new();
    }

    public static class AtsSaveBridgeService
    {
        public static AtsSendToGameResult SendJobToGame(DispatchJob? job)
        {
            return SendJobToGame(new AtsSendToGameRequest
            {
                Job = job,
                UseLatestSave = true
            });
        }

        public static AtsSendToGameResult SendJobToGame(AtsSendToGameRequest? request)
        {
            var result = new AtsSendToGameResult();

            if (request == null)
            {
                result.Success = false;
                result.Message = "Request is null.";
                return result;
            }

            if (request.Job == null)
            {
                result.Success = false;
                result.Message = "Dispatch job is null.";
                return result;
            }

            var validation = AtsLoadValidationService.Validate(
                request.Job.Cargo,
                request.Job.Company,
                request.Job.Trailer);

            if (!validation.IsValid)
            {
                result.Success = false;
                result.Message = string.Join(Environment.NewLine, validation.Errors);
                result.Warnings.AddRange(validation.Errors);
                return result;
            }

            AtsInjectedJobResult inject;

            if (!string.IsNullOrWhiteSpace(request.ProfileNameOrId) &&
                !string.IsNullOrWhiteSpace(request.SaveName))
            {
                inject = AtsSaveJobInjectionService.InjectJobIntoSpecificSave(
                    request.ProfileNameOrId,
                    request.SaveName,
                    request.Job);
            }
            else if (!string.IsNullOrWhiteSpace(request.ProfileNameOrId) &&
                     request.UseLatestSaveForProfile)
            {
                inject = AtsSaveJobInjectionService.InjectJobIntoProfileLatestSave(
                    request.ProfileNameOrId,
                    request.Job);
            }
            else
            {
                inject = AtsSaveJobInjectionService.InjectJobIntoLatestSave(request.Job);
            }

            result.Success = inject.Success;
            result.Message = inject.Message;
            result.SavePath = inject.SavePath;
            result.BackupPath = inject.BackupPath;
            result.InjectedUnitId = inject.InjectedUnitId;
            result.Warnings.AddRange(inject.Warnings);

            if (inject.Success)
            {
                TryMarkJobAsSentToGame(request.Job, inject);
            }

            return result;
        }

        public static AtsSendToGameResult GenerateAndSendToGame(AtsGenerateLoadOptions? options = null)
        {
            var result = new AtsSendToGameResult();

            var job = AtsSmartLoadGeneratorService.GenerateOne(options);
            if (job == null)
            {
                result.Success = false;
                result.Message = "Failed to generate a valid ATS load.";
                return result;
            }

            return SendJobToGame(job);
        }

        private static void TryMarkJobAsSentToGame(DispatchJob job, AtsInjectedJobResult inject)
        {
            try
            {
                job.Status = "Sent To Game";
                job.UpdatedUtc = DateTime.UtcNow;

                var append = $" [ATS Save Injected: {inject.InjectedUnitId}]";
                if (string.IsNullOrWhiteSpace(job.Notes))
                    job.Notes = append.Trim();
                else if (!job.Notes.Contains(inject.InjectedUnitId, StringComparison.OrdinalIgnoreCase))
                    job.Notes += append;
            }
            catch
            {
                // non-fatal
            }
        }
    }
}