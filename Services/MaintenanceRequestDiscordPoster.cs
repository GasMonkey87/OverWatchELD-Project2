using OverWatchELD.Models;
using System;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace OverWatchELD.Services
{
    public sealed class MaintenanceRequestDiscordPoster
    {
        private const string DefaultBotBaseUrl = "https://overwatcheld.up.railway.app";

        public async Task<bool> PostAsync(MaintenanceRequestTicket ticket)
        {
            try
            {
                var botBase = ResolveBotBaseUrl().Trim().TrimEnd('/');
                var guildId = FirstNonBlank(ResolveGuildId(), ticket.GuildId);

                if (string.IsNullOrWhiteSpace(botBase))
                {
                    MessageBox.Show("Bot base URL is empty.", "Discord Debug");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(guildId))
                {
                    MessageBox.Show("Guild ID is empty.", "Discord Debug");
                    return false;
                }

                ticket.GuildId = guildId;

                using var http = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(15)
                };

                var body = new
                {
                    guildId,
                    requestNumber = ticket.RequestNumber,
                    title = $"Maintenance Request {ticket.RequestNumber}",
                    driverName = ticket.DriverName,
                    driverDiscordId = ticket.DriverDiscordId,
                    truck = ticket.TruckName,
                    unitNumber = ticket.UnitNumber,
                    plateNumber = ticket.PlateNumber,
                    location = ticket.Location,
                    odometerMiles = ticket.OdometerMiles,
                    fuelPercent = ticket.FuelPercent,
                    conditionPercent = ticket.ConditionPercent,
                    currentIssue = ticket.CurrentIssue,
                    severity = ticket.CurrentIssueSeverity,
                    outOfService = ticket.OutOfService,
                    dotInspectionRequested = ticket.DotInspectionRequested,
                    damageRepairRequested = ticket.DamageRepairRequested,
                    otherMaintenanceRequested = ticket.OtherMaintenanceRequested,
                    malfunctionRepairRequested = ticket.MalfunctionRepairRequested,
                    notes = ticket.Notes,
                    createdUtc = ticket.CreatedUtc
                };

                var json = JsonSerializer.Serialize(body);

                var content = new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json");

                var url =
                    $"{botBase}/api/maintenance/request?guildId={Uri.EscapeDataString(guildId)}";

                var res = await http.PostAsync(url, content);

                var responseText = await res.Content.ReadAsStringAsync();

                MessageBox.Show(
                    $"POST URL:\n{url}\n\n" +
                    $"STATUS:\n{(int)res.StatusCode} {res.StatusCode}\n\n" +
                    $"RESPONSE:\n{responseText}",
                    "Maintenance Discord Debug");

                return res.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.ToString(),
                    "Maintenance Discord Exception");

                return false;
            }
        }

        private static string ResolveBotBaseUrl()
        {
            try
            {
                var cfg = VtcConfigService.LoadOrCreate();
                return FirstNonBlank(
                    GetString(cfg, "BotApiBaseUrl"),
                    GetString(cfg, "BotBaseUrl"),
                    GetString(cfg, "VtcServerUrl"),
                    GetString(cfg, "ApiBaseUrl"),
                    DefaultBotBaseUrl);
            }
            catch
            {
                return DefaultBotBaseUrl;
            }
        }

        private static string ResolveGuildId()
        {
            try
            {
                var cfg = VtcConfigService.LoadOrCreate();
                var discord = GetObject(cfg, "Discord");
                return FirstNonBlank(
                    GetString(discord, "GuildId"),
                    GetString(cfg, "GuildId"),
                    GetString(cfg, "DiscordGuildId"));
            }
            catch
            {
                return "";
            }
        }

        public async Task<bool> PostFixedAsync(MaintenanceRequestTicket ticket, string fixedType)
        {
            try
            {
                var botBase = ResolveBotBaseUrl().Trim().TrimEnd('/');
                var guildId = FirstNonBlank(ResolveGuildId(), ticket.GuildId);

                if (string.IsNullOrWhiteSpace(botBase))
                {
                    MessageBox.Show("Bot base URL is empty.", "Discord Debug");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(guildId))
                {
                    MessageBox.Show("Guild ID is empty.", "Discord Debug");
                    return false;
                }

                using var http = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(15)
                };

                var body = new
                {
                    guildId,
                    requestNumber = ticket.RequestNumber,
                    truck = ticket.TruckName,
                    unitNumber = ticket.UnitNumber,
                    driverName = ticket.DriverName,
                    fixedType,
                    fixedBy = ticket.FixedBy,
                    fixNotes = ticket.FixNotes,
                    fixedUtc = ticket.FixedUtc
                };

                var json = JsonSerializer.Serialize(body);

                var content = new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json");

                var url =
                    $"{botBase}/api/maintenance/fixed?guildId={Uri.EscapeDataString(guildId)}";

                var res = await http.PostAsync(url, content);

                var responseText = await res.Content.ReadAsStringAsync();

                MessageBox.Show(
                    $"POST URL:\n{url}\n\n" +
                    $"STATUS:\n{(int)res.StatusCode} {res.StatusCode}\n\n" +
                    $"RESPONSE:\n{responseText}",
                    "Maintenance Fixed Discord Debug");

                return res.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.ToString(),
                    "Maintenance Fixed Discord Exception");

                return false;
            }
        }

        private static object? GetObject(object? target, string name)
        {
            if (target == null) return null;
            var prop = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            return prop?.GetValue(target);
        }

        private static string GetString(object? target, string name)
        {
            var value = GetObject(target, name);
            return value?.ToString()?.Trim() ?? "";
        }

        private static string FirstNonBlank(params string?[] values)
        {
            foreach (var v in values)
                if (!string.IsNullOrWhiteSpace(v))
                    return v.Trim();
            return "";
        }
    }
}
