using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services.ATS
{
    public sealed class IndividualLoadHistoryRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string LoadNumber { get; set; } = "";
        public string Cargo { get; set; } = "";
        public string Trailer { get; set; } = "";
        public string PickupCompany { get; set; } = "";
        public string PickupCity { get; set; } = "";
        public string PickupState { get; set; } = "";
        public string DropOffCompany { get; set; } = "";
        public string DropOffCity { get; set; } = "";
        public string DropOffState { get; set; } = "";
        public int Miles { get; set; }
        public int WeightLbs { get; set; }
        public string AssignedDriver { get; set; } = "";
        public string AssignedTruck { get; set; } = "";
        public string SourceMod { get; set; } = "";
        public string Status { get; set; } = "";
        public string SavePath { get; set; } = "";
        public string Message { get; set; } = "";
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public string RouteDisplay => $"{PickupCompany} / {PickupCity}, {PickupState} → {DropOffCompany} / {DropOffCity}, {DropOffState}";
        public string LoadDisplay => $"{LoadNumber} • {Cargo} • {Trailer}";
    }

    public static class IndividualLoadHistoryStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };

        private static string StorePath
        {
            get
            {
                var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OverWatchELD", "LoadHistory");
                Directory.CreateDirectory(root);
                return Path.Combine(root, "individual_load_history.json");
            }
        }

        public static List<IndividualLoadHistoryRecord> LoadAll()
        {
            try
            {
                if (!File.Exists(StorePath))
                    return new List<IndividualLoadHistoryRecord>();

                return JsonSerializer.Deserialize<List<IndividualLoadHistoryRecord>>(File.ReadAllText(StorePath), JsonOptions)
                       ?? new List<IndividualLoadHistoryRecord>();
            }
            catch
            {
                return new List<IndividualLoadHistoryRecord>();
            }
        }

        public static void SaveAll(IEnumerable<IndividualLoadHistoryRecord> records)
        {
            try
            {
                File.WriteAllText(StorePath, JsonSerializer.Serialize(records.ToList(), JsonOptions));
            }
            catch
            {
            }
        }

        public static void AddFromCreateLoad(
            string loadNumber,
            string cargo,
            string trailer,
            string pickupCompany,
            string pickupCity,
            string pickupState,
            string dropOffCompany,
            string dropOffCity,
            string dropOffState,
            int miles,
            int weightLbs,
            string assignedDriver,
            string assignedTruck,
            string sourceMod,
            string status)
        {
            Add(new IndividualLoadHistoryRecord
            {
                LoadNumber = loadNumber,
                Cargo = cargo,
                Trailer = trailer,
                PickupCompany = pickupCompany,
                PickupCity = pickupCity,
                PickupState = pickupState,
                DropOffCompany = dropOffCompany,
                DropOffCity = dropOffCity,
                DropOffState = dropOffState,
                Miles = miles,
                WeightLbs = weightLbs,
                AssignedDriver = assignedDriver,
                AssignedTruck = assignedTruck,
                SourceMod = sourceMod,
                Status = status,
                Message = "Created from Create Load window."
            });
        }

        public static void AddFromExportResult(
            string loadNumber,
            string cargo,
            string trailer,
            string pickupCompany,
            string pickupCity,
            string pickupState,
            string dropOffCompany,
            string dropOffCity,
            string dropOffState,
            int miles,
            int weightLbs,
            string assignedDriver,
            string assignedTruck,
            string sourceMod,
            string status,
            string savePath,
            string message)
        {
            Add(new IndividualLoadHistoryRecord
            {
                LoadNumber = loadNumber,
                Cargo = cargo,
                Trailer = trailer,
                PickupCompany = pickupCompany,
                PickupCity = pickupCity,
                PickupState = pickupState,
                DropOffCompany = dropOffCompany,
                DropOffCity = dropOffCity,
                DropOffState = dropOffState,
                Miles = miles,
                WeightLbs = weightLbs,
                AssignedDriver = assignedDriver,
                AssignedTruck = assignedTruck,
                SourceMod = sourceMod,
                Status = status,
                SavePath = savePath,
                Message = message
            });
        }

        public static void Add(IndividualLoadHistoryRecord record)
        {
            if (record == null)
                return;

            var all = LoadAll();

            if (string.IsNullOrWhiteSpace(record.Id))
                record.Id = Guid.NewGuid().ToString("N");

            if (record.CreatedUtc == default)
                record.CreatedUtc = DateTime.UtcNow;

            all.Add(record);
            SaveAll(all.OrderByDescending(x => x.CreatedUtc).Take(1000));
        }

        public static void Clear()
        {
            SaveAll(Array.Empty<IndividualLoadHistoryRecord>());
        }
    }
}
