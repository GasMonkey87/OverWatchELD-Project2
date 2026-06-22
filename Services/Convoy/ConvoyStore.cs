using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OverWatchELD.Models.Convoy;
using OverWatchELD.Services.Discord;

namespace OverWatchELD.Services.Convoy
{
    public sealed class ConvoyStore
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

        public ConvoyStore()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OverWatchELD");

            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "convoys.json");
        }

        public List<ConvoyEvent> LoadAll()
        {
            lock (_lock)
            {
                try
                {
                    if (!File.Exists(_path))
                        return new List<ConvoyEvent>();

                    var json = File.ReadAllText(_path);
                    var items = JsonSerializer.Deserialize<List<ConvoyEvent>>(json, JsonReadOpts);
                    return items ?? new List<ConvoyEvent>();
                }
                catch
                {
                    return new List<ConvoyEvent>();
                }
            }
        }

        public ConvoyEvent? GetLatest()
        {
            return LoadAll()
                .OrderByDescending(x => x.UpdatedUtc)
                .ThenByDescending(x => x.CreatedUtc)
                .FirstOrDefault();
        }

        public ConvoyEvent? GetById(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;

            return LoadAll().FirstOrDefault(x => x.Id == id);
        }

        public void Save(ConvoyEvent convoy)
        {
            if (convoy == null) return;

            lock (_lock)
            {
                var all = LoadAll();

                var existing = all.FirstOrDefault(x => x.Id == convoy.Id);
                var isNew = existing == null;
                if (existing == null)
                {
                    convoy.CreatedUtc = convoy.CreatedUtc == default ? DateTimeOffset.UtcNow : convoy.CreatedUtc;
                    convoy.UpdatedUtc = DateTimeOffset.UtcNow;
                    all.Add(convoy);
                }
                else
                {
                    existing.Title = convoy.Title;
                    existing.StartLocation = convoy.StartLocation;
                    existing.Destination = convoy.Destination;
                    existing.DateDisplay = convoy.DateDisplay;
                    existing.TimeDisplay = convoy.TimeDisplay;
                    existing.MeetTime = convoy.MeetTime;
                    existing.DepartureTime = convoy.DepartureTime;
                    existing.Server = convoy.Server;
                    existing.LeadDriver = convoy.LeadDriver;
                    existing.Status = convoy.Status;
                    existing.Notes = convoy.Notes;
                    existing.Attendees = convoy.Attendees ?? new List<ConvoyAttendee>();
                    existing.UpdatedUtc = DateTimeOffset.UtcNow;
                }

                Persist(all);

                DiscordNotificationPushService.PushFireAndForget(
                    "Convoys",
                    isNew ? "Convoy Created" : "Convoy Updated",
                    $"{convoy.Title} • {convoy.DateDisplay} {convoy.TimeDisplay}",
                    $"Route: {convoy.StartLocation} → {convoy.Destination}\nLead: {convoy.LeadDriver}\nServer: {convoy.Server}");
            }
        }

        public void SaveAll(List<ConvoyEvent> items)
        {
            lock (_lock)
            {
                Persist(items ?? new List<ConvoyEvent>());
            }
        }

        public void ReplaceAttendees(string convoyId, List<ConvoyAttendee> attendees)
        {
            if (string.IsNullOrWhiteSpace(convoyId)) return;

            lock (_lock)
            {
                var all = LoadAll();
                var existing = all.FirstOrDefault(x => x.Id == convoyId);
                if (existing == null) return;

                existing.Attendees = attendees ?? new List<ConvoyAttendee>();
                existing.UpdatedUtc = DateTimeOffset.UtcNow;

                Persist(all);

                DiscordNotificationPushService.PushFireAndForget(
                    "Convoys",
                    "Convoy Attendance Updated",
                    $"{existing.Title} attendance changed.",
                    $"Attendees: {existing.Attendees?.Count ?? 0}");
            }
        }

        private void Persist(List<ConvoyEvent> items)
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