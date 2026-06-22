using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using OverWatchELD.Models.Fleet;

namespace OverWatchELD.Services.Fleet
{
    /// <summary>
    /// Pushes local ELD fleet/truck data to the website VTC portal fleet tab.
    /// Website endpoint:
    /// POST /api/vtc/portal/fleet?guildId={guildId}
    /// </summary>
    public sealed class FleetPortalSyncService
    {
        public static FleetPortalSyncService Shared { get; } = new FleetPortalSyncService();

        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private DateTimeOffset _lastAutoSyncUtc = DateTimeOffset.MinValue;

        private FleetPortalSyncService() { }

        public void QueueSync(IEnumerable<FleetTruck>? trucks)
        {
            var list = trucks?.Where(x => x != null).ToList() ?? new List<FleetTruck>();
            if (list.Count == 0) return;

            // Prevent save storms from posting repeatedly.
            if ((DateTimeOffset.UtcNow - _lastAutoSyncUtc).TotalSeconds < 3)
                return;

            _lastAutoSyncUtc = DateTimeOffset.UtcNow;
            _ = Task.Run(async () =>
            {
                try { await SyncAllAsync(list); }
                catch { }
            });
        }

        public void QueueTelemetrySync(TelemetrySnapshot? snap)
        {
            if (snap == null || !snap.Connected) return;

            // Telemetry can fire many times per second. Keep website sync reasonable.
            if ((DateTimeOffset.UtcNow - _lastAutoSyncUtc).TotalSeconds < 10)
                return;

            _lastAutoSyncUtc = DateTimeOffset.UtcNow;
            _ = Task.Run(async () =>
            {
                try { await PushTelemetryTruckAsync(snap); }
                catch { }
            });
        }

        public async Task<FleetPortalSyncResult> SyncRepositoryAsync()
        {
            // The VTC dashboard/Fleet Snapshot is backed by FleetCommandStore, not only FleetTruckRepository.
            // Earlier patches only synced FleetTruckRepository, so the website received 0 trucks even while
            // the ELD dashboard showed Fleet Trucks: 1. This method now pushes both sources.
            var rows = new List<PortalTruckDto>();

            try
            {
                var repo = new FleetTruckRepository();
                rows.AddRange(repo.LoadAll().Where(x => x != null).Select(ToPortalTruck));
            }
            catch { }

            try
            {
                var commandStore = new FleetCommandStore();
                rows.AddRange(commandStore.LoadAll().Where(x => x != null).Select(ToPortalTruck));
            }
            catch { }

            return await SyncPortalRowsAsync(rows).ConfigureAwait(false);
        }

        public async Task<FleetPortalSyncResult> SyncAllAsync(IEnumerable<FleetTruck>? trucks)
        {
            var rows = (trucks ?? Enumerable.Empty<FleetTruck>())
                .Where(x => x != null)
                .Select(ToPortalTruck)
                .ToList();

            // Add FleetCommandStore every time Sync All runs because this is what feeds the dashboard Fleet Snapshot.
            try
            {
                var commandStore = new FleetCommandStore();
                rows.AddRange(commandStore.LoadAll().Where(x => x != null).Select(ToPortalTruck));
            }
            catch { }

            return await SyncPortalRowsAsync(rows).ConfigureAwait(false);
        }

        private async Task<FleetPortalSyncResult> SyncPortalRowsAsync(IEnumerable<PortalTruckDto>? trucks)
        {
            var result = new FleetPortalSyncResult();
            var guildId = ResolveGuildId();
            result.GuildId = guildId;
            result.ApiBaseUrl = ResolveApiBaseUrl();
            if (string.IsNullOrWhiteSpace(guildId))
            {
                result.Errors.Add("Missing VTC GuildId. Link/select a VTC first.");
                return result;
            }

            var rows = (trucks ?? Enumerable.Empty<PortalTruckDto>())
                .Where(x => x != null)
                .Where(x => !string.IsNullOrWhiteSpace(x.Name) || !string.IsNullOrWhiteSpace(x.Model) || !string.IsNullOrWhiteSpace(x.TruckNumber))
                .GroupBy(x => FirstNonBlank(x.Id, x.TruckNumber, x.Plate, x.Name, x.Model), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            if (rows.Count == 0)
                return result;

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                foreach (var truck in rows)
                {
                    try
                    {
                        var ok = await PostTruckAsync(guildId, truck).ConfigureAwait(false);
                        if (ok) result.Pushed++;
                        else result.Failed++;
                    }
                    catch (Exception ex)
                    {
                        result.Failed++;
                        result.Errors.Add($"{FirstNonBlank(truck.TruckNumber, truck.Name, truck.Model, "Truck")}: {ex.Message}");
                    }
                }
            }
            finally
            {
                _gate.Release();
            }

            return result;
        }

        public async Task<bool> PushTelemetryTruckAsync(TelemetrySnapshot snap)
        {
            var guildId = ResolveGuildId();
            if (string.IsNullOrWhiteSpace(guildId) || snap == null) return false;
            return await PostTruckAsync(guildId, ToPortalTruck(snap)).ConfigureAwait(false);
        }

        private async Task<bool> PostTruckAsync(string guildId, PortalTruckDto truck)
        {
            var apiBase = ResolveApiBaseUrl();
            var url = $"{apiBase}/api/vtc/portal/fleet?guildId={Uri.EscapeDataString(guildId)}";
            using var response = await _http.PostAsJsonAsync(url, truck).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) return true;

            var body = string.Empty;
            try { body = await response.Content.ReadAsStringAsync().ConfigureAwait(false); } catch { }
            throw new InvalidOperationException($"Fleet portal POST failed: {(int)response.StatusCode} {response.ReasonPhrase}. {body}");
        }

        private static PortalTruckDto ToPortalTruck(FleetTruck truck)
        {
            var location = FirstNonBlank(
                truck.LastKnownLocation,
                JoinLocation(truck.LastKnownCity, truck.LastKnownState));

            var condition = truck.ConditionPercent > 0
                ? truck.ConditionPercent.ToString("0") + "%"
                : ComputeConditionFromDamage(truck).ToString("0") + "%";

            return new PortalTruckDto
            {
                TruckNumber = FirstNonBlank(truck.Plate, truck.Nickname),
                Name = FirstNonBlank(truck.Nickname, truck.MakeModel, truck.Plate, "Truck"),
                Model = truck.MakeModel ?? "",
                Driver = truck.AssignedDriver ?? "",
                Plate = truck.Plate ?? "",
                Odometer = FirstNonBlank(
                    truck.OdometerMiles > 0 ? truck.OdometerMiles.ToString("0") : "",
                    truck.LastKnownOdometerMiles.HasValue ? truck.LastKnownOdometerMiles.Value.ToString("0") : ""),
                Location = FirstNonBlank(location, "Unknown"),
                Status = truck.IsOnline ? "Online" : truck.IsDriving ? "Driving" : truck.IsParked ? "Parked" : truck.NeedsService ? "Needs Service" : "Available",
                Condition = condition,
                Fuel = truck.FuelPercent > 0 ? truck.FuelPercent.ToString("0") + "%" : truck.FuelPct > 0 ? truck.FuelPct.ToString("0") + "%" : "",
                Notes = BuildNotes(truck)
            };
        }


        private static PortalTruckDto ToPortalTruck(FleetCommandTruck truck)
        {
            var location = FirstNonBlank(
                truck.Location,
                JoinLocation(truck.LastKnownCity, truck.LastKnownState));

            var truckName = FirstNonBlank(
                truck.TruckName,
                truck.Model,
                truck.TruckNumber,
                truck.PlateNumber,
                "Truck");

            return new PortalTruckDto
            {
                Id = FirstNonBlank(truck.Id, truck.TruckNumber, truck.PlateNumber),
                TruckNumber = FirstNonBlank(truck.TruckNumber, truck.PlateNumber, truck.TruckName),
                Name = truckName,
                Model = FirstNonBlank(truck.Model, truck.TruckName),
                Driver = FirstNonBlank(truck.AssignedDriver, "Unassigned"),
                DriverDiscordUserId = FirstNonBlank(truck.DriverDiscordId),
                Plate = FirstNonBlank(truck.PlateNumber),
                Odometer = truck.OdometerMiles > 0 ? truck.OdometerMiles.ToString("0") : "",
                Location = FirstNonBlank(location, "Unknown"),
                Status = FirstNonBlank(truck.Status, truck.IsOnline ? "Online" : truck.IsDriving ? "Driving" : truck.IsParked ? "Parked" : "Available"),
                Condition = truck.HealthPercent > 0 ? truck.HealthPercent.ToString("0") + "%" : "",
                Fuel = truck.FuelPercent > 0 ? truck.FuelPercent.ToString("0") + "%" : "",
                Notes = BuildNotes(truck)
            };
        }

        private static PortalTruckDto ToPortalTruck(TelemetrySnapshot snap)
        {
            var info = VtcInfoStore.Load();
            var link = VtcLinkService.GetLink();
            var location = JoinLocation(snap.City, snap.State);

            return new PortalTruckDto
            {
                TruckNumber = FirstNonBlank(info.UnitNumber, snap.TruckMakeModel, "Truck"),
                Name = FirstNonBlank(snap.TruckMakeModel, info.UnitNumber, "Truck"),
                Model = snap.TruckMakeModel ?? "",
                Driver = FirstNonBlank(link?.DriverName, info.DriverName, "Driver"),
                DriverDiscordUserId = FirstNonBlank(link?.DiscordUserId, info.DiscordUserId),
                Plate = "",
                Odometer = snap.OdometerMiles.HasValue ? snap.OdometerMiles.Value.ToString("0") : "",
                Location = FirstNonBlank(location, "Unknown"),
                Status = "Online",
                Condition = snap.DamagePct.HasValue ? Math.Max(0, 100 - snap.DamagePct.Value).ToString("0") + "%" : "",
                Fuel = snap.FuelPct.HasValue ? snap.FuelPct.Value.ToString("0") + "%" : "",
                Notes = "Synced from live ELD telemetry"
            };
        }

        private static double ComputeConditionFromDamage(FleetTruck truck)
        {
            var maxDamage = Math.Max(truck.EngineDamagePct,
                Math.Max(truck.TransmissionDamagePct,
                Math.Max(truck.CabinDamagePct,
                Math.Max(truck.ChassisDamagePct, truck.WheelsDamagePct))));
            return Math.Max(0, 100 - maxDamage);
        }

        private static string BuildNotes(FleetTruck truck)
        {
            var items = new List<string>();
            if (truck.NeedsService) items.Add("Needs service");
            if (!string.IsNullOrWhiteSpace(truck.LastKnownDutyStatus)) items.Add("Duty: " + truck.LastKnownDutyStatus);
            if (truck.LastTelemetryUtc > DateTimeOffset.MinValue) items.Add("Last telemetry: " + truck.LastTelemetryUtc.ToLocalTime().ToString("g"));
            return string.Join(" | ", items);
        }


        private static string BuildNotes(FleetCommandTruck truck)
        {
            var items = new List<string>();
            if (!string.IsNullOrWhiteSpace(truck.CurrentLoadNumber)) items.Add("Load: " + truck.CurrentLoadNumber);
            if (truck.ServiceDueDate.HasValue) items.Add("Service due: " + truck.ServiceDueDate.Value.ToString("MM/dd/yyyy"));
            if (truck.InspectionDueDate.HasValue) items.Add("Inspection due: " + truck.InspectionDueDate.Value.ToString("MM/dd/yyyy"));
            if (truck.UpdatedUtc > DateTimeOffset.MinValue) items.Add("Updated: " + truck.UpdatedUtc.ToLocalTime().ToString("g"));
            return string.Join(" | ", items);
        }

        private static string ResolveGuildId()
        {
            // Prefer the currently selected/logged-in VTC. Older config files can contain a stale guild id,
            // which makes the ELD say it pushed while the website page for the active VTC stays empty.
            try
            {
                var app = System.Windows.Application.Current as App;
                var gid = Convert.ToString(app?.Session?.GuildId)?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(gid)) return gid;
            }
            catch { }

            try
            {
                var pairing = VtcPairingStore.Load();
                var gid = (pairing?.GuildId ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(gid)) return gid;
            }
            catch { }

            try
            {
                var info = VtcInfoStore.Load();
                var gid = FirstNonBlank(info.DiscordGuildId);
                if (!string.IsNullOrWhiteSpace(gid)) return gid;
            }
            catch { }

            try
            {
                var cfg = VtcConfigService.Load();
                var gid = FirstNonBlank(cfg.Discord?.GuildId, cfg.GuildId);
                if (!string.IsNullOrWhiteSpace(gid)) return gid;
            }
            catch { }

            return "";
        }

        private static string ResolveApiBaseUrl()
        {
            try
            {
                var cfg = VtcConfigService.Load();
                var url = (cfg.BotApiBaseUrl ?? "").Trim().TrimEnd('/');
                if (!string.IsNullOrWhiteSpace(url)) return url;
            }
            catch { }

            return "https://api.overwatcheld.com";
        }

        private static string JoinLocation(string? city, string? state)
        {
            city = (city ?? "").Trim();
            state = (state ?? "").Trim();
            if (string.IsNullOrWhiteSpace(city)) return state;
            if (string.IsNullOrWhiteSpace(state)) return city;
            return city + ", " + state;
        }

        private static string FirstNonBlank(params string?[] values)
        {
            foreach (var value in values)
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            return "";
        }

        private sealed class PortalTruckDto
        {
            public string Id { get; set; } = "";
            public string TruckNumber { get; set; } = "";
            public string Name { get; set; } = "";
            public string Model { get; set; } = "";
            public string Driver { get; set; } = "";
            public string DriverDiscordUserId { get; set; } = "";
            public string Plate { get; set; } = "";
            public string Odometer { get; set; } = "";
            public string Location { get; set; } = "";
            public string Status { get; set; } = "Available";
            public string Condition { get; set; } = "";
            public string Fuel { get; set; } = "";
            public string Notes { get; set; } = "";
        }
    }

    public sealed class FleetPortalSyncResult
    {
        public int Pushed { get; set; }
        public int Failed { get; set; }
        public string GuildId { get; set; } = "";
        public string ApiBaseUrl { get; set; } = "";
        public List<string> Errors { get; } = new List<string>();
        public bool Success => Failed == 0 && Errors.Count == 0;
        public string Summary => $"Website fleet sync complete. Pushed: {Pushed}. Failed: {Failed}. GuildId: {GuildId}. API: {ApiBaseUrl}." +
                                 (Errors.Count == 0 ? "" : "\n\n" + string.Join("\n", Errors.Take(5)));
    }
}
