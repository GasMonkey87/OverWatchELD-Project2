using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using ATS.Dispatcher.Models;

namespace ATS.Dispatcher.Services
{
    public static class AtsJobService
    {
        private static FileSystemWatcher? _watcher;
        public static event Action? JobsChanged;

        // --------------------------------------------------------------------
        //  Reads job data from ATS save files
        // --------------------------------------------------------------------
        public static IEnumerable<JobInfo> GetJobsFromSave()
        {
            var jobs = new List<JobInfo>();
            var docPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var atsPath = Path.Combine(docPath, "American Truck Simulator", "profiles");

            if (!Directory.Exists(atsPath))
            {
                jobs.Add(new JobInfo { Cargo = "No ATS profiles found." });
                return jobs;
            }

            // Regex patterns for typical job fields
            var cargoRx = new Regex(@"cargo:.*?""(.+?)""", RegexOptions.IgnoreCase);
            var sourceRx = new Regex(@"source_city:.*?""(.+?)""", RegexOptions.IgnoreCase);
            var destRx = new Regex(@"destination_city:.*?""(.+?)""", RegexOptions.IgnoreCase);
            var distRx = new Regex(@"distance_km: (\d+)", RegexOptions.IgnoreCase);
            var incomeRx = new Regex(@"income: (\d+)", RegexOptions.IgnoreCase);
            var companyRx = new Regex(@"company_truck:.*?""(.+?)""", RegexOptions.IgnoreCase);

            foreach (var profile in Directory.GetDirectories(atsPath))
            {
                var savePath = Path.Combine(profile, "save");
                if (!Directory.Exists(savePath)) continue;

                foreach (var saveDir in Directory.GetDirectories(savePath))
                {
                    foreach (var file in Directory.GetFiles(saveDir, "*.sii", SearchOption.TopDirectoryOnly))
                    {
                        var text = File.ReadAllText(file);

                        if (file.Contains("job") || cargoRx.IsMatch(text))
                        {
                            var job = new JobInfo
                            {
                                Cargo = cargoRx.Match(text).Groups[1].Value,
                                SourceCity = sourceRx.Match(text).Groups[1].Value,
                                DestinationCity = destRx.Match(text).Groups[1].Value,
                                DistanceKm = double.TryParse(distRx.Match(text).Groups[1].Value, out var d) ? d : 0,
                                Income = double.TryParse(incomeRx.Match(text).Groups[1].Value, out var i) ? i : 0,
                                Company = companyRx.Match(text).Groups[1].Value,
                                FilePath = file
                            };

                            if (!string.IsNullOrWhiteSpace(job.Cargo))
                                jobs.Add(job);
                        }
                    }
                }
            }

            if (jobs.Count == 0)
                jobs.Add(new JobInfo { Cargo = "No jobs detected in save files." });

            return jobs;
        }

        // --------------------------------------------------------------------
        //  File-watcher to trigger automatic reload
        // --------------------------------------------------------------------
        public static void StartWatching()
        {
            var docPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var atsPath = Path.Combine(docPath, "American Truck Simulator", "profiles");

            if (!Directory.Exists(atsPath))
                return;

            _watcher = new FileSystemWatcher
            {
                Path = atsPath,
                IncludeSubdirectories = true,
                Filter = "*.sii",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
            };

            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Renamed += OnFileChanged;
            _watcher.EnableRaisingEvents = true;

            Console.WriteLine("Watching ATS save files for changes...");
        }

        private static void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            if (!e.FullPath.EndsWith(".sii", StringComparison.OrdinalIgnoreCase))
                return;

            Console.WriteLine($"Detected save file change: {e.Name}");
            JobsChanged?.Invoke();
        }

        public static void StopWatching()
        {
            if (_watcher == null) return;

            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
    }
}
