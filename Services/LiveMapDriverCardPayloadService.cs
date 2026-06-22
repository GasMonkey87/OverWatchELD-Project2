using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public static class LiveMapDriverCardPayloadService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public static string BuildDriversJson(IEnumerable<object>? telemetryRows)
        {
            var cards = BuildDrivers(telemetryRows);
            return JsonSerializer.Serialize(cards, JsonOptions);
        }

        public static List<LiveMapDriverCardRow> BuildDrivers(IEnumerable<object>? telemetryRows)
        {
            var rows = new List<LiveMapDriverCardRow>();

            if (telemetryRows == null)
                return rows;

            foreach (var row in telemetryRows)
            {
                try
                {
                    var driver = FirstNonEmpty(
                        Read(row, "DriverName"),
                        Read(row, "Driver"),
                        Read(row, "Name"),
                        "Unknown Driver");

                    var truck = FirstNonEmpty(
                        Read(row, "TruckName"),
                        Read(row, "Truck"),
                        Read(row, "VehicleName"),
                        Read(row, "Vehicle"),
                        "Unknown Truck");

                    var status = FirstNonEmpty(
                        Read(row, "Status"),
                        Read(row, "DutyStatus"),
                        Read(row, "CurrentStatus"),
                        "Unknown");

                    var loadNumber = FirstNonEmpty(
                        Read(row, "LoadNumber"),
                        Read(row, "CurrentLoad"),
                        Read(row, "ActiveLoad"),
                        "");

                    var cargo = FirstNonEmpty(
                        Read(row, "Cargo"),
                        Read(row, "CargoName"),
                        Read(row, "Commodity"),
                        "");

                    var trailer = FirstNonEmpty(
                        Read(row, "Trailer"),
                        Read(row, "TrailerName"),
                        "");

                    var city = FirstNonEmpty(
                        Read(row, "ResolvedCity"),
                        Read(row, "CurrentCity"),
                        Read(row, "City"),
                        "");

                    var company = FirstNonEmpty(
                        Read(row, "ResolvedCompany"),
                        Read(row, "CurrentCompany"),
                        Read(row, "Company"),
                        "");

                    var source = FirstNonEmpty(
                        Read(row, "ResolvedSource"),
                        Read(row, "MapSource"),
                        Read(row, "ExpansionSource"),
                        "");

                    var x = ReadDouble(row, "WorldX", "X", "RawX");
                    var z = ReadDouble(row, "WorldZ", "Z", "RawZ");
                    var lat = ReadDouble(row, "Latitude", "Lat");
                    var lng = ReadDouble(row, "Longitude", "Lng", "Lon");

                    rows.Add(new LiveMapDriverCardRow
                    {
                        DriverName = driver,
                        TruckName = truck,
                        Status = status,
                        LoadNumber = loadNumber,
                        Cargo = cargo,
                        Trailer = trailer,
                        CurrentCity = city,
                        CurrentCompany = company,
                        MapSource = source,
                        WorldX = x,
                        WorldZ = z,
                        Latitude = lat,
                        Longitude = lng,
                        LastUpdatedUtc = DateTime.UtcNow
                    });
                }
                catch
                {
                }
            }

            return rows
                .GroupBy(x => x.DriverName.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Last())
                .OrderBy(x => x.DriverName)
                .ToList();
        }

        private static string Read(object obj, string propertyName)
        {
            try
            {
                var prop = obj.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                return prop?.GetValue(obj)?.ToString()?.Trim() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static double? ReadDouble(object obj, params string[] names)
        {
            foreach (var name in names)
            {
                var raw = Read(obj, name);
                if (double.TryParse(raw, out var value))
                    return value;
            }

            return null;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }
    }

    public sealed class LiveMapDriverCardRow
    {
        public string DriverName { get; set; } = "";
        public string TruckName { get; set; } = "";
        public string Status { get; set; } = "";
        public string LoadNumber { get; set; } = "";
        public string Cargo { get; set; } = "";
        public string Trailer { get; set; } = "";
        public string CurrentCity { get; set; } = "";
        public string CurrentCompany { get; set; } = "";
        public string MapSource { get; set; } = "";
        public double? WorldX { get; set; }
        public double? WorldZ { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
    }
}
