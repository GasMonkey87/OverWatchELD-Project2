using OverWatchELD.Models.Economy;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services.Economy
{
    public static class TruckExpenseAutomationStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private static string EconomyFolder
        {
            get
            {
                var folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "OverWatchELD",
                    "Economy");

                Directory.CreateDirectory(folder);
                return folder;
            }
        }

        private static string SnapshotPath => Path.Combine(EconomyFolder, "truck_expense_automation_snapshots.json");

        public static List<TruckExpenseAutomationSnapshot> LoadSnapshots()
        {
            try
            {
                if (!File.Exists(SnapshotPath))
                    return new List<TruckExpenseAutomationSnapshot>();

                var json = File.ReadAllText(SnapshotPath);
                return JsonSerializer.Deserialize<List<TruckExpenseAutomationSnapshot>>(json, JsonOptions)
                       ?? new List<TruckExpenseAutomationSnapshot>();
            }
            catch
            {
                return new List<TruckExpenseAutomationSnapshot>();
            }
        }

        public static void SaveSnapshots(List<TruckExpenseAutomationSnapshot> rows)
        {
            try
            {
                rows = rows
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.TruckKey))
                    .GroupBy(x => x.TruckKey, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderByDescending(x => x.LastUpdatedUtc).First())
                    .OrderBy(x => x.TruckName)
                    .ToList();

                File.WriteAllText(SnapshotPath, JsonSerializer.Serialize(rows, JsonOptions));
            }
            catch
            {
            }
        }

        public static TruckExpenseAutomationSnapshot? GetSnapshot(string truckKey)
        {
            if (string.IsNullOrWhiteSpace(truckKey))
                return null;

            return LoadSnapshots()
                .FirstOrDefault(x => string.Equals(x.TruckKey, truckKey, StringComparison.OrdinalIgnoreCase));
        }

        public static void UpsertSnapshot(TruckExpenseAutomationSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.TruckKey))
                return;

            var rows = LoadSnapshots();
            rows.RemoveAll(x => string.Equals(x.TruckKey, snapshot.TruckKey, StringComparison.OrdinalIgnoreCase));
            snapshot.LastUpdatedUtc = DateTime.UtcNow;
            rows.Add(snapshot);
            SaveSnapshots(rows);
        }
    }
}
