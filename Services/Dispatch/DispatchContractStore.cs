using OverWatchELD.Models.Dispatch;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services.Dispatch
{
    public static class DispatchContractStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private static string Folder
        {
            get
            {
                var folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "OverWatchELD",
                    "DispatchContracts");

                Directory.CreateDirectory(folder);
                return folder;
            }
        }

        private static string ContractsPath => Path.Combine(Folder, "dispatch_contracts.json");
        private static string EventsPath => Path.Combine(Folder, "dispatch_contract_events.json");

        public static List<DispatchContract> LoadContracts()
        {
            try
            {
                if (!File.Exists(ContractsPath))
                    return new List<DispatchContract>();

                var json = File.ReadAllText(ContractsPath);
                return JsonSerializer.Deserialize<List<DispatchContract>>(json, JsonOptions)
                       ?? new List<DispatchContract>();
            }
            catch
            {
                return new List<DispatchContract>();
            }
        }

        public static void SaveContracts(List<DispatchContract> rows)
        {
            try
            {
                rows = rows
                    .Where(x => x != null)
                    .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .OrderByDescending(x => x.CreatedUtc)
                    .ToList();

                File.WriteAllText(ContractsPath, JsonSerializer.Serialize(rows, JsonOptions));
            }
            catch
            {
            }
        }

        public static List<DispatchContractEvent> LoadEvents()
        {
            try
            {
                if (!File.Exists(EventsPath))
                    return new List<DispatchContractEvent>();

                var json = File.ReadAllText(EventsPath);
                return JsonSerializer.Deserialize<List<DispatchContractEvent>>(json, JsonOptions)
                       ?? new List<DispatchContractEvent>();
            }
            catch
            {
                return new List<DispatchContractEvent>();
            }
        }

        public static void SaveEvents(List<DispatchContractEvent> rows)
        {
            try
            {
                rows = rows
                    .Where(x => x != null)
                    .OrderByDescending(x => x.CreatedUtc)
                    .Take(10000)
                    .ToList();

                File.WriteAllText(EventsPath, JsonSerializer.Serialize(rows, JsonOptions));
            }
            catch
            {
            }
        }

        public static void AddEvent(DispatchContractEvent item)
        {
            var rows = LoadEvents();
            rows.Insert(0, item);
            SaveEvents(rows);
        }

        public static string NextContractNumber()
        {
            var next = LoadContracts().Count + 1;
            return $"CT-{DateTime.UtcNow:yyyyMMdd}-{next:000}";
        }
    }
}
