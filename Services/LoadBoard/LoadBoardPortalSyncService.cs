using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OverWatchELD.Services.LoadBoard
{
    /// <summary>
    /// Pushes local ELD dispatch/load-board data to the website portal load board.
    /// Website endpoint:
    /// POST /api/vtc/portal/loads?guildId={guildId}
    /// </summary>
    public sealed class LoadBoardPortalSyncService
    {
        public static LoadBoardPortalSyncService Shared { get; } = new();

        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        private readonly SemaphoreSlim _syncGate = new SemaphoreSlim(1, 1);
        private DateTimeOffset _lastAutoPushUtc = DateTimeOffset.MinValue;

        private LoadBoardPortalSyncService() { }

        public void QueueSync(LoadBoardLoad? load)
        {
            if (load == null) return;

            // Prevent telemetry Upsert storms from posting dozens of times per second.
            if ((DateTimeOffset.UtcNow - _lastAutoPushUtc).TotalSeconds < 3)
                return;

            _lastAutoPushUtc = DateTimeOffset.UtcNow;

            _ = Task.Run(async () =>
            {
                try { await PushLoadAsync(load); }
                catch { }
            });
        }

        public async Task<LoadBoardPortalSyncResult> SyncAllAsync(IEnumerable<LoadBoardLoad>? loadBoardLoads, IEnumerable<DispatchJob>? dispatchJobs)
        {
            var result = new LoadBoardPortalSyncResult();
            var guildId = ResolveGuildId();
            if (string.IsNullOrWhiteSpace(guildId))
            {
                result.Errors.Add("Missing VTC GuildId. Link/select a VTC first.");
                return result;
            }

            var loads = new List<PortalLoadDto>();

            foreach (var load in loadBoardLoads ?? Enumerable.Empty<LoadBoardLoad>())
            {
                if (load == null || string.IsNullOrWhiteSpace(load.LoadNumber)) continue;
                loads.Add(ToPortalLoad(load));
            }

            foreach (var job in dispatchJobs ?? Enumerable.Empty<DispatchJob>())
            {
                if (job == null || string.IsNullOrWhiteSpace(job.LoadNumber)) continue;
                if (loads.Any(x => Same(x.LoadNumber, job.LoadNumber))) continue;
                loads.Add(ToPortalLoad(job));
            }

            if (loads.Count == 0)
                return result;

            await _syncGate.WaitAsync();
            try
            {
                foreach (var load in loads.OrderByDescending(x => x.UpdatedUtc))
                {
                    try
                    {
                        var ok = await PostLoadAsync(guildId, load);
                        if (ok) result.Pushed++;
                        else result.Failed++;
                    }
                    catch (Exception ex)
                    {
                        result.Failed++;
                        result.Errors.Add($"{load.LoadNumber}: {ex.Message}");
                    }
                }
            }
            finally
            {
                _syncGate.Release();
            }

            return result;
        }

        public async Task<bool> PushLoadAsync(LoadBoardLoad load)
        {
            var guildId = ResolveGuildId();
            if (string.IsNullOrWhiteSpace(guildId)) return false;
            return await PostLoadAsync(guildId, ToPortalLoad(load));
        }

        public async Task<bool> PushDispatchJobAsync(DispatchJob job)
        {
            var guildId = ResolveGuildId();
            if (string.IsNullOrWhiteSpace(guildId)) return false;
            return await PostLoadAsync(guildId, ToPortalLoad(job));
        }

        private async Task<bool> PostLoadAsync(string guildId, PortalLoadDto load)
        {
            var apiBase = ResolveApiBaseUrl();
            var url = $"{apiBase}/api/vtc/portal/loads?guildId={Uri.EscapeDataString(guildId)}";
            using var response = await _http.PostAsJsonAsync(url, load);
            return response.IsSuccessStatusCode;
        }

        private static PortalLoadDto ToPortalLoad(LoadBoardLoad load)
        {
            return new PortalLoadDto
            {
                LoadNumber = load.LoadNumber,
                Status = NormalizeStatus(load.Status),
                Title = load.LoadNumber,
                Cargo = load.Commodity,
                Weight = load.WeightLbs > 0 ? $"{load.WeightLbs:0} lbs" : "",
                Origin = FirstNonBlank(load.ShipperCity, load.ShipperName),
                OriginCompany = load.ShipperName,
                Destination = FirstNonBlank(load.ReceiverCity, load.ReceiverName),
                DestinationCompany = load.ReceiverName,
                Revenue = load.RevenueUsd > 0 ? load.RevenueUsd.ToString("C2") : "",
                AssignedDriver = load.DriverName,
                AssignedDriverDiscordUserId = load.DriverDiscordId,
                AssignedTruck = FirstNonBlank(load.TruckNumber, load.TruckName),
                Dispatcher = "OverWatch ELD",
                Notes = load.CurrentLocation,
                IsCompanyLoad = true,
                CreatedUtc = load.CreatedUtc,
                UpdatedUtc = load.UpdatedUtc,
                ClaimedUtc = load.AtShipperUtc,
                PickedUpUtc = load.BolCompletedUtc ?? load.AtShipperUtc,
                DeliveredUtc = load.DeliveredUtc
            };
        }

        private static PortalLoadDto ToPortalLoad(DispatchJob job)
        {
            return new PortalLoadDto
            {
                Id = job.Id,
                LoadNumber = job.LoadNumber,
                Status = NormalizeStatus(job.Status),
                Title = job.LoadNumber,
                Cargo = job.Cargo,
                Weight = job.ActualCargoWeightLbs > 0 ? $"{job.ActualCargoWeightLbs:0} lbs" : job.CargoWeight > 0 ? $"{job.CargoWeight:0} lbs" : "",
                Origin = job.OriginDisplay,
                OriginCompany = job.Company,
                Destination = job.DestinationDisplay,
                DestinationCompany = "",
                Miles = job.Miles > 0 ? job.Miles.ToString() : "",
                Rate = job.RatePerMile > 0 ? job.RatePerMile.ToString("C2") : "",
                Revenue = job.BestRevenue > 0 ? job.BestRevenue.ToString("C2") : "",
                AssignedDriver = FirstNonBlank(job.AssignedDriver, job.ClaimedBy),
                AssignedTruck = FirstNonBlank(job.AssignedTruck, job.LastKnownTruckName),
                Dispatcher = "OverWatch ELD Dispatch Tracker",
                Notes = job.Notes,
                IsCompanyLoad = true,
                CreatedUtc = new DateTimeOffset(DateTime.SpecifyKind(job.CreatedUtc, DateTimeKind.Utc)),
                UpdatedUtc = new DateTimeOffset(DateTime.SpecifyKind(job.UpdatedUtc, DateTimeKind.Utc)),
                ClaimedUtc = ToOffset(job.ClaimedUtc ?? job.AcceptedUtc),
                PickedUpUtc = ToOffset(job.PickedUpUtc),
                DeliveredUtc = ToOffset(job.DeliveredUtc)
            };
        }

        private static DateTimeOffset? ToOffset(DateTime? value)
        {
            if (!value.HasValue) return null;
            return new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
        }

        private static string NormalizeStatus(string? status)
        {
            var s = (status ?? "").Trim().Replace("_", " ").ToLowerInvariant();
            return s switch
            {
                "at shipper" => "Assigned",
                "bol complete" => "Picked Up",
                "picked up" => "Picked Up",
                "pickedup" => "Picked Up",
                "in transit" => "In Transit",
                "delivered" => "Delivered",
                "paid" => "Paid",
                "cancelled" => "Cancelled",
                "canceled" => "Cancelled",
                "available" => "Available",
                "unassigned" => "Available",
                "assigned" => "Assigned",
                _ => string.IsNullOrWhiteSpace(status) ? "Available" : status.Trim()
            };
        }

        private static string ResolveGuildId()
        {
            try
            {
                var cfg = VtcConfigService.Load();
                var gid = FirstNonBlank(cfg.Discord?.GuildId, cfg.GuildId);
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
                var app = System.Windows.Application.Current as App;
                var gid = Convert.ToString(app?.Session?.GuildId)?.Trim() ?? "";
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

        private static string FirstNonBlank(params string?[] values)
        {
            foreach (var value in values)
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            return "";
        }

        private static bool Same(string? a, string? b)
            => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        private sealed class PortalLoadDto
        {
            public string Id { get; set; } = "";
            public string LoadNumber { get; set; } = "";
            public string Status { get; set; } = "Available";
            public string Title { get; set; } = "";
            public string Cargo { get; set; } = "";
            public string Weight { get; set; } = "";
            public string Origin { get; set; } = "";
            public string OriginCompany { get; set; } = "";
            public string Destination { get; set; } = "";
            public string DestinationCompany { get; set; } = "";
            public string Miles { get; set; } = "";
            public string Rate { get; set; } = "";
            public string Revenue { get; set; } = "";
            public string AssignedDriver { get; set; } = "";
            public string AssignedDriverDiscordUserId { get; set; } = "";
            public string AssignedTruck { get; set; } = "";
            public string Dispatcher { get; set; } = "OverWatch ELD";
            public string Notes { get; set; } = "";
            public string BolUrl { get; set; } = "";
            public string ReceiptUrl { get; set; } = "";
            public string DiscordMessageUrl { get; set; } = "";
            public bool IsCompanyLoad { get; set; } = true;
            public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
            public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
            public DateTimeOffset? ClaimedUtc { get; set; }
            public DateTimeOffset? PickedUpUtc { get; set; }
            public DateTimeOffset? DeliveredUtc { get; set; }
        }
    }

    public sealed class LoadBoardPortalSyncResult
    {
        public int Pushed { get; set; }
        public int Failed { get; set; }
        public List<string> Errors { get; } = new();
        public bool Success => Failed == 0 && Errors.Count == 0;
        public string Summary => $"Website load sync complete. Pushed: {Pushed}. Failed: {Failed}." +
                                 (Errors.Count == 0 ? "" : "\n\n" + string.Join("\n", Errors.Take(5)));
    }
}
