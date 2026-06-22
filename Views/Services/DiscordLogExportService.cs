using OverWatchELD.ViewModels;
using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using OverWatchELD.Services;

namespace OverWatchELD.Services
{
    public sealed class DiscordLogExportService
    {
        private const string BotBaseUrl = "https://overwatcheld.up.railway.app";

        public Task<bool> ExportAsync(LogsViewModel? vm)
        {
            var eventCount = vm?.DutyEvents?.Count ?? 0;

            var summary = eventCount <= 0
                ? "No duty events found for selected day."
                : string.Join("\n", vm!.DutyEvents.Take(25).Select(x =>
                    $"{x.StartLocal} - {x.EndLocal} | {x.Status} | {x.Note}"));

            return ExportAsync(
                vm,
                summary,
                vm?.HeaderDateText ?? DateTime.Now.ToString("yyyy-MM-dd"));
        }

        public async Task<bool> ExportAsync(
            LogsViewModel? vm,
            string summary,
            string dateRange)
        {
            var guildId = ResolveGuildId();

            if (string.IsNullOrWhiteSpace(guildId))
                throw new InvalidOperationException("No VTC guild ID found. Link ELD to a VTC first.");

            var reportText = BuildReportText(vm, summary, dateRange);

            var payload = new
            {
                guildId,
                driverName = ResolveDriverName(),
                truck = ResolveTruckName(),
                unitNumber = ResolveTruckNumber(),
                dateRange,
                certified = vm?.IsSelectedDayCertified == true ? "YES" : "NO",
                violations = ResolveViolations(vm),
                hosRemaining = ResolveHosRemaining(vm),
                summary = string.IsNullOrWhiteSpace(summary)
                    ? "No selected duty events."
                    : summary,
                reportText
            };

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var json = JsonSerializer.Serialize(payload);

            var res = await http.PostAsync(
                $"{BotBaseUrl}/api/logs/export",
                new StringContent(json, Encoding.UTF8, "application/json"));

            return res.IsSuccessStatusCode;
        }

        private static string BuildReportText(
            LogsViewModel? vm,
            string summary,
            string dateRange)
        {
            var sb = new StringBuilder();

            sb.AppendLine("OverWatch ELD Driver Log Export");
            sb.AppendLine("--------------------------------");
            sb.AppendLine($"Driver: {ResolveDriverName()}");
            sb.AppendLine($"Truck: {ResolveTruckName()}");
            sb.AppendLine($"Unit #: {ResolveTruckNumber()}");
            sb.AppendLine($"Date Range: {dateRange}");
            sb.AppendLine($"Certified: {(vm?.IsSelectedDayCertified == true ? "YES" : "NO")}");
            sb.AppendLine($"Violations: {ResolveViolations(vm)}");
            sb.AppendLine($"HOS Remaining: {ResolveHosRemaining(vm)}");
            sb.AppendLine();
            sb.AppendLine("Duty Events");
            sb.AppendLine("--------------------------------");

            if (string.IsNullOrWhiteSpace(summary))
                sb.AppendLine("No selected duty events.");
            else
                sb.AppendLine(summary);

            return sb.ToString();
        }

        private static string ResolveDriverName()
        {
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

        private static string ResolveTruckName()
        {
            try
            {
                var app = System.Windows.Application.Current as App;
                var snap = app?.Telemetry?.LastSnapshot;

                var truck = FirstNonBlank(
                    GetProp(snap, "TruckName"),
                    GetProp(snap, "TruckMakeModel"),
                    GetProp(snap, "TruckModel"),
                    GetProp(snap, "Model"));

                if (!string.IsNullOrWhiteSpace(truck))
                    return truck;
            }
            catch { }

            return "Unknown Truck";
        }

        private static string ResolveTruckNumber()
        {
            try
            {
                var app = System.Windows.Application.Current as App;
                var snap = app?.Telemetry?.LastSnapshot;

                var unit = FirstNonBlank(
                    GetProp(snap, "TruckId"),
                    GetProp(snap, "TruckID"),
                    GetProp(snap, "TruckNumber"),
                    GetProp(snap, "AssignedTruckNumber"));

                if (!string.IsNullOrWhiteSpace(unit))
                    return unit;
            }
            catch { }

            return "N/A";
        }

        private static string ResolveViolations(LogsViewModel? vm)
        {
            try
            {
                var direct = FirstNonBlank(
                    GetProp(vm, "ViolationsText"),
                    GetProp(vm, "ViolationText"),
                    GetProp(vm, "CurrentViolationsText"),
                    GetProp(vm, "ViolationSummary"));

                if (!string.IsNullOrWhiteSpace(direct))
                    return direct;
            }
            catch { }

            return "None";
        }

        private static string ResolveHosRemaining(LogsViewModel? vm)
        {
            try
            {
                var direct = FirstNonBlank(
                    GetProp(vm, "HosRemainingText"),
                    GetProp(vm, "HOSRemainingText"),
                    GetProp(vm, "CycleRemainingText"),
                    GetProp(vm, "DriveRemainingText"));

                if (!string.IsNullOrWhiteSpace(direct))
                    return direct;
            }
            catch { }

            return "N/A";
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
                return obj?.GetType().GetProperty(
                    name,
                    BindingFlags.Public |
                    BindingFlags.Instance |
                    BindingFlags.IgnoreCase)?.GetValue(obj);
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
                return obj?.GetType().GetProperty(
                    name,
                    BindingFlags.Public |
                    BindingFlags.Instance |
                    BindingFlags.IgnoreCase)?.GetValue(obj)?.ToString()?.Trim() ?? "";
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