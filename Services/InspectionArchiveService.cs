using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OverWatchELD.Models;

namespace OverWatchELD.Services
{
    /// <summary>
    /// Simple on-disk archive for DVIR/Inspection reports.
    /// Stores each report as a JSON file under %AppData%\OverWatchELD\Inspections.
    /// </summary>
    public static class InspectionArchiveService
    {
        private static readonly JsonSerializerOptions _json = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private static string ArchiveDir
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OverWatchELD", "Inspections");

        /// <summary>Persist a report to disk (best-effort).</summary>
        public static string Save(InspectionReport report)
        {
            Directory.CreateDirectory(ArchiveDir);

            var stamp = report.SubmittedUtc.ToLocalTime().ToString("yyyyMMdd_HHmmss");
            var id = string.IsNullOrWhiteSpace(report.Id) ? Guid.NewGuid().ToString("N") : report.Id;
            var file = Path.Combine(ArchiveDir, $"{stamp}_{id}.json");

            var json = JsonSerializer.Serialize(report, _json);
            File.WriteAllText(file, json);
            return file;
        }

        /// <summary>
        /// Load reports whose local SubmittedUtc date is within [startLocalDate, endLocalDate] inclusive.
        /// </summary>
        public static List<InspectionReport> LoadRange(DateTime startLocalDate, DateTime endLocalDate)
        {
            try
            {
                Directory.CreateDirectory(ArchiveDir);

                var start = startLocalDate.Date;
                var end = endLocalDate.Date;

                var files = Directory.EnumerateFiles(ArchiveDir, "*.json", SearchOption.TopDirectoryOnly);

                var list = new List<InspectionReport>();
                foreach (var f in files)
                {
                    try
                    {
                        var txt = File.ReadAllText(f);
                        var r = JsonSerializer.Deserialize<InspectionReport>(txt, _json);
                        if (r == null) continue;

                        var localDate = r.SubmittedUtc.ToLocalTime().Date;
                        if (localDate < start || localDate > end) continue;

                        list.Add(r);
                    }
                    catch
                    {
                        // ignore bad files
                    }
                }

                return list
                    .OrderByDescending(r => r.SubmittedUtc)
                    .ToList();
            }
            catch
            {
                return new List<InspectionReport>();
            }
        }

        /// <summary>
        /// Export the requested range into a single JSON file and return the file path.
        /// </summary>
        public static string ExportToFile(DateTime startLocalDate, DateTime endLocalDate)
        {
            var reports = LoadRange(startLocalDate, endLocalDate);

            var outDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OverWatchELD");
            Directory.CreateDirectory(outDir);

            var file = Path.Combine(outDir, $"Inspections_{startLocalDate:yyyyMMdd}_{endLocalDate:yyyyMMdd}.json");
            var json = JsonSerializer.Serialize(reports, _json);
            File.WriteAllText(file, json);

            return file;
        }
    }
}
