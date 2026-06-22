using OverWatchELD.Models.Fleet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace OverWatchELD.Services.Fleet
{
    public sealed class FleetCommanderRosterService
    {
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        private static readonly JsonSerializerOptions _json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public sealed class DiscordRosterDriver
        {
            public string DriverId { get; set; } = "";
            public string DiscordUserId { get; set; } = "";
            public string DriverName { get; set; } = "";
            public string DisplayName { get; set; } = "";
            public string Username { get; set; } = "";
            public string RoleName { get; set; } = "";
        }

        public sealed class FleetCommanderRow
        {
            public string DriverId { get; set; } = "";
            public string DiscordUserId { get; set; } = "";
            public string DriverName { get; set; } = "";
            public string AssignedTruckId { get; set; } = "";
            public string AssignedTruckNumber { get; set; } = "";
            public string AssignedTruckName { get; set; } = "";
            public string Status { get; set; } = "";
        }

        public async Task<List<FleetCommanderRow>> BuildRowsAsync(
            string botApiBaseUrl,
            IEnumerable<FleetTruck> trucks,
            IEnumerable<FleetTruckAssignment> assignments)
        {
            var roster = await LoadDiscordRosterAsync(botApiBaseUrl);

            var truckList = trucks?.ToList() ?? new List<FleetTruck>();
            var assignmentList = assignments?.ToList() ?? new List<FleetTruckAssignment>();

            var truckById = truckList
                .Select(t => new
                {
                    Truck = t,
                    Id = GetTruckId(t)
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.First().Truck,
                    StringComparer.OrdinalIgnoreCase);

            var rows = new List<FleetCommanderRow>();

            // 1) Always show every Discord roster driver
            foreach (var driver in roster)
            {
                var match = assignmentList.FirstOrDefault(a =>
                    Same(a.DriverId, driver.DriverId) ||
                    Same(a.DriverId, driver.DiscordUserId) ||
                    Same(a.DiscordUserId, driver.DiscordUserId));

                FleetTruck? truck = null;
                if (match != null && !string.IsNullOrWhiteSpace(match.TruckId))
                    truckById.TryGetValue(match.TruckId.Trim(), out truck);

                rows.Add(new FleetCommanderRow
                {
                    DriverId = FirstNonEmpty(driver.DriverId, driver.DiscordUserId),
                    DiscordUserId = driver.DiscordUserId ?? "",
                    DriverName = FirstNonEmpty(
                        driver.DisplayName,
                        driver.DriverName,
                        driver.Username,
                        "Unknown Driver"),
                    AssignedTruckId = match?.TruckId ?? "",
                    AssignedTruckNumber = truck != null ? GetTruckNumber(truck) : "",
                    AssignedTruckName = truck != null ? GetTruckDisplayName(truck) : "",
                    Status = match != null ? "Assigned" : "Unassigned"
                });
            }

            // 2) Add fallback assigned rows not found in Discord roster
            foreach (var assignment in assignmentList)
            {
                var existing = rows.Any(r =>
                    Same(r.DriverId, assignment.DriverId) ||
                    Same(r.DiscordUserId, assignment.DiscordUserId));

                if (existing)
                    continue;

                FleetTruck? truck = null;
                if (!string.IsNullOrWhiteSpace(assignment.TruckId))
                    truckById.TryGetValue(assignment.TruckId.Trim(), out truck);

                rows.Add(new FleetCommanderRow
                {
                    DriverId = assignment.DriverId ?? "",
                    DiscordUserId = assignment.DiscordUserId ?? "",
                    DriverName = FirstNonEmpty(
                        assignment.DriverName,
                        assignment.DiscordUserId,
                        assignment.DriverId,
                        "Unknown Driver"),
                    AssignedTruckId = assignment.TruckId ?? "",
                    AssignedTruckNumber = truck != null ? GetTruckNumber(truck) : "",
                    AssignedTruckName = truck != null ? GetTruckDisplayName(truck) : "",
                    Status = "Assigned"
                });
            }

            return rows
                .OrderBy(r => r.DriverName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task<List<DiscordRosterDriver>> LoadDiscordRosterAsync(string botApiBaseUrl)
        {
            if (string.IsNullOrWhiteSpace(botApiBaseUrl))
                return new List<DiscordRosterDriver>();

            var baseUrl = botApiBaseUrl.Trim().TrimEnd('/');

            // Try likely roster endpoints
            var urls = new[]
            {
                $"{baseUrl}/api/vtc/roster",
                $"{baseUrl}/api/roster",
                $"{baseUrl}/api/vtc/drivers",
                $"{baseUrl}/api/drivers"
            };

            foreach (var url in urls)
            {
                try
                {
                    var json = await _http.GetStringAsync(url);
                    if (string.IsNullOrWhiteSpace(json))
                        continue;

                    // direct array
                    var direct = JsonSerializer.Deserialize<List<DiscordRosterDriver>>(json, _json);
                    if (direct != null && direct.Count > 0)
                        return direct;

                    // wrapped object: { drivers:[...] } or { roster:[...] }
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        if (doc.RootElement.TryGetProperty("drivers", out var driversEl) &&
                            driversEl.ValueKind == JsonValueKind.Array)
                        {
                            var wrappedDrivers = JsonSerializer.Deserialize<List<DiscordRosterDriver>>(
                                driversEl.GetRawText(), _json);

                            if (wrappedDrivers != null && wrappedDrivers.Count > 0)
                                return wrappedDrivers;
                        }

                        if (doc.RootElement.TryGetProperty("roster", out var rosterEl) &&
                            rosterEl.ValueKind == JsonValueKind.Array)
                        {
                            var wrappedRoster = JsonSerializer.Deserialize<List<DiscordRosterDriver>>(
                                rosterEl.GetRawText(), _json);

                            if (wrappedRoster != null && wrappedRoster.Count > 0)
                                return wrappedRoster;
                        }

                        if (doc.RootElement.TryGetProperty("members", out var membersEl) &&
                            membersEl.ValueKind == JsonValueKind.Array)
                        {
                            var wrappedMembers = JsonSerializer.Deserialize<List<DiscordRosterDriver>>(
                                membersEl.GetRawText(), _json);

                            if (wrappedMembers != null && wrappedMembers.Count > 0)
                                return wrappedMembers;
                        }
                    }
                }
                catch
                {
                    // try next endpoint
                }
            }

            return new List<DiscordRosterDriver>();
        }

        private static bool Same(string? a, string? b)
        {
            return string.Equals(
                (a ?? "").Trim(),
                (b ?? "").Trim(),
                StringComparison.OrdinalIgnoreCase);
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

        private static string GetTruckId(FleetTruck truck)
        {
            return FirstNonEmpty(
                ReadString(truck, "Id"),
                ReadString(truck, "TruckId"),
                ReadString(truck, "FleetTruckId"),
                ReadString(truck, "VehicleId"),
                ReadString(truck, "UnitId"),
                ReadString(truck, "TruckGuid"),
                ReadString(truck, "Guid"),
                ReadString(truck, "Number"),
                ReadString(truck, "TruckNumber"));
        }

        private static string GetTruckNumber(FleetTruck truck)
        {
            return FirstNonEmpty(
                ReadString(truck, "UnitNumber"),
                ReadString(truck, "TruckNumber"),
                ReadString(truck, "Number"),
                ReadString(truck, "UnitNo"),
                ReadString(truck, "TruckNo"),
                ReadString(truck, "Unit"),
                ReadString(truck, "Identifier"),
                ReadString(truck, "Id"),
                ReadString(truck, "TruckId"));
        }

        private static string GetTruckDisplayName(FleetTruck truck)
        {
            var primary = FirstNonEmpty(
                ReadString(truck, "DisplayName"),
                ReadString(truck, "TruckName"),
                ReadString(truck, "Name"),
                ReadString(truck, "Title"),
                ReadString(truck, "Label"),
                ReadString(truck, "Description"),
                ReadString(truck, "MakeModel"),
                ReadString(truck, "Model"));

            var make = ReadString(truck, "Make");
            var model = ReadString(truck, "Model");

            if (!string.IsNullOrWhiteSpace(primary))
                return primary;

            var combo = $"{make} {model}".Trim();
            return combo;
        }

        private static string ReadString(object obj, string propertyName)
        {
            try
            {
                var prop = obj.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                if (prop == null)
                    return "";

                var value = prop.GetValue(obj);
                if (value == null)
                    return "";

                return Convert.ToString(value)?.Trim() ?? "";
            }
            catch
            {
                return "";
            }
        }
    }
}
