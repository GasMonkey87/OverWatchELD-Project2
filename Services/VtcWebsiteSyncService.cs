using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using OverWatchELD.Models;

namespace OverWatchELD.Services
{
    /// <summary>
    /// Phase 7: ELD ↔ Website sync.
    /// Shared source of truth is the bot/API portal storage:
    ///   GET  /api/vtc/portal/settings?guildId=...
    ///   POST /api/vtc/portal/settings
    ///   POST /api/vtc/portal/drivers?guildId=...
    ///   POST /api/vtc/portal/fleet?guildId=...
    ///   POST /api/vtc/portal/garages?guildId=...
    /// </summary>
    public sealed class VtcWebsiteSyncService
    {
        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        public sealed class SyncResult
        {
            public bool Ok { get; set; }
            public string Message { get; set; } = "";
            public DateTimeOffset Utc { get; set; } = DateTimeOffset.UtcNow;
            public PortalSyncPayload? Payload { get; set; }

            public static SyncResult Success(string message, PortalSyncPayload? payload = null) => new SyncResult { Ok = true, Message = message, Payload = payload };
            public static SyncResult Fail(string message) => new SyncResult { Ok = false, Message = message };
        }

        public async Task<SyncResult> PullFromWebsiteAsync(CancellationToken ct = default)
        {
            try
            {
                var ctx = ResolveContext();
                if (!ctx.Ok) return SyncResult.Fail(ctx.Message);

                var url = $"{ctx.BotBaseUrl}/api/vtc/portal/settings?guildId={Uri.EscapeDataString(ctx.GuildId)}";
                using var res = await Http.GetAsync(url, ct).ConfigureAwait(false);
                var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!res.IsSuccessStatusCode)
                    return SyncResult.Fail($"Website pull failed: HTTP {(int)res.StatusCode} {body}");

                var wrapper = JsonSerializer.Deserialize<PortalSyncResponse>(body, JsonOptions);
                var payload = wrapper?.Data;
                if (payload == null)
                    return SyncResult.Fail("Website pull returned no portal data.");

                ApplyToLocalStores(payload);
                return SyncResult.Success("Website portal settings pulled into ELD.", payload);
            }
            catch (Exception ex)
            {
                return SyncResult.Fail(ex.Message);
            }
        }

        public async Task<SyncResult> PushToWebsiteAsync(CancellationToken ct = default)
        {
            try
            {
                var ctx = ResolveContext();
                if (!ctx.Ok) return SyncResult.Fail(ctx.Message);

                var payload = BuildPayloadFromLocalStores(ctx);
                var url = $"{ctx.BotBaseUrl}/api/vtc/portal/settings";
                using var res = await Http.PostAsJsonAsync(url, payload, JsonOptions, ct).ConfigureAwait(false);
                var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!res.IsSuccessStatusCode)
                    return SyncResult.Fail($"Website push failed: HTTP {(int)res.StatusCode} {body}");

                return SyncResult.Success("ELD VTC settings pushed to website.", payload);
            }
            catch (Exception ex)
            {
                return SyncResult.Fail(ex.Message);
            }
        }

        public async Task<SyncResult> PushCurrentDriverAsync(CancellationToken ct = default)
        {
            try
            {
                var ctx = ResolveContext();
                if (!ctx.Ok) return SyncResult.Fail(ctx.Message);

                var info = VtcInfoStore.Load();
                var link = VtcLinkService.GetLink();
                var driver = new PortalDriverSync
                {
                    Name = FirstNonBlank(link?.DriverName, info.DriverName, "Driver"),
                    DiscordUserId = FirstNonBlank(link?.DiscordUserId, info.DiscordUserId),
                    DiscordUsername = FirstNonBlank(link?.DiscordUserName, ""),
                    Role = "Driver",
                    AssignedTruck = info.UnitNumber,
                    Status = "Member"
                };

                var url = $"{ctx.BotBaseUrl}/api/vtc/portal/drivers?guildId={Uri.EscapeDataString(ctx.GuildId)}";
                using var res = await Http.PostAsJsonAsync(url, driver, JsonOptions, ct).ConfigureAwait(false);
                var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!res.IsSuccessStatusCode)
                    return SyncResult.Fail($"Driver sync failed: HTTP {(int)res.StatusCode} {body}");

                return SyncResult.Success("Current driver synced to website.");
            }
            catch (Exception ex)
            {
                return SyncResult.Fail(ex.Message);
            }
        }

        public async Task<SyncResult> PushCurrentTruckAsync(TelemetrySnapshot? snap = null, CancellationToken ct = default)
        {
            try
            {
                var ctx = ResolveContext();
                if (!ctx.Ok) return SyncResult.Fail(ctx.Message);

                var info = VtcInfoStore.Load();
                var link = VtcLinkService.GetLink();
                var truck = new PortalTruckSync
                {
                    TruckNumber = info.UnitNumber,
                    Name = FirstNonBlank(snap?.TruckMakeModel, info.UnitNumber, "Truck"),
                    Model = snap?.TruckMakeModel ?? "",
                    Driver = FirstNonBlank(link?.DriverName, info.DriverName, "Driver"),
                    DriverDiscordUserId = FirstNonBlank(link?.DiscordUserId, info.DiscordUserId),
                    Plate = "",
                    Odometer = snap?.OdometerMiles?.ToString("0") ?? "",
                    Location = FirstNonBlank(JoinLocation(snap?.City, snap?.State), "Unknown"),
                    Status = "Active",
                    Condition = snap?.DamagePct.HasValue == true ? Math.Max(0, 100 - snap.DamagePct.Value).ToString("0") + "%" : "",
                    Fuel = snap?.FuelPct.HasValue == true ? snap.FuelPct.Value.ToString("0") + "%" : ""
                };

                var url = $"{ctx.BotBaseUrl}/api/vtc/portal/fleet?guildId={Uri.EscapeDataString(ctx.GuildId)}";
                using var res = await Http.PostAsJsonAsync(url, truck, JsonOptions, ct).ConfigureAwait(false);
                var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!res.IsSuccessStatusCode)
                    return SyncResult.Fail($"Truck sync failed: HTTP {(int)res.StatusCode} {body}");

                return SyncResult.Success("Current truck synced to website.");
            }
            catch (Exception ex)
            {
                return SyncResult.Fail(ex.Message);
            }
        }

        public async Task<SyncResult> PushGaragesAsync(CancellationToken ct = default)
        {
            try
            {
                var ctx = ResolveContext();
                if (!ctx.Ok) return SyncResult.Fail(ctx.Message);

                var garages = VtcGarageStore.Load();
                var url = $"{ctx.BotBaseUrl}/api/vtc/garages/save?guildId={Uri.EscapeDataString(ctx.GuildId)}";
                var payload = new
                {
                    garages = garages.ConvertAll(g => new PortalGarageSync
                    {
                        Id = g.Id,
                        City = g.CityName,
                        CityName = g.CityName,
                        State = g.State,
                        Size = g.Size,
                        TruckCapacity = g.TruckCapacity,
                        Slots = g.TruckCapacity.ToString(),
                        IsOwned = g.IsOwned,
                        MapX = g.MapX,
                        MapY = g.MapY,
                        AssignedTruckNumbers = g.AssignedTruckNumbers
                    })
                };

                using var res = await Http.PostAsJsonAsync(url, payload, JsonOptions, ct).ConfigureAwait(false);
                var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!res.IsSuccessStatusCode)
                    return SyncResult.Fail($"Garage sync failed: HTTP {(int)res.StatusCode} {body}");

                return SyncResult.Success("Garages synced to website.");
            }
            catch (Exception ex)
            {
                return SyncResult.Fail(ex.Message);
            }
        }

        public async Task<SyncResult> FullPushAsync(TelemetrySnapshot? snap = null, CancellationToken ct = default)
        {
            var main = await PushToWebsiteAsync(ct).ConfigureAwait(false);
            if (!main.Ok) return main;

            await PushCurrentDriverAsync(ct).ConfigureAwait(false);
            await PushCurrentTruckAsync(snap, ct).ConfigureAwait(false);
            await PushGaragesAsync(ct).ConfigureAwait(false);

            return SyncResult.Success("Full ELD → website sync completed.", main.Payload);
        }

        private static PortalSyncPayload BuildPayloadFromLocalStores(SyncContext ctx)
        {
            var info = VtcInfoStore.Load();
            var cfg = VtcConfigService.Load();
            var settings = new AppSettingsService().Load();

            return new PortalSyncPayload
            {
                GuildId = ctx.GuildId,
                CompanyName = FirstNonBlank(info.VtcName, info.CompanyName, cfg.VtcName, settings.CompanyName),
                SiteTitle = FirstNonBlank(info.VtcName, info.CompanyName, cfg.VtcName, settings.CompanyName),
                WelcomeText = FirstNonBlank(info.Notes, "Welcome to " + FirstNonBlank(info.VtcName, info.CompanyName, cfg.VtcName, "our VTC")),
                CompanyInfo = info.Notes,
                JoinDiscordUrl = "",
                LearnMoreUrl = "",
                IsPublicDirectoryListed = true,
                IsAcceptingApplications = true,
                PublicRecruitingMessage = "",
                PublicRequirements = "",
                Drivers = new List<PortalDriverSync>
                {
                    new PortalDriverSync
                    {
                        Name = FirstNonBlank(info.DriverName, settings.DriverName, "Driver"),
                        DiscordUserId = FirstNonBlank(ctx.DiscordUserId, info.DiscordUserId),
                        Role = "Driver",
                        AssignedTruck = FirstNonBlank(info.UnitNumber, settings.TruckNumber),
                        Status = "Member"
                    }
                },
                Trucks = new List<PortalTruckSync>
                {
                    new PortalTruckSync
                    {
                        TruckNumber = FirstNonBlank(info.UnitNumber, settings.TruckNumber),
                        Name = FirstNonBlank(info.UnitNumber, settings.TruckNumber, "Truck"),
                        Driver = FirstNonBlank(info.DriverName, settings.DriverName, "Driver"),
                        DriverDiscordUserId = FirstNonBlank(ctx.DiscordUserId, info.DiscordUserId),
                        Status = "Assigned"
                    }
                }
            };
        }

        private static void ApplyToLocalStores(PortalSyncPayload payload)
        {
            var info = VtcInfoStore.Load();
            info.DiscordGuildId = FirstNonBlank(payload.GuildId, info.DiscordGuildId);
            info.VtcId = FirstNonBlank(payload.GuildId, info.VtcId);
            info.VtcName = FirstNonBlank(payload.CompanyName, payload.SiteTitle, info.VtcName);
            info.CompanyName = FirstNonBlank(payload.CompanyName, payload.SiteTitle, info.CompanyName);
            info.Notes = FirstNonBlank(payload.CompanyInfo, payload.WelcomeText, info.Notes);
            VtcInfoStore.Save(info);

            var cfg = VtcConfigService.Load();
            cfg.GuildId = FirstNonBlank(payload.GuildId, cfg.GuildId);
            cfg.VtcName = FirstNonBlank(payload.CompanyName, payload.SiteTitle, cfg.VtcName);
            cfg.Enabled = true;
            VtcConfigService.Save(cfg);

            var settings = new AppSettingsService().Load();
            settings.CompanyName = FirstNonBlank(payload.CompanyName, payload.SiteTitle, settings.CompanyName);
            new AppSettingsService().Save(settings);
        }

        private static SyncContext ResolveContext()
        {
            var cfg = VtcConfigService.Load();
            var info = VtcInfoStore.Load();
            var link = VtcLinkService.GetLink();

            var botBaseUrl = FirstNonBlank(cfg.BotApiBaseUrl, info.DiscordLockApiBaseUrl, "https://api.overwatcheld.com").TrimEnd('/');
            var guildId = FirstNonBlank(cfg.GuildId, cfg.Discord?.GuildId, info.DiscordGuildId);
            var discordUserId = FirstNonBlank(link?.DiscordUserId, info.DiscordUserId, cfg.Linking?.DiscordUserId);

            if (string.IsNullOrWhiteSpace(botBaseUrl))
                return SyncContext.Fail("Missing bot/API base URL.");

            if (string.IsNullOrWhiteSpace(guildId))
                return SyncContext.Fail("Missing guild ID. Link the ELD to a VTC first.");

            return new SyncContext
            {
                Ok = true,
                BotBaseUrl = botBaseUrl,
                GuildId = guildId,
                DiscordUserId = discordUserId
            };
        }

        private static string FirstNonBlank(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
            return "";
        }

        private static string JoinLocation(string? city, string? state)
        {
            city = (city ?? "").Trim();
            state = (state ?? "").Trim();
            if (string.IsNullOrWhiteSpace(city)) return state;
            if (string.IsNullOrWhiteSpace(state)) return city;
            return city + ", " + state;
        }

        private sealed class SyncContext
        {
            public bool Ok { get; set; }
            public string Message { get; set; } = "";
            public string BotBaseUrl { get; set; } = "";
            public string GuildId { get; set; } = "";
            public string DiscordUserId { get; set; } = "";

            public static SyncContext Fail(string message) => new SyncContext { Ok = false, Message = message };
        }
    }

    public sealed class PortalSyncResponse
    {
        public bool Ok { get; set; }
        public PortalSyncPayload? Data { get; set; }
    }

    public sealed class PortalSyncPayload
    {
        public string GuildId { get; set; } = "";
        public string SiteTitle { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string LogoImageUrl { get; set; } = "";
        public string CompanyPictureUrl { get; set; } = "";
        public string BannerImageUrl { get; set; } = "";
        public string WelcomeText { get; set; } = "";
        public string CompanyInfo { get; set; } = "";
        public string HeroImageUrl { get; set; } = "";
        public string JoinDiscordUrl { get; set; } = "";
        public string LearnMoreUrl { get; set; } = "";
        public bool IsPublicDirectoryListed { get; set; } = true;
        public bool IsAcceptingApplications { get; set; } = true;
        public string PublicRecruitingMessage { get; set; } = "";
        public string PublicRequirements { get; set; } = "";
        public List<PortalApplicationQuestionSync> ApplicationQuestions { get; set; } = new List<PortalApplicationQuestionSync>();
        public List<PortalDriverSync> Drivers { get; set; } = new List<PortalDriverSync>();
        public List<PortalDriverSync> FeaturedDrivers { get; set; } = new List<PortalDriverSync>();
        public List<string> SlideshowImages { get; set; } = new List<string>();
        public List<PortalDriverSync> ManagementTeam { get; set; } = new List<PortalDriverSync>();
        public string SelectedFeaturedDriver { get; set; } = "";
        public List<PortalTruckSync> Trucks { get; set; } = new List<PortalTruckSync>();
        public List<PortalGarageSync> Garages { get; set; } = new List<PortalGarageSync>();
    }

    public sealed class PortalApplicationQuestionSync
    {
        public string Id { get; set; } = "";
        public string Question { get; set; } = "";
        public string Type { get; set; } = "textarea";
        public bool Required { get; set; } = true;
    }

    public sealed class PortalDriverSync
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Role { get; set; } = "Driver";
        public string Bio { get; set; } = "";
        public string ImageUrl { get; set; } = "";
        public string DiscordUserId { get; set; } = "";
        public string DiscordUsername { get; set; } = "";
        public string DiscordAvatarUrl { get; set; } = "";
        public string FavoriteTruck { get; set; } = "";
        public string AssignedTruck { get; set; } = "";
        public string Mileage { get; set; } = "";
        public string TotalMiles { get; set; } = "";
        public string MonthlyMiles { get; set; } = "";
        public string Achievement { get; set; } = "";
        public string Status { get; set; } = "Member";
        public string YearsInVtc { get; set; } = "";
    }

    public sealed class PortalTruckSync
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

    public sealed class PortalGarageSync
    {
        public string Id { get; set; } = "";
        public string City { get; set; } = "";
        public string State { get; set; } = "";
        public string Country { get; set; } = "";
        public string Slots { get; set; } = "";
        public string Cost { get; set; } = "";
        public string PurchasedBy { get; set; } = "";
        public string PurchasedUtc { get; set; } = "";
        public string Notes { get; set; } = "";
        public string CityToken { get; set; } = "";
        public string CityName { get; set; } = "";
        public string Size { get; set; } = "Small";
        public int TruckCapacity { get; set; } = 3;
        public bool IsOwned { get; set; }
        public double? MapX { get; set; }
        public double? MapY { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public List<string> AssignedTruckNumbers { get; set; } = new List<string>();
    }
}
