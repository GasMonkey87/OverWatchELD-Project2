using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services
{
    /// <summary>
    /// Persists locations discovered from live telemetry/map driver payloads.
    /// This lets OverWatch ELD learn expansion cities/companies over time without changing ATS telemetry conversion.
    /// </summary>
    public static class AtsExpansionLocationDiscoveryStore
    {
        private static readonly object Gate = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static string DataFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OverWatchELD");

        private static string DiscoveryFilePath =>
            Path.Combine(DataFolder, "ats_expansion_discovered_locations.json");

        public static List<AtsExpansionLiveMapLocation> Load()
        {
            lock (Gate)
            {
                try
                {
                    if (!File.Exists(DiscoveryFilePath))
                        return new List<AtsExpansionLiveMapLocation>();

                    var json = File.ReadAllText(DiscoveryFilePath);
                    var rows = JsonSerializer.Deserialize<List<AtsExpansionLiveMapLocation>>(json, JsonOptions)
                               ?? new List<AtsExpansionLiveMapLocation>();

                    return rows
                        .Where(IsValid)
                        .GroupBy(BuildKey, StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.OrderByDescending(x => x.DiscoveredUtc).First())
                        .OrderBy(x => x.Kind, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(x => x.City, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                catch
                {
                    return new List<AtsExpansionLiveMapLocation>();
                }
            }
        }

        public static void Upsert(AtsExpansionLiveMapLocation location)
        {
            if (!IsValid(location))
                return;

            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(DataFolder);

                    var rows = LoadUnlocked();
                    var key = BuildKey(location);
                    var existing = rows.FirstOrDefault(x => string.Equals(BuildKey(x), key, StringComparison.OrdinalIgnoreCase));

                    if (existing == null)
                    {
                        location.Source = FirstNonEmpty(location.Source, "Telemetry Discovery");
                        location.Kind = FirstNonEmpty(location.Kind, "Discovered");
                        location.DiscoveredUtc = location.DiscoveredUtc == default ? DateTime.UtcNow : location.DiscoveredUtc;
                        rows.Add(location);
                    }
                    else
                    {
                        existing.Kind = FirstNonEmpty(location.Kind, existing.Kind, "Discovered");
                        existing.Name = FirstNonEmpty(location.Name, existing.Name);
                        existing.City = FirstNonEmpty(location.City, existing.City);
                        existing.Source = FirstNonEmpty(location.Source, existing.Source, "Telemetry Discovery");
                        existing.Token = FirstNonEmpty(location.Token, existing.Token);
                        existing.Longitude = location.Longitude;
                        existing.Latitude = location.Latitude;
                        existing.DiscoveredUtc = DateTime.UtcNow;
                    }

                    rows = rows
                        .Where(IsValid)
                        .GroupBy(BuildKey, StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.OrderByDescending(x => x.DiscoveredUtc).First())
                        .OrderByDescending(x => x.DiscoveredUtc)
                        .Take(1000)
                        .OrderBy(x => x.Kind, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(x => x.City, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    File.WriteAllText(DiscoveryFilePath, JsonSerializer.Serialize(rows, JsonOptions));
                }
                catch
                {
                }
            }
        }

        public static void UpsertFromWebMessage(JsonElement payload)
        {
            try
            {
                var kind = Read(payload, "kind");
                var name = Read(payload, "name");
                var city = Read(payload, "city");
                var source = Read(payload, "source");
                var token = Read(payload, "token");
                var longitude = ReadDouble(payload, "longitude");
                var latitude = ReadDouble(payload, "latitude");

                if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(city))
                    name = city;

                if (string.IsNullOrWhiteSpace(kind))
                    kind = string.IsNullOrWhiteSpace(city) || string.Equals(city, name, StringComparison.OrdinalIgnoreCase)
                        ? "City"
                        : "Company";

                Upsert(new AtsExpansionLiveMapLocation
                {
                    Kind = kind,
                    Name = name,
                    City = city,
                    Source = FirstNonEmpty(source, "Telemetry Discovery"),
                    Token = token,
                    Longitude = longitude,
                    Latitude = latitude,
                    DiscoveredUtc = DateTime.UtcNow
                });
            }
            catch
            {
            }
        }

        private static List<AtsExpansionLiveMapLocation> LoadUnlocked()
        {
            try
            {
                if (!File.Exists(DiscoveryFilePath))
                    return new List<AtsExpansionLiveMapLocation>();

                var json = File.ReadAllText(DiscoveryFilePath);
                return JsonSerializer.Deserialize<List<AtsExpansionLiveMapLocation>>(json, JsonOptions)
                       ?? new List<AtsExpansionLiveMapLocation>();
            }
            catch
            {
                return new List<AtsExpansionLiveMapLocation>();
            }
        }

        private static bool IsValid(AtsExpansionLiveMapLocation? row)
        {
            if (row == null)
                return false;

            if (string.IsNullOrWhiteSpace(row.Name) && string.IsNullOrWhiteSpace(row.City))
                return false;

            if (double.IsNaN(row.Longitude) || double.IsNaN(row.Latitude) ||
                double.IsInfinity(row.Longitude) || double.IsInfinity(row.Latitude))
                return false;

            if (Math.Abs(row.Longitude) < 0.01 || Math.Abs(row.Latitude) < 0.01)
                return false;

            return true;
        }

        private static string BuildKey(AtsExpansionLiveMapLocation row)
        {
            var name = Normalize(FirstNonEmpty(row.City, row.Name));
            var kind = Normalize(FirstNonEmpty(row.Kind, "Discovered"));
            var lon = Math.Round(row.Longitude, 4).ToString("0.0000");
            var lat = Math.Round(row.Latitude, 4).ToString("0.0000");
            return $"{kind}|{name}|{lon}|{lat}";
        }

        private static string Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            return string.Join(" ", value.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        private static string Read(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.Object)
                return "";

            foreach (var prop in element.EnumerateObject())
            {
                if (!string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                    continue;

                return prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString()?.Trim() ?? "",
                    JsonValueKind.Number => prop.Value.ToString(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => ""
                };
            }

            return "";
        }

        private static double ReadDouble(JsonElement element, string name)
        {
            var text = Read(element, name);
            return double.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var value)
                ? value
                : 0;
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }
    }
}
