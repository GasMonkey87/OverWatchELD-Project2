using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services.LoadBoard
{
    public sealed class LoadBoardLoad
    {
        public string LoadNumber { get; set; } = "";
        public string Status { get; set; } = "Available"; // At Shipper / BOL Complete / In Transit / Delivered
        public string DriverName { get; set; } = "";
        public string DriverDiscordId { get; set; } = "";
        public string TruckNumber { get; set; } = "";
        public string TruckName { get; set; } = "";
        public string TrailerNumber { get; set; } = "";
        public string TrailerName { get; set; } = "";
        public string Commodity { get; set; } = "";
        public double WeightLbs { get; set; }
        public decimal RevenueUsd { get; set; }
        public string RevenueSource { get; set; } = "";
        public DateTimeOffset? RevenueCapturedUtc { get; set; }
        public string RevenueDisplay => RevenueUsd <= 0 ? "$0.00" : RevenueUsd.ToString("C2");
        public string ShipperName { get; set; } = "";
        public string ShipperCity { get; set; } = "";
        public string ReceiverName { get; set; } = "";
        public string ReceiverCity { get; set; } = "";
        public string CurrentLocation { get; set; } = "";
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? AtShipperUtc { get; set; }
        public DateTimeOffset? PickupDiscordSentUtc { get; set; }
        public DateTimeOffset? BolCompletedUtc { get; set; }
        public DateTimeOffset? InTransitUtc { get; set; }
        public DateTimeOffset? DeliveredUtc { get; set; }
        public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    public static class LoadBoardStore
    {
        private static readonly object Gate = new();
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private static string BaseDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OverWatchELD");
        private static string StorePath => Path.Combine(BaseDir, "load_board.json");

        public static List<LoadBoardLoad> LoadAll()
        {
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(BaseDir);
                    if (!File.Exists(StorePath))
                        return new List<LoadBoardLoad>();

                    var json = File.ReadAllText(StorePath);
                    return JsonSerializer.Deserialize<List<LoadBoardLoad>>(json, JsonOptions) ?? new List<LoadBoardLoad>();
                }
                catch
                {
                    return new List<LoadBoardLoad>();
                }
            }
        }

        public static LoadBoardLoad? GetActiveForDriver(string? driverDiscordId, string? driverName)
        {
            var id = (driverDiscordId ?? "").Trim();
            var name = (driverName ?? "").Trim();

            return LoadAll()
                .Where(x => !Same(x.Status, "Delivered"))
                .OrderByDescending(x => x.UpdatedUtc)
                .FirstOrDefault(x =>
                    (!string.IsNullOrWhiteSpace(id) && Same(x.DriverDiscordId, id)) ||
                    (!string.IsNullOrWhiteSpace(name) && Same(x.DriverName, name)));
        }

        public static void Upsert(LoadBoardLoad load)
        {
            if (load == null) return;
            if (string.IsNullOrWhiteSpace(load.LoadNumber))
                load.LoadNumber = GenerateLoadNumber();

            load.LoadNumber = load.LoadNumber.Trim();
            load.UpdatedUtc = DateTimeOffset.UtcNow;

            lock (Gate)
            {
                var all = LoadAllUnlocked();
                var idx = all.FindIndex(x => Same(x.LoadNumber, load.LoadNumber));
                if (idx >= 0) all[idx] = load;
                else all.Add(load);
                SaveUnlocked(all);
            }

            try
            {
                LoadBoardPortalSyncService.Shared.QueueSync(load);
            }
            catch
            {
            }
        }

        public static string GenerateLoadNumber()
        {
            var day = DateTime.Now.ToString("yyyyMMdd");
            var all = LoadAll();
            var max = 0;
            foreach (var l in all)
            {
                if (l.LoadNumber != null && l.LoadNumber.StartsWith(day, StringComparison.OrdinalIgnoreCase))
                {
                    var tail = l.LoadNumber.Length > 8 ? l.LoadNumber.Substring(8) : "";
                    if (int.TryParse(tail, out var n) && n > max)
                        max = n;
                }
            }
            return $"{day}{max + 1:0000}";
        }

        private static List<LoadBoardLoad> LoadAllUnlocked()
        {
            try
            {
                Directory.CreateDirectory(BaseDir);
                if (!File.Exists(StorePath)) return new List<LoadBoardLoad>();
                return JsonSerializer.Deserialize<List<LoadBoardLoad>>(File.ReadAllText(StorePath), JsonOptions) ?? new List<LoadBoardLoad>();
            }
            catch { return new List<LoadBoardLoad>(); }
        }

        private static void SaveUnlocked(List<LoadBoardLoad> all)
        {
            Directory.CreateDirectory(BaseDir);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(all.OrderByDescending(x => x.UpdatedUtc).ToList(), JsonOptions));
        }

        private static bool Same(string? a, string? b)
            => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
