using OverWatchELD.Models.Fleet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services.Fleet
{
    public sealed class FleetTrailerStore
    {
        private static readonly JsonSerializerOptions ReadOpts = new() { PropertyNameCaseInsensitive = true };
        private static readonly JsonSerializerOptions WriteOpts = new() { WriteIndented = true };
        private readonly string _path;

        public FleetTrailerStore()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OverWatchELD");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "fleet_command_trailers.json");
        }

        public List<FleetCommandTrailer> LoadAll()
        {
            try
            {
                if (!File.Exists(_path))
                    return new List<FleetCommandTrailer>();

                var json = File.ReadAllText(_path);
                var items = JsonSerializer.Deserialize<List<FleetCommandTrailer>>(json, ReadOpts) ?? new List<FleetCommandTrailer>();
                return items
                    .Where(x => x != null)
                    .OrderBy(x => ParseTrailerNumberForSort(x.TrailerNumber))
                    .ThenBy(x => x.TrailerNumber, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.PlateNumber, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch
            {
                return new List<FleetCommandTrailer>();
            }
        }

        public FleetCommandTrailer? GetById(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            return LoadAll().FirstOrDefault(x => string.Equals(x.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public FleetCommandTrailer? GetByTrailerNumber(string? trailerNumber)
        {
            if (string.IsNullOrWhiteSpace(trailerNumber)) return null;
            var key = trailerNumber.Trim();
            return LoadAll().FirstOrDefault(x => string.Equals((x.TrailerNumber ?? "").Trim(), key, StringComparison.OrdinalIgnoreCase));
        }

        public void Save(FleetCommandTrailer trailer)
        {
            if (trailer == null) return;

            if (string.IsNullOrWhiteSpace(trailer.Id))
                trailer.Id = Guid.NewGuid().ToString("N");

            trailer.UpdatedUtc = DateTimeOffset.UtcNow;

            var all = LoadAll();
            var index = all.FindIndex(x => string.Equals(x.Id, trailer.Id, StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
                all[index] = trailer;
            else
                all.Add(trailer);

            SaveAll(all);
        }

        public void Delete(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            var all = LoadAll();
            all.RemoveAll(x => string.Equals(x.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
            SaveAll(all);
        }

        public string GetNextAvailableTrailerNumber()
        {
            var used = LoadAll()
                .Select(x => x.TrailerNumber)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => int.TryParse(x.Trim(), out var n) ? n : -1)
                .Where(x => x > 0)
                .ToHashSet();

            var next = 1;
            while (used.Contains(next)) next++;
            return next.ToString("000");
        }

        private void SaveAll(List<FleetCommandTrailer> trailers)
        {
            var clean = trailers
                .Where(x => x != null)
                .OrderBy(x => ParseTrailerNumberForSort(x.TrailerNumber))
                .ThenBy(x => x.TrailerNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();

            File.WriteAllText(_path, JsonSerializer.Serialize(clean, WriteOpts));
        }

        private static int ParseTrailerNumberForSort(string? value)
        {
            if (int.TryParse((value ?? "").Trim(), out var n))
                return n;
            return int.MaxValue;
        }
    }
}
