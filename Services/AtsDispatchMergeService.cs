
using System;
using System.Collections.Generic;
using System.Linq;
using ATS.Dispatcher.Models;
using ATS.Dispatcher.Services;
using OverWatchELD.Models;

namespace OverWatchELD.Services
{
    /// <summary>
    /// Integrates ATS.Dispatcher backend job/mod scanning into the main OverWatch ELD app.
    /// No separate dispatcher bot/process required.
    /// </summary>
    public static class AtsDispatchMergeService
    {
        private static readonly object _gate = new();
        private static bool _started;

        public static void Start()
        {
            lock (_gate)
            {
                if (_started) return;
                _started = true;
            }

            try { ModDefinitionScanner.ScanMods(); } catch { }
            try { ImportModLoads(); } catch { }
            try { ImportSaveJobs(); } catch { }

            try
            {
                AtsJobService.JobsChanged -= AtsJobService_JobsChanged;
                AtsJobService.JobsChanged += AtsJobService_JobsChanged;
                AtsJobService.StartWatching();
            }
            catch { }
        }

        private static void AtsJobService_JobsChanged()
        {
            try { ImportSaveJobs(); } catch { }
        }

        public static void ImportModLoads()
        {
            try { ModDefinitionScanner.ScanMods(); } catch { }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var cargo in ModDefinitionScanner.CargoNames ?? new List<string>())
            {
                var clean = Pretty(cargo);
                if (string.IsNullOrWhiteSpace(clean)) continue;
                var key = "modcargo:" + clean;
                if (!seen.Add(key)) continue;

                var job = new VtcJob
                {
                    Id = key,
                    DriverKey = "dispatch-pool",
                    DriverName = "Dispatch Pool",
                    Title = clean,
                    Body = "Imported from ATS mod folder.",
                    Origin = "Mod Cargo Pool",
                    Destination = "Assign Destination",
                    Status = "Assigned",
                    StatusNote = "mod-import"
                };

                VtcJobsService.Upsert(job);
            }

            foreach (var trailer in ModDefinitionScanner.TrailerNames ?? new List<string>())
            {
                var clean = Pretty(trailer);
                if (string.IsNullOrWhiteSpace(clean)) continue;
                var key = "modtrailer:" + clean;
                if (!seen.Add(key)) continue;

                var job = new VtcJob
                {
                    Id = key,
                    DriverKey = "dispatch-pool",
                    DriverName = "Dispatch Pool",
                    Title = clean + " Trailer",
                    Body = "Imported trailer definition from ATS mod folder.",
                    Origin = "Trailer Pool",
                    Destination = "Assign Destination",
                    Status = "Assigned",
                    StatusNote = "mod-import"
                };

                VtcJobsService.Upsert(job);
            }
        }

        public static void ImportSaveJobs()
        {
            IEnumerable<JobInfo> jobs;
            try { jobs = AtsJobService.GetJobsFromSave() ?? Enumerable.Empty<JobInfo>(); }
            catch { return; }

            foreach (var j in jobs)
            {
                if (string.IsNullOrWhiteSpace(j?.Cargo)) continue;
                if (j.Cargo.Contains("No ATS profiles found", StringComparison.OrdinalIgnoreCase)) continue;
                if (j.Cargo.Contains("No jobs detected", StringComparison.OrdinalIgnoreCase)) continue;

                var id = "atssave:" + Hash($"{j.Cargo}|{j.SourceCity}|{j.DestinationCity}|{j.FilePath}");
                var title = Pretty(j.Cargo);
                var note = $"ATS save import{(j.Income > 0 ? $" | ${j.Income:N0}" : "")}{(j.DistanceKm > 0 ? $" | {Math.Round(j.DistanceKm * 0.621371, 0):N0} mi" : "")}";

                var job = new VtcJob
                {
                    Id = id,
                    DriverKey = "dispatch-pool",
                    DriverName = "Dispatch Pool",
                    Title = title,
                    Body = note,
                    Origin = Pretty(j.SourceCity),
                    Destination = Pretty(j.DestinationCity),
                    Status = "Assigned",
                    StatusNote = string.IsNullOrWhiteSpace(j.Company) ? "ats-save-import" : $"company:{j.Company}"
                };

                VtcJobsService.Upsert(job);
            }
        }

        private static string Pretty(string? value)
        {
            value = (value ?? string.Empty).Trim().Replace('_', ' ').Replace('.', ' ');
            return string.Join(" ", value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.Length <= 1 ? w.ToUpperInvariant() : char.ToUpperInvariant(w[0]) + w.Substring(1)));
        }

        private static string Hash(string value)
        {
            unchecked
            {
                int hash = 23;
                foreach (var ch in value ?? string.Empty)
                    hash = (hash * 31) + ch;
                return Math.Abs(hash).ToString();
            }
        }
    }
}
