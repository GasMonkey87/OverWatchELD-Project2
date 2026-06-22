using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public static class LiveMapDispatchRoutePreviewService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public static string BuildRoutesJson(IEnumerable<object>? dispatchJobs)
        {
            var routes = BuildRoutes(dispatchJobs);
            return JsonSerializer.Serialize(routes, JsonOptions);
        }

        public static List<LiveMapDispatchRoutePreviewRow> BuildRoutes(IEnumerable<object>? dispatchJobs)
        {
            var rows = new List<LiveMapDispatchRoutePreviewRow>();

            if (dispatchJobs == null)
                return rows;

            foreach (var job in dispatchJobs)
            {
                try
                {
                    var status = FirstNonEmpty(Read(job, "Status"), "Unknown");

                    if (status.Equals("Delivered", StringComparison.OrdinalIgnoreCase) ||
                        status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var route = new LiveMapDispatchRoutePreviewRow
                    {
                        LoadNumber = FirstNonEmpty(Read(job, "LoadNumber"), Read(job, "Id"), "Load"),
                        DriverName = FirstNonEmpty(Read(job, "AssignedDriver"), Read(job, "ClaimedBy"), "Unassigned"),
                        Cargo = FirstNonEmpty(Read(job, "Cargo"), Read(job, "Commodity"), ""),
                        Status = status,
                        Company = FirstNonEmpty(Read(job, "Company"), Read(job, "ShipperName"), ""),
                        OriginCity = FirstNonEmpty(Read(job, "OriginCity"), Read(job, "PickupCity"), Read(job, "ShipperCity"), ""),
                        OriginState = FirstNonEmpty(Read(job, "OriginState"), Read(job, "PickupState"), ""),
                        DestinationCity = FirstNonEmpty(Read(job, "DestinationCity"), Read(job, "ReceiverCity"), Read(job, "DeliveryCity"), ""),
                        DestinationState = FirstNonEmpty(Read(job, "DestinationState"), Read(job, "DeliveryState"), ""),
                        Miles = ReadDouble(job, "Miles", "PlannedMiles", "TripMiles"),
                        CreatedUtc = ReadDate(job, "CreatedUtc", "PostedUtc", "UpdatedUtc") ?? DateTime.UtcNow
                    };

                    rows.Add(route);
                }
                catch
                {
                }
            }

            return rows
                .OrderByDescending(x => x.CreatedUtc)
                .Take(100)
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

        private static DateTime? ReadDate(object obj, params string[] names)
        {
            foreach (var name in names)
            {
                var raw = Read(obj, name);

                if (DateTime.TryParse(raw, out var value))
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

    public sealed class LiveMapDispatchRoutePreviewRow
    {
        public string LoadNumber { get; set; } = "";
        public string DriverName { get; set; } = "";
        public string Cargo { get; set; } = "";
        public string Status { get; set; } = "";
        public string Company { get; set; } = "";
        public string OriginCity { get; set; } = "";
        public string OriginState { get; set; } = "";
        public string DestinationCity { get; set; } = "";
        public string DestinationState { get; set; } = "";
        public double? Miles { get; set; }
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }
}