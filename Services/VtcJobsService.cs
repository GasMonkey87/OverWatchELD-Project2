// Services/VtcJobsService.cs
// Simple JSON-backed job store (dependency-free).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OverWatchELD.Models;

namespace OverWatchELD.Services
{
    public static class VtcJobsService
    {
        private static readonly object _lock = new();
        private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

        private static string JobsPath
        {
            get
            {
                try
                {
                    // Put alongside existing VTC link/config storage if possible
                    var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    return Path.Combine(baseDir, "vtc_jobs.json");
                }
                catch
                {
                    return "vtc_jobs.json";
                }
            }
        }

        public static VtcJob Upsert(VtcJob job)
        {
            lock (_lock)
            {
                var all = LoadAllInternal();
                var existing = all.FirstOrDefault(x => string.Equals(x.Id, job.Id, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    job.UpdatedUtc = DateTimeOffset.UtcNow;
                    all.Insert(0, job);
                }
                else
                {
                    existing.DriverKey = job.DriverKey ?? existing.DriverKey;
                    existing.DriverName = job.DriverName ?? existing.DriverName;
                    existing.Title = job.Title ?? existing.Title;
                    existing.Body = job.Body ?? existing.Body;
                    existing.Origin = job.Origin ?? existing.Origin;
                    existing.Destination = job.Destination ?? existing.Destination;
                    existing.DueUtc = job.DueUtc ?? existing.DueUtc;
                    existing.Status = string.IsNullOrWhiteSpace(job.Status) ? existing.Status : job.Status;
                    existing.StatusNote = job.StatusNote ?? existing.StatusNote;
                    existing.UpdatedUtc = DateTimeOffset.UtcNow;
                }

                SaveAllInternal(all);
                return GetById(job.Id) ?? job;
            }
        }

        public static VtcJob? UpdateStatus(string id, string status, string note)
        {
            lock (_lock)
            {
                var all = LoadAllInternal();
                var job = all.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
                if (job == null) return null;

                job.Status = status;
                if (!string.IsNullOrWhiteSpace(note))
                    job.StatusNote = note;

                job.UpdatedUtc = DateTimeOffset.UtcNow;
                SaveAllInternal(all);
                return job;
            }
        }

        public static List<VtcJob> GetForDriver(string driverKey)
        {
            lock (_lock)
            {
                var all = LoadAllInternal();
                return all
                    .Where(x => string.Equals((x.DriverKey ?? "").Trim(), (driverKey ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => x.UpdatedUtc ?? x.CreatedUtc)
                    .ToList();
            }
        }

        public static List<VtcJob> GetAll()
        {
            lock (_lock)
            {
                return LoadAllInternal()
                    .OrderByDescending(x => x.UpdatedUtc ?? x.CreatedUtc)
                    .ToList();
            }
        }

        public static VtcJob? GetById(string id)
        {
            lock (_lock)
            {
                var all = LoadAllInternal();
                return all.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            }
        }

        private static List<VtcJob> LoadAllInternal()
        {
            try
            {
                if (!File.Exists(JobsPath)) return new List<VtcJob>();
                var json = File.ReadAllText(JobsPath);
                return JsonSerializer.Deserialize<List<VtcJob>>(json) ?? new List<VtcJob>();
            }
            catch { return new List<VtcJob>(); }
        }

        private static void SaveAllInternal(List<VtcJob> jobs)
        {
            try
            {
                var json = JsonSerializer.Serialize(jobs, _opts);
                File.WriteAllText(JobsPath, json);
            }
            catch { }
        }
    }
}