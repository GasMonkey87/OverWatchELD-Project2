using OverWatchELD.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services.ATS
{
    public sealed class CompanyLoadRequest
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string LoadNumber { get; set; } = "";
        public string PickupCompany { get; set; } = "";
        public string PickupCity { get; set; } = "";
        public string PickupState { get; set; } = "";
        public string DropOffCompany { get; set; } = "";
        public string DropOffCity { get; set; } = "";
        public string DropOffState { get; set; } = "";
        public string Cargo { get; set; } = "";
        public string Trailer { get; set; } = "";
        public int WeightLbs { get; set; }
        public int Miles { get; set; }
        public string AssignedDriver { get; set; } = "";
        public string AssignedTruck { get; set; } = "";
        public string Status { get; set; } = "Pending";
        public string CreatedBy { get; set; } = "";
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public string Notes { get; set; } = "";

        public string RouteDisplay => $"{PickupCompany} / {PickupCity}, {PickupState} → {DropOffCompany} / {DropOffCity}, {DropOffState}";
        public string LoadDisplay => $"{LoadNumber} • {Cargo} • {Trailer}";
    }

    public static class CompanyLoadRequestStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };

        private static string StorePath
        {
            get
            {
                var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OverWatchELD", "CompanyLoads");
                Directory.CreateDirectory(root);
                return Path.Combine(root, "company_loads.json");
            }
        }

        public static List<CompanyLoadRequest> LoadAll()
        {
            try
            {
                if (!File.Exists(StorePath)) return new List<CompanyLoadRequest>();
                return JsonSerializer.Deserialize<List<CompanyLoadRequest>>(File.ReadAllText(StorePath), JsonOptions) ?? new List<CompanyLoadRequest>();
            }
            catch { return new List<CompanyLoadRequest>(); }
        }

        public static void SaveAll(IEnumerable<CompanyLoadRequest> loads)
        {
            try { File.WriteAllText(StorePath, JsonSerializer.Serialize(loads.ToList(), JsonOptions)); }
            catch { }
        }

        public static void AddOrUpdate(CompanyLoadRequest load)
        {
            if (load == null) return;
            var all = LoadAll();
            var index = all.FindIndex(x => string.Equals(x.Id, load.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) all[index] = load; else all.Add(load);
            SaveAll(all.OrderByDescending(x => x.CreatedUtc));
        }

        public static void Delete(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            var all = LoadAll();
            all.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            SaveAll(all);
        }

        public static DispatchJob ToDispatchJob(CompanyLoadRequest request)
        {
            var miles = request.Miles > 0 ? request.Miles : 500;
            var weight = request.WeightLbs > 0 ? request.WeightLbs : 42000;
            var payout = Math.Max(500, Math.Round((miles * 3.35m) + 250, 0));

            return new DispatchJob
            {
                Id = Guid.NewGuid().ToString("N"),
                LoadNumber = string.IsNullOrWhiteSpace(request.LoadNumber) ? DispatchService.NextLoadNumber() : request.LoadNumber.Trim(),
                Company = request.PickupCompany,
                OriginCity = request.PickupCity,
                OriginState = request.PickupState,
                DestinationCity = request.DropOffCity,
                DestinationState = request.DropOffState,
                Miles = miles,
                Cargo = request.Cargo,
                Trailer = request.Trailer,
                AssignedDriver = string.IsNullOrWhiteSpace(request.AssignedDriver) ? "Unassigned" : request.AssignedDriver,
                AssignedTruck = string.IsNullOrWhiteSpace(request.AssignedTruck) ? "Any" : request.AssignedTruck,
                Status = "Company Load",
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow,
                PostedUtc = DateTime.UtcNow,
                PickupDate = DateTime.Now.AddMinutes(15),
                DeliveryDeadline = DateTime.Now.AddHours(Math.Max(6, miles / 45.0 + 8.0)),
                ActualCargoWeightLbs = weight,
                CargoWeight = weight,
                Payout = payout,
                RevenueUsd = payout,
                Notes = "Company load created by management in OverWatch ELD. " + request.Notes
            };
        }
    }
}
