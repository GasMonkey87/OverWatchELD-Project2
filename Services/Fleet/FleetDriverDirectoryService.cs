using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace OverWatchELD.Services.Fleet
{
    public sealed class FleetDriverDirectoryService
    {
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        public sealed class DriverDirectoryItem
        {
            public string DriverId { get; set; } = "";
            public string DiscordUserId { get; set; } = "";
            public string DriverName { get; set; } = "";
            public string DisplayName { get; set; } = "";
            public string Username { get; set; } = "";
            public string RoleName { get; set; } = "";

            public string NameForUi =>
                FirstNonEmpty(
                    DisplayName,
                    DriverName,
                    Username,
                    DiscordUserId,
                    DriverId,
                    "Unknown Driver");
        }

        public async Task<List<DriverDirectoryItem>> LoadDriversAsync(string botApiBaseUrl, string? guildId = null)
        {
            var result = new List<DriverDirectoryItem>();

            if (string.IsNullOrWhiteSpace(botApiBaseUrl))
                return result;

            var baseUrl = botApiBaseUrl.Trim().TrimEnd('/');
            var gid = (guildId ?? "").Trim();
            var suffix = string.IsNullOrWhiteSpace(gid) ? "" : $"?guildId={Uri.EscapeDataString(gid)}";

            var endpoints = new[]
            {
                $"{baseUrl}/api/dashboard/drivers{suffix}",
                $"{baseUrl}/api/vtc/roster{suffix}",
                $"{baseUrl}/api/roster{suffix}",
                $"{baseUrl}/api/vtc/drivers{suffix}",
                $"{baseUrl}/api/drivers{suffix}",
                $"{baseUrl}/api/vtc/members{suffix}"
            };

            foreach (var url in endpoints)
            {
                try
                {
                    var json = await _http.GetStringAsync(url);
                    if (string.IsNullOrWhiteSpace(json))
                        continue;

                    var parsed = ParseDriversFromJson(json);
                    if (parsed.Count > 0)
                        return Normalize(parsed);
                }
                catch
                {
                    // try next endpoint
                }
            }

            return result;
        }

        private static List<DriverDirectoryItem> ParseDriversFromJson(string json)
        {
            var result = new List<DriverDirectoryItem>();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                ReadArray(root, result);
                return result;
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                // wrapped arrays
                if (TryGetArray(root, "drivers", out var driversArray))
                {
                    ReadArray(driversArray, result);
                    if (result.Count > 0) return result;
                }

                if (TryGetArray(root, "roster", out var rosterArray))
                {
                    ReadArray(rosterArray, result);
                    if (result.Count > 0) return result;
                }

                if (TryGetArray(root, "members", out var membersArray))
                {
                    ReadArray(membersArray, result);
                    if (result.Count > 0) return result;
                }

                if (TryGetArray(root, "users", out var usersArray))
                {
                    ReadArray(usersArray, result);
                    if (result.Count > 0) return result;
                }

                if (TryGetArray(root, "items", out var itemsArray))
                {
                    ReadArray(itemsArray, result);
                    if (result.Count > 0) return result;
                }

                if (TryGetArray(root, "data", out var dataArray))
                {
                    ReadArray(dataArray, result);
                    if (result.Count > 0) return result;
                }

                // single object fallback
                var single = ParseDriver(root);
                if (single != null)
                    result.Add(single);
            }

            return result;
        }

        private static void ReadArray(JsonElement array, List<DriverDirectoryItem> result)
        {
            if (array.ValueKind != JsonValueKind.Array)
                return;

            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var name = Clean(item.GetString());
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        result.Add(new DriverDirectoryItem
                        {
                            DriverId = name,
                            DiscordUserId = "",
                            DriverName = name,
                            DisplayName = name,
                            Username = name,
                            RoleName = ""
                        });
                    }

                    continue;
                }

                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var parsed = ParseDriver(item);
                if (parsed != null)
                    result.Add(parsed);
            }
        }

        private static DriverDirectoryItem? ParseDriver(JsonElement obj)
        {
            if (obj.ValueKind != JsonValueKind.Object)
                return null;

            var driverId = FirstNonEmpty(
                ReadString(obj, "driverId"),
                ReadString(obj, "id"),
                ReadString(obj, "memberId"),
                ReadString(obj, "userId"));

            var discordUserId = FirstNonEmpty(
                ReadString(obj, "discordUserId"),
                ReadString(obj, "discordId"),
                ReadString(obj, "userId"),
                ReadString(obj, "id"));

            var driverName = FirstNonEmpty(
                ReadString(obj, "driverName"),
                ReadString(obj, "name"),
                ReadString(obj, "nickname"),
                ReadString(obj, "nick"),
                ReadString(obj, "displayName"),
                ReadString(obj, "globalName"),
                ReadString(obj, "username"),
                ReadString(obj, "discordUsername"),
                ReadString(obj, "discordName"));

            var displayName = FirstNonEmpty(
                ReadString(obj, "displayName"),
                ReadString(obj, "globalName"),
                ReadString(obj, "nickname"),
                ReadString(obj, "nick"),
                ReadString(obj, "driverName"),
                ReadString(obj, "name"),
                ReadString(obj, "discordUsername"),
                ReadString(obj, "discordName"));

            var username = FirstNonEmpty(
                ReadString(obj, "username"),
                ReadString(obj, "userName"),
                ReadString(obj, "login"),
                ReadString(obj, "name"),
                ReadString(obj, "discordUsername"),
                ReadString(obj, "discordName"));

            var roleName = FirstNonEmpty(
                ReadString(obj, "roleName"),
                ReadString(obj, "role"),
                ReadFirstArrayString(obj, "roles"),
                ReadFirstArrayString(obj, "roleNames"));

            var finalName = FirstNonEmpty(
                displayName,
                driverName,
                username,
                discordUserId,
                driverId);

            if (string.IsNullOrWhiteSpace(finalName))
                return null;

            return new DriverDirectoryItem
            {
                DriverId = Clean(driverId),
                DiscordUserId = Clean(discordUserId),
                DriverName = Clean(driverName),
                DisplayName = Clean(displayName),
                Username = Clean(username),
                RoleName = Clean(roleName)
            };
        }

        private static bool TryGetArray(JsonElement obj, string propertyName, out JsonElement array)
        {
            array = default;

            foreach (var prop in obj.EnumerateObject())
            {
                if (!string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    array = prop.Value;
                    return true;
                }
            }

            return false;
        }

        private static string ReadString(JsonElement obj, string propertyName)
        {
            foreach (var prop in obj.EnumerateObject())
            {
                if (!string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    continue;

                return JsonValueToString(prop.Value);
            }

            return "";
        }

        private static string ReadFirstArrayString(JsonElement obj, string propertyName)
        {
            foreach (var prop in obj.EnumerateObject())
            {
                if (!string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (prop.Value.ValueKind != JsonValueKind.Array)
                    return "";

                foreach (var item in prop.Value.EnumerateArray())
                {
                    var text = JsonValueToString(item);
                    if (!string.IsNullOrWhiteSpace(text))
                        return text.Trim();
                }
            }

            return "";
        }

        private static string JsonValueToString(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    return value.GetString()?.Trim() ?? "";

                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                    return value.ToString().Trim();

                case JsonValueKind.Object:
                    return FirstNonEmpty(
                        ReadString(value, "name"),
                        ReadString(value, "displayName"),
                        ReadString(value, "username"),
                        ReadString(value, "id"));

                default:
                    return "";
            }
        }

        private static List<DriverDirectoryItem> Normalize(List<DriverDirectoryItem> items)
        {
            var final = new List<DriverDirectoryItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                item.DriverId = Clean(item.DriverId);
                item.DiscordUserId = Clean(item.DiscordUserId);
                item.DriverName = Clean(item.DriverName);
                item.DisplayName = Clean(item.DisplayName);
                item.Username = Clean(item.Username);
                item.RoleName = Clean(item.RoleName);

                if (string.IsNullOrWhiteSpace(item.DisplayName))
                    item.DisplayName = FirstNonEmpty(item.DriverName, item.Username);

                if (string.IsNullOrWhiteSpace(item.DriverName))
                    item.DriverName = FirstNonEmpty(item.DisplayName, item.Username, item.DiscordUserId, item.DriverId);

                var key = FirstNonEmpty(
                    item.DiscordUserId,
                    item.DriverId,
                    item.NameForUi);

                if (string.IsNullOrWhiteSpace(key))
                    continue;

                if (seen.Add(key))
                    final.Add(item);
            }

            final.Sort((a, b) => string.Compare(a.NameForUi, b.NameForUi, StringComparison.OrdinalIgnoreCase));
            return final;
        }

        private static string Clean(string? value) => value?.Trim() ?? "";

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var v in values)
            {
                if (!string.IsNullOrWhiteSpace(v))
                    return v.Trim();
            }
            return "";
        }
    }
}
