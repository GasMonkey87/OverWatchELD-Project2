using OverWatchELD.Models.Events;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services.Events
{
    public sealed class EventStore
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

        public EventStore()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OverWatchELD");

            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "events.json");
        }

        public List<EventItem> LoadAll()
        {
            lock (_lock)
            {
                try
                {
                    if (!File.Exists(_path))
                        return new List<EventItem>();

                    var json = File.ReadAllText(_path);
                    var items = JsonSerializer.Deserialize<List<EventItem>>(json, JsonReadOpts);
                    return items ?? new List<EventItem>();
                }
                catch
                {
                    return new List<EventItem>();
                }
            }
        }

        public EventItem? GetById(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;

            return LoadAll().FirstOrDefault(x => x.Id == id);
        }

        public List<EventItem> GetUpcoming()
        {
            var today = DateTime.Today;
            return LoadAll()
                .Where(x => x.EventDate.Date >= today)
                .OrderBy(x => x.EventDate)
                .ThenBy(x => x.TimeDisplay)
                .ToList();
        }

        public void Save(EventItem item)
        {
            if (item == null) return;

            bool isNew = false;

            lock (_lock)
            {
                var all = LoadAll();
                var existing = all.FirstOrDefault(x => x.Id == item.Id);

                if (existing == null)
                {
                    isNew = true;

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
                    existing.Status = item.Status;
                    existing.Attendees = item.Attendees ?? new List<EventAttendee>();
                    existing.UpdatedUtc = DateTimeOffset.UtcNow;
                }

                Persist(all);
            }

            // ✅ FIRE ANNOUNCEMENT ONLY IF NEW EVENT
            if (isNew)
            {
                _ = EventAnnouncementService.FireAsync(item);
            }
        }

        public void SaveAll(List<EventItem> items)
        {
            lock (_lock)
            {
                Persist(items ?? new List<EventItem>());
            }
        }

        private void Persist(List<EventItem> items)
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