using OverWatchELD.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Stores
{
    public sealed class MaintenanceRequestTicketStore
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
                    "Maintenance");

                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private static string FilePath => Path.Combine(Folder, "maintenance_requests.json");

        public List<MaintenanceRequestTicket> LoadAll()
        {
            lock (Gate)
            {
                return LoadAllUnlocked();
            }
        }

        public MaintenanceRequestTicket Add(MaintenanceRequestTicket ticket)
        {
            lock (Gate)
            {
                var list = LoadAllUnlocked();

                if (string.IsNullOrWhiteSpace(ticket.Id))
                    ticket.Id = Guid.NewGuid().ToString("N");

                if (string.IsNullOrWhiteSpace(ticket.RequestNumber))
                    ticket.RequestNumber = NextRequestNumber(list);

                if (ticket.CreatedUtc == default)
                    ticket.CreatedUtc = DateTime.UtcNow;

                if (string.IsNullOrWhiteSpace(ticket.Status))
                    ticket.Status = "Open";

                list.RemoveAll(x => Same(x.Id, ticket.Id) || Same(x.RequestNumber, ticket.RequestNumber));
                list.Add(ticket);

                SaveAllUnlocked(list);
                return ticket;
            }
        }

        public void Update(MaintenanceRequestTicket ticket)
        {
            lock (Gate)
            {
                var list = LoadAllUnlocked();

                if (string.IsNullOrWhiteSpace(ticket.Id))
                    ticket.Id = Guid.NewGuid().ToString("N");

                if (string.IsNullOrWhiteSpace(ticket.RequestNumber))
                    ticket.RequestNumber = NextRequestNumber(list);

                list.RemoveAll(x => Same(x.Id, ticket.Id) || Same(x.RequestNumber, ticket.RequestNumber));
                list.Add(ticket);

                SaveAllUnlocked(list);
            }
        }

        private static List<MaintenanceRequestTicket> LoadAllUnlocked()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new List<MaintenanceRequestTicket>();

                var json = File.ReadAllText(FilePath);

                if (string.IsNullOrWhiteSpace(json))
                    return new List<MaintenanceRequestTicket>();

                return JsonSerializer.Deserialize<List<MaintenanceRequestTicket>>(json, JsonOptions)
                       ?? new List<MaintenanceRequestTicket>();
            }
            catch
            {
                return new List<MaintenanceRequestTicket>();
            }
        }

        private static void SaveAllUnlocked(List<MaintenanceRequestTicket> list)
        {
            Directory.CreateDirectory(Folder);

            var ordered = list
                .OrderByDescending(x => string.Equals(x.Status, "Open", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(x => x.CreatedUtc)
                .ToList();

            File.WriteAllText(FilePath, JsonSerializer.Serialize(ordered, JsonOptions));
        }

        private static string NextRequestNumber(List<MaintenanceRequestTicket> existing)
        {
            var today = DateTime.UtcNow.ToString("yyyyMMdd");
            var prefix = $"MR-{today}-";
            var max = 0;

            foreach (var item in existing)
            {
                var rn = item.RequestNumber ?? "";

                if (!rn.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var tail = rn.Substring(prefix.Length);

                if (int.TryParse(tail, out var n) && n > max)
                    max = n;
            }

            return prefix + (max + 1).ToString("000");
        }

        private static bool Same(string? a, string? b) =>
            !string.IsNullOrWhiteSpace(a) &&
            !string.IsNullOrWhiteSpace(b) &&
            string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}