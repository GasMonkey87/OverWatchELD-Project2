using OverWatchELD.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public sealed class DriverSubmissionService
    {
        private readonly string _filePath;

        public DriverSubmissionService()
        {
            _filePath = Path.Combine(AppContext.BaseDirectory, "driver_submissions.json");
        }

        public List<DriverSubmission> LoadAll()
        {
            try
            {
                if (!File.Exists(_filePath))
                    return new List<DriverSubmission>();

                var json = File.ReadAllText(_filePath);
                var items = JsonSerializer.Deserialize<List<DriverSubmission>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return items ?? new List<DriverSubmission>();
            }
            catch
            {
                return new List<DriverSubmission>();
            }
        }

        public void SaveAll(List<DriverSubmission> items)
        {
            var json = JsonSerializer.Serialize(items.OrderByDescending(x => x.DateUtc).ToList(),
                new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(_filePath, json);
        }

        public void Add(DriverSubmission item)
        {
            var items = LoadAll();
            items.Add(item);
            SaveAll(items);
        }

        public void Update(DriverSubmission item)
        {
            var items = LoadAll();
            var existing = items.FirstOrDefault(x => x.Id == item.Id);
            if (existing == null)
            {
                items.Add(item);
            }
            else
            {
                existing.DateUtc = item.DateUtc;
                existing.SubmissionType = item.SubmissionType;
                existing.Title = item.Title;
                existing.Amount = item.Amount;
                existing.TruckName = item.TruckName;
                existing.DiscordUserId = item.DiscordUserId;
                existing.DiscordUsername = item.DiscordUsername;
                existing.Details = item.Details;
                existing.IsApproved = item.IsApproved;
            }

            SaveAll(items);
        }

        public void Remove(string id)
        {
            var items = LoadAll();
            items.RemoveAll(x => x.Id == id);
            SaveAll(items);
        }
    }
}
