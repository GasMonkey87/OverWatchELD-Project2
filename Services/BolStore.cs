// Services/BolStore.cs
// FULL COPY/REPLACE
// Persists BOL drafts keyed by LoadNumber + per-date counters for yyyymmddxxxx generation.
// Includes recent/search list support and load pay fields from dispatch/telemetry.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public static class BolStore
    {
        public sealed class BolRecord
        {
            public string LoadNumber { get; set; } = "";
            public string Date { get; set; } = "";
            public string Time { get; set; } = "";

            public string Truck { get; set; } = "";
            public string LicensePlate { get; set; } = "";

            public string Commodity { get; set; } = "";
            public string Mileage { get; set; } = "";
            public string WeightLbs { get; set; } = "";

            public string CityOrigin { get; set; } = "";
            public string CityDestination { get; set; } = "";

            public string CompanyPickup { get; set; } = "";
            public string CompanyDropoff { get; set; } = "";

            public string Notes { get; set; } = "";

            // Driver ownership. Drivers only see their own BOLs; Owner/Admin/Dispatcher sees all.
            // DriverId is the stable permission key. It should be discord:<id> when Discord is linked;
            // older name/username fields stay only as display/backfill data.
            public string DriverId { get; set; } = "";
            public string DriverDiscordUserId { get; set; } = "";
            public string DriverDiscordName { get; set; } = "";
            public string DriverName { get; set; } = "";

            // Load pay / dispatch revenue fields
            public decimal RevenueUsd { get; set; }
            public decimal Payout { get; set; }
            public decimal RatePerMile { get; set; }

            public string RevenueDisplay =>
                RevenueUsd > 0
                    ? RevenueUsd.ToString("C2")
                    : Payout > 0
                        ? Payout.ToString("C2")
                        : "--";

            public string RatePerMileDisplay =>
                RatePerMile > 0
                    ? RatePerMile.ToString("C2") + "/mi"
                    : "--";

            public DateTimeOffset SavedUtc { get; set; }
        }

        public sealed class BolListItem
        {
            public string LoadNumber { get; set; } = "";
            public DateTimeOffset SavedUtc { get; set; }
            public string Display { get; set; } = "";
        }

        private sealed class StoreFile
        {
            public Dictionary<string, BolRecord> Records { get; set; } = new();
            public Dictionary<string, int> DateCounters { get; set; } = new();
        }

        private static readonly object _lock = new();

        private static readonly JsonSerializerOptions _json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        private static string BaseDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OverWatchELD");

        private static string StorePath => Path.Combine(BaseDir, "bol_store.json");

        public static string GenerateNextLoadNumber()
        {
            lock (_lock)
            {
                var store = Load();

                var day = DateTime.Now.ToString("yyyyMMdd");
                store.DateCounters.TryGetValue(day, out var last);

                var next = last + 1;
                if (next > 9999)
                    throw new InvalidOperationException($"Load # counter exceeded 9999 for {day}.");

                string candidate;
                do
                {
                    candidate = $"{day}{next:0000}";
                    next++;

                    if (next > 10000)
                        throw new InvalidOperationException($"Unable to generate a unique Load # for {day}.");
                }
                while (store.Records.ContainsKey(candidate));

                store.DateCounters[day] = next - 1;
                Save(store);

                return candidate;
            }
        }

        public static void Upsert(BolRecord record)
        {
            if (record == null)
                return;

            if (string.IsNullOrWhiteSpace(record.LoadNumber))
                return;

            lock (_lock)
            {
                var store = Load();

                record.LoadNumber = record.LoadNumber.Trim();
                BolAccessService.StampCurrentDriver(record);

                if (record.SavedUtc == default)
                    record.SavedUtc = DateTimeOffset.UtcNow;

                if (record.RatePerMile <= 0)
                {
                    var miles = ParseDecimal(record.Mileage);
                    var pay = record.RevenueUsd > 0 ? record.RevenueUsd : record.Payout;

                    if (miles > 0 && pay > 0)
                        record.RatePerMile = Math.Round(pay / miles, 2);
                }

                store.Records[record.LoadNumber] = record;
                Save(store);
            }
        }

        public static BolRecord? TryGet(string loadNumber)
        {
            if (string.IsNullOrWhiteSpace(loadNumber))
                return null;

            lock (_lock)
            {
                var store = Load();
                store.Records.TryGetValue(loadNumber.Trim(), out var rec);
                return rec;
            }
        }



        public static BolRecord? TryGetForCurrentUser(string loadNumber)
        {
            var rec = TryGet(loadNumber);
            return BolAccessService.CanCurrentUserAccess(rec) ? rec : null;
        }

        public static void UpsertPay(
            string? loadNumber,
            decimal revenueUsd,
            decimal payout = 0,
            decimal ratePerMile = 0)
        {
            if (string.IsNullOrWhiteSpace(loadNumber))
                return;

            if (revenueUsd <= 0 && payout <= 0)
                return;

            lock (_lock)
            {
                var store = Load();
                var key = loadNumber.Trim();

                if (!store.Records.TryGetValue(key, out var rec))
                {
                    rec = new BolRecord
                    {
                        LoadNumber = key,
                        SavedUtc = DateTimeOffset.UtcNow
                    };
                }

                if (rec.RevenueUsd <= 0 && revenueUsd > 0)
                    rec.RevenueUsd = revenueUsd;

                if (rec.Payout <= 0 && payout > 0)
                    rec.Payout = payout;

                if (rec.RatePerMile <= 0 && ratePerMile > 0)
                    rec.RatePerMile = ratePerMile;

                rec.SavedUtc = DateTimeOffset.UtcNow;

                store.Records[key] = rec;
                Save(store);
            }
        }


        public static List<BolListItem> SearchRecentForCurrentUser(string query, int max = 50)
        {
            var canViewAll = BolAccessService.CanViewAllBols();
            var identity = DriverProfileIdentitySnapshot.Current();

            return SearchRecentInternal(query, max, record => canViewAll || BolAccessService.IsOwnBol(record, identity));
        }

        public static List<BolListItem> SearchRecent(string query, int max = 50)
        {
            return SearchRecentInternal(query, max, _ => true);
        }

        private static List<BolListItem> SearchRecentInternal(string query, int max, Func<BolRecord, bool> accessFilter)
        {
            query = (query ?? "").Trim();
            var q = query.ToLowerInvariant();

            lock (_lock)
            {
                var store = Load();

                IEnumerable<BolRecord> records = store.Records.Values.Where(accessFilter ?? (_ => true));

                if (!string.IsNullOrWhiteSpace(q))
                {
                    records = records.Where(r =>
                        Contains(r.LoadNumber, q) ||
                        Contains(r.Truck, q) ||
                        Contains(r.LicensePlate, q) ||
                        Contains(r.CityOrigin, q) ||
                        Contains(r.CityDestination, q) ||
                        Contains(r.CompanyPickup, q) ||
                        Contains(r.CompanyDropoff, q) ||
                        Contains(r.Commodity, q) ||
                        Contains(r.RevenueDisplay, q) ||
                        Contains(r.RatePerMileDisplay, q) ||
                        Contains(r.Notes, q));
                }

                return records
                    .OrderByDescending(r => r.SavedUtc)
                    .Take(Math.Max(1, max))
                    .Select(r => new BolListItem
                    {
                        LoadNumber = r.LoadNumber,
                        SavedUtc = r.SavedUtc,
                        Display = MakeDisplay(r)
                    })
                    .ToList();
            }

            static bool Contains(string? field, string q)
                => !string.IsNullOrWhiteSpace(field) && field.ToLowerInvariant().Contains(q);

            static string MakeDisplay(BolRecord r)
            {
                var when = r.SavedUtc.LocalDateTime.ToString("MM/dd HH:mm");
                var truck = string.IsNullOrWhiteSpace(r.Truck) ? "Truck: -" : $"Truck: {r.Truck}";
                var route = $"{BlankDash(r.CityOrigin)} → {BlankDash(r.CityDestination)}";
                var pay = r.RevenueUsd > 0 || r.Payout > 0 ? $"  •  Pay: {r.RevenueDisplay}" : "";
                var driver = string.IsNullOrWhiteSpace(r.DriverName) ? "" : $"  •  Driver: {r.DriverName.Trim()}";

                return $"{r.LoadNumber}  •  {when}  •  {truck}  •  {route}{pay}{driver}";
            }

            static string BlankDash(string? v) => string.IsNullOrWhiteSpace(v) ? "-" : v.Trim();
        }

        private static StoreFile Load()
        {
            try
            {
                Directory.CreateDirectory(BaseDir);

                if (!File.Exists(StorePath))
                    return new StoreFile();

                var json = File.ReadAllText(StorePath);
                var store = JsonSerializer.Deserialize<StoreFile>(json, _json);

                return store ?? new StoreFile();
            }
            catch
            {
                return new StoreFile();
            }
        }

        private static void Save(StoreFile store)
        {
            Directory.CreateDirectory(BaseDir);

            var json = JsonSerializer.Serialize(store ?? new StoreFile(), _json);
            File.WriteAllText(StorePath, json);
        }

        private static decimal ParseDecimal(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0;

            var cleaned = new string(value.Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());

            return decimal.TryParse(
                cleaned,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed)
                    ? parsed
                    : 0;
        }
    }
}
