using OverWatchELD.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Stores
{
    public sealed class InspectionRecordStore
    {
        private static readonly object Gate = new();
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private static string Folder
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "OverWatchELD",
                    "Inspections");

                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private static string FilePath => Path.Combine(Folder, "inspection_history.json");

        public List<InspectionRecord> LoadAll()
        {
            lock (Gate)
            {
                try
                {
                    if (!File.Exists(FilePath))
                        return new List<InspectionRecord>();

                    var json = File.ReadAllText(FilePath);

                    if (string.IsNullOrWhiteSpace(json))
                        return new List<InspectionRecord>();

                    return JsonSerializer.Deserialize<List<InspectionRecord>>(json, JsonOptions)
                           ?? new List<InspectionRecord>();
                }
                catch
                {
                    return new List<InspectionRecord>();
                }
            }
        }

        public InspectionRecord Add(InspectionRecord record)
        {
            lock (Gate)
            {
                var list = LoadAll();

                if (string.IsNullOrWhiteSpace(record.Id))
                    record.Id = Guid.NewGuid().ToString("N");

                if (record.CreatedUtc == default)
                    record.CreatedUtc = DateTime.UtcNow;

                if (string.IsNullOrWhiteSpace(record.InspectionNumber))
                    record.InspectionNumber = NextNumber(list);

                list.RemoveAll(x => Same(x.Id, record.Id) || Same(x.InspectionNumber, record.InspectionNumber));
                list.Add(record);

                SaveAll(list);
                return record;
            }
        }

        private static void SaveAll(List<InspectionRecord> list)
        {
            var ordered = list
                .OrderByDescending(x => x.CreatedUtc)
                .ToList();

            File.WriteAllText(FilePath, JsonSerializer.Serialize(ordered, JsonOptions));
        }

        private static string NextNumber(List<InspectionRecord> existing)
        {
            var today = DateTime.UtcNow.ToString("yyyyMMdd");
            var prefix = $"INSP-{today}-";
            var max = 0;

            foreach (var r in existing)
            {
                var n = r.InspectionNumber ?? "";
                if (!n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (int.TryParse(n.Substring(prefix.Length), out var value) && value > max)
                    max = value;
            }

            return prefix + (max + 1).ToString("000");
        }

        private static bool Same(string? a, string? b) =>
            !string.IsNullOrWhiteSpace(a) &&
            !string.IsNullOrWhiteSpace(b) &&
            string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}