using OverWatchELD.Models.Events;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OverWatchELD.Services.Discord;

namespace OverWatchELD.Services.Events
{
    public sealed class VtcEventStore
    {
        private static readonly JsonSerializerOptions JsonReadOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly JsonSerializerOptions JsonWriteOpts = new()
        {
            WriteIndented = true
        };

        private readonly string _path;
        private readonly object _lock = new();

        public VtcEventStore()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OverWatchELD");

            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "events.json");
        }

        public List<VtcEventItem> LoadAll()
        {
            lock (_lock)
            {
                try
                {
                    if (!File.Exists(_path))
                        return new List<VtcEventItem>();

                    var json = File.ReadAllText(_path);
                    var items = JsonSerializer.Deserialize<List<VtcEventItem>>(json, JsonReadOpts);
                    return items ?? new List<VtcEventItem>();
                }
                catch
                {
                    return new List<VtcEventItem>();
                }
            }
        }

        public VtcEventItem? GetById(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;

            return LoadAll().FirstOrDefault(x =>
                string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public void Save(VtcEventItem item)
        {
            if (item == null)
                return;

            lock (_lock)
            {
                var all = LoadAll();
                var existing = all.FirstOrDefault(x =>
                    string.Equals(x.Id, item.Id, StringComparison.OrdinalIgnoreCase));

                var isNew = existing == null;

                if (existing == null)
                {
                    item.CreatedUtc = item.CreatedUtc == default ? DateTimeOffset.UtcNow : item.CreatedUtc;
                    item.UpdatedUtc = DateTimeOffset.UtcNow;
                    all.Add(item);
                }
                else
                {
                    existing.Title = item.Title;
                    existing.EventType = item.EventType;
                    existing.EventDate = item.EventDate;
                    existing.TimeDisplay = item.TimeDisplay;
                    existing.Location = item.Location;
                    existing.Host = item.Host;
                    existing.Notes = item.Notes;
                    existing.AttendeeCount = item.AttendeeCount;
                    existing.UpdatedUtc = DateTimeOffset.UtcNow;
                }

                Persist(all);

                DiscordNotificationPushService.PushFireAndForget(
                    "Events",
                    isNew ? "Event Created" : "Event Updated",
                    $"{item.Title} • {item.EventDate:MMM d, yyyy} {item.TimeDisplay}",
                    $"Location: {item.Location}\nHost: {item.Host}");
            }
        }

        public void Delete(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return;

            lock (_lock)
            {
                var all = LoadAll();
                var removed = all.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
                all.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
                Persist(all);

                if (removed != null)
                {
                    DiscordNotificationPushService.PushFireAndForget(
                        "Events",
                        "Event Deleted",
                        $"{removed.Title} was removed from the VTC event board.",
                        $"Date: {removed.EventDate:MMM d, yyyy} {removed.TimeDisplay}");
                }
            }
        }

        private void Persist(List<VtcEventItem> items)
        {
            try
            {
                var json = JsonSerializer.Serialize(items, JsonWriteOpts);
                File.WriteAllText(_path, json);
            }
            catch
            {
            }
        }
    }
}