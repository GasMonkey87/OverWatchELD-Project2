using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OverWatchELD.Models;

namespace OverWatchELD.Services
{
    public static class InspectionStore
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static string RootDir
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ATS_ELD",
                    "inspections");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private static string Safe(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s.Trim();
        }

        public static string BuildPath(InspectionLog log)
        {
            var day = log.LocalDay.ToString("yyyy-MM-dd");
            var load = Safe(log.LoadId);
            if (string.IsNullOrWhiteSpace(load)) load = "no-load";
            var file = $"{day}__{load}__{log.LogId}.json";
            return Path.Combine(RootDir, file);
        }

        public static void Save(InspectionLog log)
        {
            var path = BuildPath(log);
            SaveTo(path, log);
        }

        public static void SaveTo(string path, InspectionLog log)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(log, JsonOpts);
            File.WriteAllText(path, json);
        }

        public static InspectionLog Load(string path)
        {
            var json = File.ReadAllText(path);
            var log = JsonSerializer.Deserialize<InspectionLog>(json, JsonOpts);
            return log ?? new InspectionLog();
        }

        public static List<SavedInspectionItem> ListForDate(DateOnly day)
        {
            var prefix = day.ToString("yyyy-MM-dd") + "__";
            if (!Directory.Exists(RootDir)) return new();

            return Directory.EnumerateFiles(RootDir, $"{prefix}*.json", SearchOption.TopDirectoryOnly)
                .Select(p =>
                {
                    InspectionLog? log = null;
                    try { log = Load(p); } catch { /* ignore */ }

                    return new SavedInspectionItem
                    {
                        Path = p,
                        Title = log?.DisplayName ?? Path.GetFileNameWithoutExtension(p)
                    };
                })
                .OrderByDescending(i => i.Title)
                .ToList();
        }

        public static (string Path, InspectionLog Log)? LoadMostRecent(DateOnly day)
        {
            var list = ListForDate(day);
            var first = list.FirstOrDefault();
            if (first == null || string.IsNullOrWhiteSpace(first.Path)) return null;

            try
            {
                var log = Load(first.Path);
                return (first.Path, log);
            }
            catch
            {
                return null;
            }
        }
    }
}
