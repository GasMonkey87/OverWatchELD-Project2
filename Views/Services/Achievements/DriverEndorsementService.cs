using OverWatchELD.Models.Achievements;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using OverWatchELD.Services;

namespace OverWatchELD.Services.Achievements
{
    public static class DriverEndorsementService
    {
        private static readonly object Gate = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private static string StorePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "OverWatchELD",
                "driver_endorsements.json");

        public static List<DriverEndorsementRecord> LoadAll(bool includeInactive = false)
        {
            lock (Gate)
            {
                try
                {
                    if (!File.Exists(StorePath))
                        return new List<DriverEndorsementRecord>();

                    var json = File.ReadAllText(StorePath);
                    var rows = JsonSerializer.Deserialize<List<DriverEndorsementRecord>>(json, JsonOptions)
                               ?? new List<DriverEndorsementRecord>();

                    return rows
                        .Where(x => includeInactive || x.IsActive)
                        .OrderByDescending(x => x.CreatedUtc)
                        .ToList();
                }
                catch
                {
                    return new List<DriverEndorsementRecord>();
                }
            }
        }

        public static List<DriverEndorsementRecord> ForDriver(string? driverName, string? driverDiscordId = null)
        {
            var name = Normalize(driverName);
            var id = Normalize(driverDiscordId);

            return LoadAll()
                .Where(x =>
                    (!string.IsNullOrWhiteSpace(id) && Normalize(x.DriverDiscordId) == id) ||
                    (!string.IsNullOrWhiteSpace(name) && Normalize(x.DriverName) == name))
                .OrderByDescending(x => x.CreatedUtc)
                .ToList();
        }

        public static DriverEndorsementRecord Add(
            string? driverName,
            string? driverDiscordId,
            string? title,
            string? icon,
            string? notes,
            string? createdBy)
        {
            lock (Gate)
            {
                var rows = LoadAll(includeInactive: true);

                var record = new DriverEndorsementRecord
                {
                    Id = Guid.NewGuid().ToString("N"),
                    DriverName = (driverName ?? "").Trim(),
                    DriverDiscordId = (driverDiscordId ?? "").Trim(),
                    Title = string.IsNullOrWhiteSpace(title) ? "Endorsed Driver" : title.Trim(),
                    Icon = string.IsNullOrWhiteSpace(icon) ? "⭐" : icon.Trim(),
                    Notes = (notes ?? "").Trim(),
                    CreatedBy = string.IsNullOrWhiteSpace(createdBy) ? EldDriverIdentityResolver.DriverName() : createdBy.Trim(),
                    CreatedUtc = DateTime.UtcNow,
                    IsActive = true
                };

                rows.Add(record);
                Save(rows);

                DriverProfileMasterStore.AddEndorsement(
                    record.DriverDiscordId,
                    record.DriverName,
                    record.DriverName,
                    string.IsNullOrWhiteSpace(record.Title)
                        ? record.Notes
                        : $"{record.Icon} {record.Title}".Trim());

                return record;
            }
        }

        public static void Remove(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return;

            lock (Gate)
            {
                var rows = LoadAll(includeInactive: true);

                foreach (var row in rows.Where(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)))
                    row.IsActive = false;

                Save(rows);
            }
        }

        public static void DeletePermanent(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return;

            lock (Gate)
            {
                var rows = LoadAll(includeInactive: true)
                    .Where(x => !string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                Save(rows);
            }
        }

        private static void Save(List<DriverEndorsementRecord> rows)
        {
            try
            {
                var dir = Path.GetDirectoryName(StorePath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(StorePath, JsonSerializer.Serialize(rows, JsonOptions));
            }
            catch
            {
            }
        }

        private static string Normalize(string? value)
        {
            return (value ?? "")
                .Trim()
                .ToLowerInvariant();
        }
    }
}
