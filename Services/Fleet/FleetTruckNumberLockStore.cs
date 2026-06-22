using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services.Fleet
{
    public sealed class LockedFleetTruckNumber
    {
        public string TruckNumber { get; set; } = "";
        public string PendingApprovalId { get; set; } = "";
        public string TruckName { get; set; } = "";
        public string AssignedDriver { get; set; } = "";
        public string ApprovedBy { get; set; } = "";
        public DateTime ApprovedUtc { get; set; } = DateTime.UtcNow;
        public string Notes { get; set; } = "";
    }

    public static class FleetTruckNumberLockStore
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
                    "Fleet");

                Directory.CreateDirectory(folder);
                return folder;
            }
        }

        private static string FilePath => Path.Combine(Folder, "locked_fleet_truck_numbers.json");

        public static List<LockedFleetTruckNumber> Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new List<LockedFleetTruckNumber>();

                return JsonSerializer.Deserialize<List<LockedFleetTruckNumber>>(
                           File.ReadAllText(FilePath),
                           JsonOptions)
                       ?? new List<LockedFleetTruckNumber>();
            }
            catch
            {
                return new List<LockedFleetTruckNumber>();
            }
        }

        public static void Save(List<LockedFleetTruckNumber> rows)
        {
            try
            {
                rows = rows
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.TruckNumber))
                    .GroupBy(x => Normalize(x.TruckNumber), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .OrderBy(x => Normalize(x.TruckNumber))
                    .ToList();

                File.WriteAllText(FilePath, JsonSerializer.Serialize(rows, JsonOptions));
            }
            catch
            {
            }
        }

        public static bool IsLocked(string truckNumber, string? exceptPendingApprovalId = null)
        {
            var number = Normalize(truckNumber);

            if (string.IsNullOrWhiteSpace(number))
                return false;

            return Load().Any(x =>
                string.Equals(Normalize(x.TruckNumber), number, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(x.PendingApprovalId ?? "", exceptPendingApprovalId ?? "", StringComparison.OrdinalIgnoreCase));
        }

        public static void LockNumber(
            string truckNumber,
            string pendingApprovalId,
            string truckName,
            string assignedDriver,
            string approvedBy,
            string notes = "")
        {
            var number = Normalize(truckNumber);

            if (string.IsNullOrWhiteSpace(number))
                throw new InvalidOperationException("Truck number is required.");

            var rows = Load();

            if (rows.Any(x =>
                    string.Equals(Normalize(x.TruckNumber), number, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(x.PendingApprovalId ?? "", pendingApprovalId ?? "", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Truck number {number} is already locked/assigned and cannot be reused.");
            }

            rows.RemoveAll(x => string.Equals(x.PendingApprovalId ?? "", pendingApprovalId ?? "", StringComparison.OrdinalIgnoreCase));

            rows.Add(new LockedFleetTruckNumber
            {
                TruckNumber = number,
                PendingApprovalId = pendingApprovalId ?? "",
                TruckName = truckName ?? "",
                AssignedDriver = assignedDriver ?? "",
                ApprovedBy = approvedBy ?? "",
                ApprovedUtc = DateTime.UtcNow,
                Notes = notes ?? ""
            });

            Save(rows);
        }

        public static string Normalize(string? truckNumber)
        {
            return (truckNumber ?? "").Trim().ToUpperInvariant();
        }
    }
}
