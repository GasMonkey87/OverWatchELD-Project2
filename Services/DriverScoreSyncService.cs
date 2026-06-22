using OverWatchELD.Stores;
using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;
using System.Text.RegularExpressions;
using OverWatchELD.Services;

namespace OverWatchELD.Services
{
    public sealed class DriverScoreSyncService
    {
        private const string BotBaseUrl = "https://overwatcheld.up.railway.app";

        public async Task<bool> SyncAsync()
        {
            var guildId = ResolveGuildId();
            var discordUserId = ResolveDiscordUserId();

            if (string.IsNullOrWhiteSpace(guildId) ||
     string.IsNullOrWhiteSpace(discordUserId))
            {
                System.Windows.MessageBox.Show(
                    $"GuildId: [{guildId}]\nDiscordUserId: [{discordUserId}]",
                    "Driver Score Debug");

                return false;
            }

            var inspections = new InspectionRecordStore().LoadAll();

            var inspectionDefects = inspections.Count(x => !x.Passed);

            var missedPreTrips = inspections.Any(x =>
                (x.InspectionType ?? "").Contains("Pre", StringComparison.OrdinalIgnoreCase) &&
                x.CreatedUtc.Date == DateTime.UtcNow.Date)
                ? 0
                : 1;

            var score = 100;
            score -= inspectionDefects * 8;
            score -= missedPreTrips * 10;
            score = Math.Clamp(score, 0, 100);

            var payload = new
            {
                guildId,
                discordUserId,
                driverName = ResolveDriverName(),
                score,
                speedingEvents = 0,
                inspectionDefects,
                hosViolations = 0,
                missedPreTrips,
                notes = score >= 90
                    ? "Excellent safety record."
                    : score >= 75
                        ? "Good score. Review minor issues."
                        : "Needs safety review."
            };

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var json = JsonSerializer.Serialize(payload);

            var res = await http.PostAsync(
                $"{BotBaseUrl}/api/drivers/score/update",
                new StringContent(json, Encoding.UTF8, "application/json"));

            return res.IsSuccessStatusCode;
        }

        private static string ResolveGuildId()
        {
            try
            {
                var cfg = VtcConfigService.Load(true);
                var discord = GetPropObj(cfg, "Discord");

                var value = FirstNonBlank(
                    GetProp(cfg, "GuildId"),
                    GetProp(discord, "GuildId"));

                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
            catch { }

            try
            {
                var pairing = VtcPairingStore.Load();

                var value = FirstNonBlank(
                    GetProp(pairing, "GuildId"));

                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
            catch { }

            return "";
        }

        private static string ResolveDiscordUserId()
        {
            var direct = FirstNonBlank(
                TryReadKnownObjects(),
                TryScanOverWatchJsonFiles());

            return direct;
        }

        private static string TryReadKnownObjects()
        {
            try
            {
                var identity = DiscordIdentityService.Load();

                var value = FirstNonBlank(
                    GetProp(identity, "DiscordUserId"),
                    GetProp(identity, "DiscordId"),
                    GetProp(identity, "UserId"),
                    GetProp(identity, "Id"),
                    GetProp(identity, "LinkedDiscordUserId"));

                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
            catch { }

            try
            {
                var pairing = VtcPairingStore.Load();

                var value = FirstNonBlank(
                    GetProp(pairing, "DiscordUserId"),
                    GetProp(pairing, "DiscordId"),
                    GetProp(pairing, "UserId"),
                    GetProp(pairing, "Id"),
                    GetProp(pairing, "LinkedDiscordUserId"));

                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
            catch { }

            return "";
        }

        private static string TryScanOverWatchJsonFiles()
        {
            try
            {
                var roots = new[]
                {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OverWatchELD"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OverWatchELD"),
            AppContext.BaseDirectory
        };

                foreach (var root in roots.Where(Directory.Exists))
                {
                    foreach (var file in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
                    {
                        var name = Path.GetFileName(file).ToLowerInvariant();

                        if (!name.Contains("discord") &&
                            !name.Contains("pair") &&
                            !name.Contains("vtc") &&
                            !name.Contains("identity"))
                            continue;

                        var text = File.ReadAllText(file);

                        var match = Regex.Match(
                            text,
                            @"(?i)""(?:discordUserId|discordId|userId|linkedDiscordUserId|id)""\s*:\s*""(?<id>\d{16,25})""");

                        if (match.Success)
                            return match.Groups["id"].Value;
                    }
                }
            }
            catch { }

            return "";
        }

        private static string ResolveDriverName()
        {
            try
            {
                var cfg = VtcConfigService.Load(true);
                var discord = GetPropObj(cfg, "Discord");

                var value = FirstNonBlank(
                    GetProp(cfg, "DriverName"),
                    GetProp(cfg, "DisplayName"),
                    GetProp(cfg, "UserName"),
                    GetProp(discord, "Username"),
                    GetProp(discord, "DiscordUsername"),
                    GetProp(discord, "DisplayName"),
                    GetProp(discord, "Name"));

                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
            catch { }

            try
            {
                var pairing = VtcPairingStore.Load();

                var value = FirstNonBlank(
                    GetProp(pairing, "DriverName"),
                    GetProp(pairing, "DisplayName"),
                    GetProp(pairing, "DiscordUsername"),
                    GetProp(pairing, "UserName"));

                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
            catch { }

            try
            {
                var identity = DiscordIdentityService.Load();

                var value = FirstNonBlank(
                    GetProp(identity, "DisplayName"),
                    GetProp(identity, "DiscordUsername"),
                    GetProp(identity, "Username"),
                    GetProp(identity, "Name"));

                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
            catch { }

            return EldDriverIdentityResolver.DriverName();
        }

        private static object? GetPropObj(object? obj, string name)
        {
            try
            {
                return obj?.GetType()
                    .GetProperty(
                        name,
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.IgnoreCase)
                    ?.GetValue(obj);
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
                return obj?.GetType()
                    .GetProperty(
                        name,
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.IgnoreCase)
                    ?.GetValue(obj)
                    ?.ToString()
                    ?.Trim() ?? "";
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