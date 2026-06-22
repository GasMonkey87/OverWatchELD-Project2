using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OverWatchELD.Services.ATS;

namespace OverWatchELD.Services
{
    public static class AtsJobExportService
    {
        private static readonly JsonSerializerOptions JsonWriteOpts = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        private static string ExportRoot =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "OverWatchELD",
                "AtsJobExports");

        private static string PendingJobsPath =>
            Path.Combine(ExportRoot, "pending_jobs.json");

        private static string VerificationLogPath =>
            Path.Combine(ExportRoot, "export_verification_log.json");

        public static void ExportPendingJob(DispatchJob job)
        {
            if (job == null) return;

            Directory.CreateDirectory(ExportRoot);

            var jobs = LoadPendingJobs();

            jobs.RemoveAll(x => string.Equals(x.Id, job.Id, StringComparison.OrdinalIgnoreCase));

            jobs.Add(new AtsExportJob
            {
                Id = job.Id,
                LoadNumber = job.LoadNumber,
                Company = job.Company,
                OriginCity = job.OriginCity,
                OriginState = job.OriginState,
                DestinationCity = job.DestinationCity,
                DestinationState = job.DestinationState,
                Miles = job.Miles,
                Cargo = job.Cargo,
                Trailer = job.Trailer,
                AssignedDriver = job.AssignedDriver,
                Status = job.Status,
                Notes = job.Notes,
                CreatedUtc = job.CreatedUtc,
                UpdatedUtc = job.UpdatedUtc,
                ExportedUtc = DateTime.UtcNow
            });

            SavePendingJobs(jobs);
        }

        public static AtsExportVerificationResult VerifyLatestSaveContainsJob(
            DispatchJob? job,
            string? injectedUnitId = null,
            string? explicitGameSiiPath = null)
        {
            if (job == null)
            {
                return SaveVerification(new AtsExportVerificationResult
                {
                    Success = false,
                    Message = "Verification failed: dispatch job was null."
                });
            }

            var path = (explicitGameSiiPath ?? "").Trim();
            AtsSaveLocatorResult? located = null;

            if (string.IsNullOrWhiteSpace(path))
            {
                located = AtsSaveGameLocatorService.LocateLatestSave();
                path = located.GameSiiPath ?? "";
            }

            if (located != null && !located.Success)
            {
                return SaveVerification(new AtsExportVerificationResult
                {
                    Success = false,
                    Message = "Verification failed: " + located.Message,
                    SavePath = path,
                    LoadNumber = job.LoadNumber,
                    InjectedUnitId = injectedUnitId ?? ""
                });
            }

            return VerifyJobInSavePath(job, path, injectedUnitId);
        }

        public static AtsExportVerificationResult VerifyJobInSavePath(
            DispatchJob? job,
            string? gameSiiPath,
            string? injectedUnitId = null)
        {
            if (job == null)
            {
                return SaveVerification(new AtsExportVerificationResult
                {
                    Success = false,
                    Message = "Verification failed: dispatch job was null."
                });
            }

            var result = new AtsExportVerificationResult
            {
                SavePath = gameSiiPath ?? "",
                LoadNumber = job.LoadNumber ?? "",
                InjectedUnitId = injectedUnitId ?? "",
                VerifiedUtc = DateTime.UtcNow
            };

            if (string.IsNullOrWhiteSpace(gameSiiPath))
            {
                result.Success = false;
                result.Message = "Verification failed: no game.sii path was provided.";
                return SaveVerification(result);
            }

            if (!File.Exists(gameSiiPath))
            {
                result.Success = false;
                result.Message = "Verification failed: game.sii was not found.";
                return SaveVerification(result);
            }

            string text;
            try
            {
                text = File.ReadAllText(gameSiiPath);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = "Verification failed: could not read game.sii. " + ex.Message;
                return SaveVerification(result);
            }

            if (string.IsNullOrWhiteSpace(text) ||
                !text.Contains("SiiNunit", StringComparison.OrdinalIgnoreCase))
            {
                result.Success = false;
                result.Message = "Verification failed: game.sii is not editable text. In ATS config.cfg set uset g_save_format \"2\", make a new manual save, then export again.";
                return SaveVerification(result);
            }

            var checks = BuildVerificationNeedles(job, injectedUnitId)
                .Where(x => !string.IsNullOrWhiteSpace(x.Value))
                .ToList();

            foreach (var check in checks)
            {
                if (text.Contains(check.Value, StringComparison.OrdinalIgnoreCase))
                    result.MatchedMarkers.Add(check.Label + ": " + check.Value);
                else
                    result.MissingMarkers.Add(check.Label + ": " + check.Value);
            }

            var hasInjectedId =
                !string.IsNullOrWhiteSpace(injectedUnitId) &&
                text.Contains(injectedUnitId, StringComparison.OrdinalIgnoreCase);

            var hasLoadNumber =
                !string.IsNullOrWhiteSpace(job.LoadNumber) &&
                text.Contains(job.LoadNumber, StringComparison.OrdinalIgnoreCase);

            var hasJobId =
                !string.IsNullOrWhiteSpace(job.Id) &&
                text.Contains(job.Id, StringComparison.OrdinalIgnoreCase);

            if (hasInjectedId || hasLoadNumber || hasJobId)
            {
                result.Success = true;
                result.Message = hasInjectedId
                    ? "Export verified: injected job unit was found in game.sii. Reload the ATS save for the load to appear."
                    : "Export verified: OverWatch load marker was found in game.sii. Reload the ATS save for the load to appear.";
            }
            else
            {
                result.Success = false;
                result.Message = "Export not verified: game.sii was re-read, but the OverWatch job marker was not found. Most likely causes: wrong profile/save, save was not editable text, ATS overwrote the save, or the injector failed before writing.";
            }

            return SaveVerification(result);
        }

        public static List<AtsExportJob> LoadPendingJobs()
        {
            try
            {
                if (!File.Exists(PendingJobsPath))
                    return new List<AtsExportJob>();

                var json = File.ReadAllText(PendingJobsPath);
                var jobs = JsonSerializer.Deserialize<List<AtsExportJob>>(json);
                return jobs ?? new List<AtsExportJob>();
            }
            catch
            {
                return new List<AtsExportJob>();
            }
        }

        private static List<(string Label, string Value)> BuildVerificationNeedles(
            DispatchJob job,
            string? injectedUnitId)
        {
            return new List<(string Label, string Value)>
            {
                ("InjectedUnitId", injectedUnitId ?? ""),
                ("LoadNumber", job.LoadNumber ?? ""),
                ("JobId", job.Id ?? ""),
                ("Cargo", job.Cargo ?? ""),
                ("Trailer", job.Trailer ?? ""),
                ("OriginCity", job.OriginCity ?? ""),
                ("DestinationCity", job.DestinationCity ?? "")
            };
        }

        private static AtsExportVerificationResult SaveVerification(AtsExportVerificationResult result)
        {
            try
            {
                Directory.CreateDirectory(ExportRoot);

                var history = new List<AtsExportVerificationResult>();
                if (File.Exists(VerificationLogPath))
                {
                    try
                    {
                        history = JsonSerializer.Deserialize<List<AtsExportVerificationResult>>(
                            File.ReadAllText(VerificationLogPath)) ?? new List<AtsExportVerificationResult>();
                    }
                    catch
                    {
                        history = new List<AtsExportVerificationResult>();
                    }
                }

                history.Add(result);

                history = history
                    .OrderByDescending(x => x.VerifiedUtc)
                    .Take(50)
                    .ToList();

                File.WriteAllText(VerificationLogPath, JsonSerializer.Serialize(history, JsonWriteOpts));
            }
            catch
            {
                // verification logging is non-fatal
            }

            return result;
        }

        private static void SavePendingJobs(List<AtsExportJob> jobs)
        {
            try
            {
                Directory.CreateDirectory(ExportRoot);
                var json = JsonSerializer.Serialize(jobs, JsonWriteOpts);
                File.WriteAllText(PendingJobsPath, json);
            }
            catch
            {
                // ignore write failures for now
            }
        }
    }

    public sealed class AtsExportVerificationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string SavePath { get; set; } = "";
        public string LoadNumber { get; set; } = "";
        public string InjectedUnitId { get; set; } = "";
        public DateTime VerifiedUtc { get; set; } = DateTime.UtcNow;
        public List<string> MatchedMarkers { get; set; } = new();
        public List<string> MissingMarkers { get; set; } = new();
    }

    public sealed class AtsExportJob
    {
        public string Id { get; set; } = "";
        public string LoadNumber { get; set; } = "";

        public string Company { get; set; } = "";
        public string OriginCity { get; set; } = "";
        public string OriginState { get; set; } = "";
        public string DestinationCity { get; set; } = "";
        public string DestinationState { get; set; } = "";

        public int Miles { get; set; }
        public string Cargo { get; set; } = "";
        public string Trailer { get; set; } = "";

        public string AssignedDriver { get; set; } = "";
        public string Status { get; set; } = "";
        public string Notes { get; set; } = "";

        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public DateTime ExportedUtc { get; set; }
    }
}
