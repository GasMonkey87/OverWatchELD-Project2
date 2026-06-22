// Services/VtcHttpEndpoints.cs
// ✅ FULL COPY/REPLACE (collision-proof edition)
// - Keeps only TWO overloads to avoid CS0111 conflicts:
//      1) TryHandleAsync(ctx, string apiKey, Func<string> getDriverName, Func<DispatchMessage,bool>, Func<object?>, Func<object?>)
//      2) TryHandleAsync(ctx, Func<string> getDriverName, Func<DispatchMessage,bool>, Func<object?>, Func<object?>)
// - NO Discord references
// - ELD stores announcements only
// - Includes /api/vtc/link/lookup

using System;
using System.IO;
using System.Net;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using OverWatchELD.Models;

namespace OverWatchELD.Services
{
    public static class VtcHttpEndpoints
    {
        private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

        // ✅ Overload used by callers that do NOT provide apiKey
        public static Task<bool> TryHandleAsync(
            HttpListenerContext ctx,
            Func<string> getDriverName,
            Func<DispatchMessage, bool> enqueueDispatch,
            Func<object?> getPerfSnapshot,
            Func<object?> getVtcStatus)
        {
            return TryHandleAsync(
                ctx,
                apiKey: "",
                getDriverName: getDriverName,
                enqueueDispatch: enqueueDispatch,
                getPerfSnapshot: getPerfSnapshot,
                getVtcStatus: getVtcStatus);
        }

        // ✅ Current overload (apiKey included)
        public static async Task<bool> TryHandleAsync(
            HttpListenerContext ctx,
            string apiKey,
            Func<string> getDriverName,
            Func<DispatchMessage, bool> enqueueDispatch,
            Func<object?> getPerfSnapshot,
            Func<object?> getVtcStatus)
        {
            var req = ctx.Request;
            var path = (req.Url?.AbsolutePath ?? "/").TrimEnd('/').ToLowerInvariant();

            // Phase 2: mileage leaderboard + fleet status (stored in OverWatchELD.db)
            if (path == "/api/vtc/leaderboard")
            {
                var days = 7;
                var q = (req.Url?.Query ?? "");
                foreach (var part in q.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = part.Split('=', 2);
                    if (kv.Length == 2
                        && kv[0].Equals("days", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(Uri.UnescapeDataString(kv[1]), out var d))
                        days = d;
                }

                var rows = DatabaseService.GetMileageLeaderboard(days);
                await WriteJson(ctx, 200, new
                {
                    ok = true,
                    days = days,
                    rows = rows.Select(r => new { driver = r.driverName, miles = Math.Round(r.miles, 1) }).ToArray()
                });
                return true;
            }

            if (path == "/api/vtc/fleet")
            {
                var fleet = DatabaseService.GetFleetStatus();
                await WriteJson(ctx, 200, new
                {
                    ok = true,
                    fleet = fleet.Select(f => new
                    {
                        discordUserId = f.DiscordUserId,
                        driverName = f.DriverName,
                        truck = f.TruckMakeModel,
                        odometerMiles = f.OdometerMiles,
                        fuelPct = f.FuelPct,
                        damagePct = f.DamagePct,
                        city = f.City,
                        state = f.State,
                        updatedUtc = f.UpdatedUtc.ToString("O")
                    }).ToArray()
                });
                return true;
            }

            if (!path.StartsWith("/api/vtc", StringComparison.OrdinalIgnoreCase))
                return false;

            // ---------------- Link Code: POST /api/vtc/link/code ----------------
            if (req.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) && path == "/api/vtc/link/code")
            {
                var cfg = VtcConfigService.Get();
                var driverName = (getDriverName?.Invoke() ?? "Driver").Trim();
                if (string.IsNullOrWhiteSpace(driverName)) driverName = "Driver";

                var code = VtcLinkService.GenerateCode(driverName, cfg.Linking.CodeLength, cfg.Linking.ExpiresMinutes);
                var expiresUtc = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, cfg.Linking.ExpiresMinutes));

                await WriteJson(ctx, 200, new
                {
                    ok = true,
                    code,
                    expiresUtc = expiresUtc.ToString("O"),
                    vtcName = cfg.VtcName,
                    vtcShort = cfg.VtcShort
                });
                return true;
            }

            // ---------------- Confirm: POST /api/vtc/link/confirm ----------------
            if (req.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) && path == "/api/vtc/link/confirm")
            {
                var body = await ReadBody(req);
                var p = JsonSerializer.Deserialize<LinkConfirmReq>(body, JsonOpts) ?? new LinkConfirmReq();

                var code = (p.code ?? "").Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(code))
                {
                    await WriteJson(ctx, 400, new { ok = false, error = "missing_code" });
                    return true;
                }

                if (!VtcLinkService.TryConsumeCode(code, out var driverKey, out var driverName, out var err))
                {
                    await WriteJson(ctx, 400, new { ok = false, error = err });
                    return true;
                }

                var cfg = VtcConfigService.Get();

                var discordUserId = (p.discordUserId ?? "").Trim();
                var discordUserName = (p.discordUserName ?? "").Trim();

                VtcLinkService.SaveLink(new VtcLinkService.LinkState
                {
                    Linked = true,
                    DriverKey = driverKey,
                    DriverName = driverName,
                    DiscordUserId = discordUserId,
                    DiscordUserName = discordUserName,
                    VtcName = cfg.VtcName,
                    VtcShort = cfg.VtcShort,
                    LinkedUtc = DateTimeOffset.UtcNow
                });

                try
                {
                    var gid = (cfg.Discord?.GuildId ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(gid))
                    {
                        VtcPairingStore.Save(new VtcPairingStore.Pairing
                        {
                            GuildId = gid,
                            VtcName = cfg.VtcName ?? "",
                            DiscordUserId = discordUserId,
                            DiscordUsername = discordUserName,
                            PairedUtc = DateTimeOffset.UtcNow
                        });
                    }
                }
                catch { }

                try
                {
                    var gid = (cfg.Discord?.GuildId ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(gid) || !string.IsNullOrWhiteSpace(discordUserId) || !string.IsNullOrWhiteSpace(discordUserName))
                    {
                        DiscordIdentityStore.Save(new DiscordIdentity
                        {
                            GuildId = gid,
                            DiscordUserId = discordUserId,
                            DiscordUsername = discordUserName
                        });
                    }
                }
                catch { }

                try { VtcModeService.SetVtcLocked(true); } catch { }

                await WriteJson(ctx, 200, new
                {
                    ok = true,
                    driverName = driverName,
                    driverKey = driverKey,
                    vtcName = cfg.VtcName,
                    vtcShort = cfg.VtcShort
                });
                return true;
            }

            // ---------------- Lookup: GET /api/vtc/link/lookup?discordUserId=... ----------------
            if (req.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) && path == "/api/vtc/link/lookup")
            {
                var q = (req.QueryString["discordUserId"] ?? "").Trim();

                var link = VtcLinkService.GetLink();
                var linked = link.Linked && !string.IsNullOrWhiteSpace(link.DriverKey);

                if (linked && !string.IsNullOrWhiteSpace(q) &&
                    string.Equals(q, (link.DiscordUserId ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    await WriteJson(ctx, 200, new
                    {
                        ok = true,
                        linked = true,
                        driverKey = link.DriverKey,
                        driverName = link.DriverName,
                        discordUserName = link.DiscordUserName
                    });
                    return true;
                }

                await WriteJson(ctx, 200, new
                {
                    ok = true,
                    linked = false,
                    driverKey = "",
                    driverName = "",
                    discordUserName = ""
                });
                return true;
            }

            // ---------------- Status: GET /api/vtc/status ----------------
            if (req.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) && path == "/api/vtc/status")
            {
                var cfg = VtcConfigService.Get();
                var link = VtcLinkService.GetLink();

                await WriteJson(ctx, 200, new
                {
                    ok = true,
                    enabled = cfg.Enabled,
                    vtcName = cfg.VtcName,
                    vtcShort = cfg.VtcShort,
                    locked = VtcModeService.IsVtcLocked(),
                    linked = link.Linked,
                    driverName = link.DriverName,
                    discordUserName = link.DiscordUserName
                });
                return true;
            }

            // ---------------- Dispatch Injection: POST /api/vtc/dispatch/enqueue ----------------
            if (req.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) && path == "/api/vtc/dispatch/enqueue")
            {
                var body = await ReadBody(req);
                var msg = JsonSerializer.Deserialize<DispatchMessage>(body, JsonOpts) ?? new DispatchMessage();

                if (string.IsNullOrWhiteSpace(msg.Id)) msg.Id = Guid.NewGuid().ToString("N");
                if (string.IsNullOrWhiteSpace(msg.DriverKey)) msg.DriverKey = VtcLinkService.GetDriverKey(getDriverName());
                if (msg.SentUtc == default) msg.SentUtc = DateTimeOffset.UtcNow;

                var ok = false;
                try { ok = enqueueDispatch != null && enqueueDispatch(msg); } catch { }

                await WriteJson(ctx, 200, new { ok });
                return true;
            }

            // ---------------- Announcements: GET /api/vtc/announcements ----------------
            if (req.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) && path == "/api/vtc/announcements")
            {
                var list = CompanyAnnouncementsService.LoadAll();
                await WriteJson(ctx, 200, new { ok = true, announcements = list });
                return true;
            }

            // ---------------- Announcements: POST /api/vtc/announcements/post ----------------
            if (req.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) && path == "/api/vtc/announcements/post")
            {
                var body = await ReadBody(req);

                string title = "";
                string message = "";
                bool fromDiscord = false;
                string authorOverride = "";

                try
                {
                    using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                    if (doc.RootElement.TryGetProperty("title", out var t)) title = t.GetString() ?? "";
                    if (doc.RootElement.TryGetProperty("body", out var b)) message = b.GetString() ?? "";
                    if (doc.RootElement.TryGetProperty("message", out var m)) message = m.GetString() ?? message;

                    if (doc.RootElement.TryGetProperty("fromDiscord", out var fd))
                    {
                        try
                        {
                            fromDiscord = fd.ValueKind == JsonValueKind.True ||
                                          (fd.ValueKind == JsonValueKind.String && (fd.GetString() ?? "").Equals("true", StringComparison.OrdinalIgnoreCase));
                        }
                        catch { }
                    }

                    if (doc.RootElement.TryGetProperty("author", out var au)) authorOverride = au.GetString() ?? "";
                }
                catch { }

                title = (title ?? "").Trim();
                message = (message ?? "").Trim();

                if (string.IsNullOrWhiteSpace(message) && string.IsNullOrWhiteSpace(title))
                {
                    await WriteJson(ctx, 400, new { ok = false, error = "Empty announcement." });
                    return true;
                }

                if (string.IsNullOrWhiteSpace(title))
                    title = "Company Announcement";

                var author = (!string.IsNullOrWhiteSpace(authorOverride)
                    ? authorOverride
                    : (getDriverName?.Invoke() ?? "Driver")).Trim();

                var a = CompanyAnnouncementsService.Add(title, message, author);

                await WriteJson(ctx, 200, new { ok = true, announcement = a, fromDiscord });
                return true;
            }

            // ---------------- Perf Snapshot: GET /api/vtc/performance ----------------
            if (req.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) && path == "/api/vtc/performance")
            {
                var perf = getPerfSnapshot?.Invoke();
                await WriteJson(ctx, 200, new { ok = true, data = perf });
                return true;
            }

            // ---------------- VTC Dashboard State: GET /api/vtc/dashboard ----------------
            if (req.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) && path == "/api/vtc/dashboard")
            {
                var state = getVtcStatus?.Invoke();
                await WriteJson(ctx, 200, new { ok = true, data = state });
                return true;
            }

            await WriteJson(ctx, 404, new { ok = false, error = "not_found" });
            return true;
        }

        // ---------------- Helpers ----------------

        private static async Task<string> ReadBody(HttpListenerRequest req)
        {
            try
            {
                using var sr = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
                return await sr.ReadToEndAsync().ConfigureAwait(false);
            }
            catch { return ""; }
        }

        private static async Task WriteJson(HttpListenerContext ctx, int status, object payload)
        {
            var json = JsonSerializer.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }

        private sealed class LinkConfirmReq
        {
            public string? code { get; set; }
            public string? discordUserId { get; set; }
            public string? discordUserName { get; set; }
        }
    }
}