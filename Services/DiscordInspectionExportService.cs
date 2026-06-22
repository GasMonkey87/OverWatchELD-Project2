using OverWatchELD.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using OverWatchELD.Services;

namespace OverWatchELD.Services
{
    public sealed class DiscordInspectionExportService
    {
        private const string BotBaseUrl = "https://overwatcheld.up.railway.app";

        public async Task<bool> ExportAsync(IReadOnlyList<InspectionRecord> records)
        {
            var guildId = ResolveGuildId();

            if (string.IsNullOrWhiteSpace(guildId))
                throw new InvalidOperationException("No VTC guild ID found. Link ELD to a VTC first.");

            var summary = string.Join("\n", records.Take(20).Select(r =>
                $"{r.CreatedLocalDisplay} | {r.InspectionType} | Unit {r.UnitNumber} | {r.StatusText} | {r.Defects} | {r.Notes}"));

            var payload = new
            {
                guildId,
                driverName = ResolveDriverName(records),
                count = records.Count,
                summary,
                inspections = records.Select(r => new
                {
                    r.InspectionNumber,
                    r.InspectionType,
                    r.CreatedUtc,
                    r.DriverName,
                    r.TruckName,
                    r.UnitNumber,
                    r.PlateNumber,
                    r.Location,
                    r.Passed,
                    r.Defects,
                    r.Notes
                }).ToArray()
            };

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var json = JsonSerializer.Serialize(payload);

            var res = await http.PostAsync(
                $"{BotBaseUrl}/api/inspections/export",
                new StringContent(json, Encoding.UTF8, "application/json"));

            return res.IsSuccessStatusCode;
        }

        private static string ResolveDriverName(IReadOnlyList<InspectionRecord> records)
        {
            try
            {
                var first = records.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(first?.DriverName))
                    return first.DriverName.Trim();
            }
            catch { }

            try
            {
                var pairing = VtcPairingStore.Load();

                var name = FirstNonBlank(
                    GetProp(pairing, "DriverName"),
                    GetProp(pairing, "DisplayName"),
                    GetProp(pairing, "DiscordUsername"),
                    GetProp(pairing, "UserName"));

                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
            catch { }

            try
            {
                var cfg = VtcConfigService.Load(true);

                var name = FirstNonBlank(
                    GetProp(cfg, "DriverName"),
                    GetProp(cfg, "DisplayName"),
                    GetProp(cfg, "UserName"),
                    GetProp(GetPropObj(cfg, "Discord"), "Username"),
                    GetProp(GetPropObj(cfg, "Discord"), "DiscordUsername"));

                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
            catch { }

            return EldDriverIdentityResolver.DriverName();
        }

        private static string ResolveGuildId()
        {
            try
            {
                var cfg = VtcConfigService.Load(true);

                var direct = GetProp(cfg, "GuildId");
                if (!string.IsNullOrWhiteSpace(direct))
                    return direct.Trim();

                var nested = GetProp(GetPropObj(cfg, "Discord"), "GuildId");
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested.Trim();
            }
            catch { }

            try
            {
                var pairing = VtcPairingStore.Load();
                var gid = GetProp(pairing, "GuildId");

                if (!string.IsNullOrWhiteSpace(gid))
                    return gid.Trim();
            }
            catch { }

            return "";
        }

        private static object? GetPropObj(object? obj, string name)
        {
            try
            {
                return obj?.GetType().GetProperty(name)?.GetValue(obj);
            }
            catch
            {
                return null;
            }
        }

        private static string GetProp(object? obj, string name)
        {
            try
            {
                return obj?.GetType().GetProperty(name)?.GetValue(obj)?.ToString()?.Trim() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static string FirstNonBlank(params string[] values)
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