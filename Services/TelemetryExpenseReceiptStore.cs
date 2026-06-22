using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public sealed class TelemetryExpenseReceipt
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string EventType { get; set; } = ""; // Fuel, Toll, Ticket
        public string DriverName { get; set; } = "";
        public string TruckName { get; set; } = "";
        public string TruckId { get; set; } = "";
        public string Location { get; set; } = "";
        public string City { get; set; } = "";
        public string State { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal Amount { get; set; }
        public double? FuelGallonsAdded { get; set; }
        public double? FuelPercent { get; set; }
        public double? OdometerMiles { get; set; }
        public string RawDetails { get; set; } = "";
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
        public bool DiscordPosted { get; set; }

        public string CreatedLocalDisplay => CreatedUtc.LocalDateTime.ToString("MM/dd/yyyy h:mm tt");
        public string AmountDisplay => Amount == 0 ? "--" : Amount.ToString("C");
        public string DriverTruckDisplay => $"{DriverName} • {TruckName}".Trim(' ', '•');
        public string ReceiptNumber => $"{EventType.ToUpperInvariant()}-{CreatedUtc:yyyyMMddHHmmss}";
    }

    public static class TelemetryExpenseReceiptStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        private static string StorePath
        {
            get
            {
                var root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "OverWatchELD",
                    "Receipts");

                Directory.CreateDirectory(root);
                return Path.Combine(root, "telemetry_receipts.json");
            }
        }

        public static List<TelemetryExpenseReceipt> LoadAll()
        {
            try
            {
                if (!File.Exists(StorePath))
                    return new List<TelemetryExpenseReceipt>();

                return JsonSerializer.Deserialize<List<TelemetryExpenseReceipt>>(
                           File.ReadAllText(StorePath),
                           JsonOptions)
                       ?? new List<TelemetryExpenseReceipt>();
            }
            catch
            {
                return new List<TelemetryExpenseReceipt>();
            }
        }

        public static void SaveAll(IEnumerable<TelemetryExpenseReceipt> receipts)
        {
            try
            {
                File.WriteAllText(StorePath, JsonSerializer.Serialize(receipts.ToList(), JsonOptions));
            }
            catch
            {
            }
        }

        public static void Add(TelemetryExpenseReceipt receipt)
        {
            if (receipt == null)
                return;

            var all = LoadAll();

            if (string.IsNullOrWhiteSpace(receipt.Id))
                receipt.Id = Guid.NewGuid().ToString("N");

            if (receipt.CreatedUtc == default)
                receipt.CreatedUtc = DateTimeOffset.UtcNow;

            all.Add(receipt);

            SaveAll(all
                .OrderByDescending(x => x.CreatedUtc)
                .Take(2000));
        }

        public static void Clear()
        {
            SaveAll(Array.Empty<TelemetryExpenseReceipt>());
        }
    }
}
