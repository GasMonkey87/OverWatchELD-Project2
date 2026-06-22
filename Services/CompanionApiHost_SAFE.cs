using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using SQLitePCL;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using static System.Net.Mime.MediaTypeNames;
using static System.Runtime.InteropServices.JavaScript.JSType;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Tab;
using OverWatchELD.Services;


namespace OverWatchELD.Services
{
    public static class CompanionApiHostSafe
    {
        private const string V = "sleeperberth";
        private static readonly object _sync = new();
        private static IHost? _host;

        private static object? _telemetrySvc;
        private static object? _dutySvc;
        private static object? _inspectionSvc;
        private static object? _dispatchInboxSvc;
        private static object? _vtcSvc;
        private static object? _mainWindowOrShell;

        private static string _webRoot = "";
        private static DateTime _startedUtc;
        private static bool _configured;

        public static int Port { get; private set; } = 5234;

        public static bool IsRunning
        {
            get
            {
                lock (_sync)
                    return _host != null;
            }
        }

        private static string ReadObjString(object? obj, params string[] names)
        {
            if (obj == null)
                return "";

            foreach (var name in names)
            {
                try
                {
                    var prop = obj.GetType().GetProperty(
                        name,
                        BindingFlags.Public |
                        BindingFlags.Instance |
                        BindingFlags.IgnoreCase);

                    var val = prop?.GetValue(obj);

                    if (val != null)
                    {
                        var s = val.ToString();

                        if (!string.IsNullOrWhiteSpace(s))
                            return s.Trim();
                    }
                }
                catch
                {
                }
            }

            return "";
        }

        private static DateTimeOffset ReadObjDate(object? obj, string name)
        {
            if (obj == null)
                return DateTimeOffset.MinValue;

            try
            {
                var prop = obj.GetType().GetProperty(
                    name,
                    BindingFlags.Public |
                    BindingFlags.Instance |
                    BindingFlags.IgnoreCase);

                var val = prop?.GetValue(obj);

                if (val is DateTimeOffset dto)
                    return dto;

                if (val is DateTime dt)
                    return new DateTimeOffset(dt);
            }
            catch
            {
            }

            return DateTimeOffset.MinValue;
        }

        public static void Configure(
            object? telemetryService = null,
            object? dutyService = null,
            object? inspectionService = null,
            object? dispatchInboxService = null,
            object? vtcService = null,
            object? mainWindowOrShell = null,
            string? webRoot = null)
        {
            lock (_sync)
            {
                _telemetrySvc = telemetryService;
                _dutySvc = dutyService;
                _inspectionSvc = inspectionService;
                _dispatchInboxSvc = dispatchInboxService;
                _vtcSvc = vtcService;
                _mainWindowOrShell = mainWindowOrShell;
                _webRoot = ResolveWebRoot(webRoot);
                _configured = true;
            }
        }

        public static void Start(int port = 5234)
        {
            lock (_sync)
            {
                if (_host != null)
                    return;

                if (!_configured)
                    Configure();

                EnsureDefaultWebFiles(_webRoot);
                try { _ = AtsModScannerService.GetResolvedDefinitions(); } catch { }
                Port = port;
                _startedUtc = DateTime.UtcNow;

                var root = _webRoot;

                var mainWwwRoot =
                    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "wwwroot"));

                var mapsRoot =
                    Path.Combine(mainWwwRoot, "maps");

                Directory.CreateDirectory(mapsRoot);

                _host = Host.CreateDefaultBuilder()
                    .ConfigureWebHostDefaults(webBuilder =>
                    {
                        webBuilder.UseKestrel(options =>
                        {
                            options.ListenAnyIP(port);
                        });

                        webBuilder.UseWebRoot(root);

                        webBuilder.Configure(app =>
                        {
                            app.Use(async (ctx, next) =>
                            {
                                ctx.Response.Headers["Cache-Control"] = "no-store";
                                ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
                                ctx.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
                                ctx.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type";

                                if (string.Equals(ctx.Request.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
                                {
                                    ctx.Response.StatusCode = StatusCodes.Status204NoContent;
                                    return;
                                }

                                await next().ConfigureAwait(false);
                            });
                            app.UseStaticFiles(new StaticFileOptions
                            {
                                RequestPath = "/maps",
                                FileProvider = new PhysicalFileProvider(mapsRoot),
                                ServeUnknownFileTypes = true
                            });
                            app.UseDefaultFiles(new DefaultFilesOptions
                            {
                                FileProvider = new PhysicalFileProvider(root)
                            });

                            app.UseStaticFiles(new StaticFileOptions
                            {
                                FileProvider = new PhysicalFileProvider(root),
                                ServeUnknownFileTypes = true
                            });

                            app.UseRouting();

                            app.UseEndpoints(endpoints =>
                            {
                                endpoints.MapGet("/health", async ctx =>
                                {
                                    await WriteJsonAsync(ctx, StatusCodes.Status200OK, new
                                    {
                                        ok = true,
                                        running = IsRunning,
                                        port = Port,
                                        startedUtc = _startedUtc,
                                        uptimeSeconds = Math.Max(0, (int)(DateTime.UtcNow - _startedUtc).TotalSeconds)
                                    }).ConfigureAwait(false);
                                });

                                endpoints.MapGet("/maps/{fileName}", async ctx =>
                                {
                                    var fileName = ctx.Request.RouteValues["fileName"]?.ToString() ?? "";

                                    var candidates = new[]
                                    {
        Path.Combine(_webRoot, "maps", fileName),
        Path.Combine(_webRoot, "companion", "maps", fileName),
        Path.Combine(AppContext.BaseDirectory, "wwwroot", "maps", fileName),
        Path.Combine(AppContext.BaseDirectory, "wwwroot", "companion", "maps", fileName),
        Path.Combine(AppContext.BaseDirectory, "maps", fileName)
    };

                                    var filePath = candidates.FirstOrDefault(File.Exists);

                                    if (filePath == null)
                                    {
                                        ctx.Response.StatusCode = 404;
                                        await ctx.Response.WriteAsync(
                                            "Map file not found.\n\nChecked:\n" +
                                            string.Join("\n", candidates));
                                        return;
                                    }

                                    ctx.Response.ContentType =
                                        fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                                            ? "application/json"
                                            : fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                                                ? "image/png"
                                                : "application/octet-stream";

                                    await ctx.Response.SendFileAsync(filePath);
                                });

                                endpoints.MapGet("/data/{fileName}", async ctx =>
                                {
                                    var fileName = ctx.Request.RouteValues["fileName"]?.ToString() ?? "";

                                    var candidates = new[]
                                    {
        Path.Combine(_webRoot, "data", fileName),
        Path.Combine(_webRoot, "companion", "data", fileName),
        Path.Combine(AppContext.BaseDirectory, "wwwroot", "data", fileName),
        Path.Combine(AppContext.BaseDirectory, "wwwroot", "companion", "data", fileName)
    };

                                    var filePath = candidates.FirstOrDefault(File.Exists);

                                    if (filePath == null)
                                    {
                                        ctx.Response.StatusCode = 404;
                                        await ctx.Response.WriteAsync("Data file not found.");
                                        return;
                                    }

                                    ctx.Response.ContentType = "application/json";

                                    await ctx.Response.SendFileAsync(filePath);
                                });

                                endpoints.MapGet("/api/companion/discovery", async ctx =>
                                {
                                    var urls = GetReachableUrls();
                                    var bestUrl =
                                        urls.FirstOrDefault(x =>
                                            x.StartsWith("http://192.168.", StringComparison.OrdinalIgnoreCase) ||
                                            x.StartsWith("http://10.", StringComparison.OrdinalIgnoreCase) ||
                                            x.StartsWith("http://172.", StringComparison.OrdinalIgnoreCase))
                                        ?? urls.FirstOrDefault(x => !x.Contains("127.0.0.1") && !x.Contains("localhost"))
                                        ?? $"http://127.0.0.1:{Port}/";

                                    await WriteJsonAsync(ctx, StatusCodes.Status200OK, new
                                    {
                                        ok = true,
                                        app = "OverWatch ELD",
                                        machine = Environment.MachineName,
                                        port = Port,
                                        bestUrl = bestUrl.TrimEnd('/'),
                                        urls,
                                        startedUtc = _startedUtc
                                    }).ConfigureAwait(false);
                                });

                                endpoints.MapGet("/api/companion/info", async ctx =>
                                {
                                    await WriteJsonAsync(ctx, StatusCodes.Status200OK, new
                                    {
                                        ok = true,
                                        port = Port,
                                        urls = GetReachableUrls(),
                                        startedUtc = _startedUtc,
                                        webRoot = _webRoot,
                                        services = new
                                        {
                                            telemetry = _telemetrySvc != null,
                                            duty = _dutySvc != null,
                                            inspection = _inspectionSvc != null,
                                            dispatchInbox = _dispatchInboxSvc != null,
                                            vtc = _vtcSvc != null,
                                            shell = _mainWindowOrShell != null
                                        }
                                    }).ConfigureAwait(false);
                                });

                                endpoints.MapGet("/api/dashboard", async ctx =>
                                {
                                    var dashboard = await BuildCompanionDashboardAsync().ConfigureAwait(false);
                                    await WriteJsonAsync(ctx, StatusCodes.Status200OK, dashboard).ConfigureAwait(false);
                                });

                                endpoints.MapGet("/api/telemetry", async ctx =>
                                {
                                    var telemetry = BuildMergedTelemetrySnapshot();
                                    await WriteJsonAsync(ctx, StatusCodes.Status200OK, new
                                    {
                                        ok = true,
                                        telemetry
                                    }).ConfigureAwait(false);
                                });

                                endpoints.MapGet("/api/telemetry/raw", async ctx =>
                                {
                                    var raw = SafeGetTelemetryObject();
                                    var lastSnapshot = GetMemberValue(raw, "lastSnapshot");
                                    await WriteJsonAsync(ctx, StatusCodes.Status200OK, new
                                    {
                                        ok = true,
                                        telemetry = raw
                                    }).ConfigureAwait(false);
                                });

                                endpoints.MapPost("/api/connect-truck", async ctx =>
                                {
                                    try
                                    {
                                        var telemetry = BuildMergedTelemetrySnapshot();
                                        var sourceObj = GetMemberValue(telemetry, "source");
                                        var lastSnapshot = GetMemberValue(sourceObj, "lastSnapshot");
                                        var lastRawJson = ToNullableString(GetMemberValue(sourceObj, "lastRawJson"));

                                        string First(params string?[] vals)
                                        {
                                            foreach (var v in vals)
                                            {
                                                if (!string.IsNullOrWhiteSpace(v))
                                                    return v.Trim();
                                            }

                                            return "";
                                        }

                                        string rawTrailerName = "";
                                        string rawTrailerId = "";
                                        string rawCargoName = "";
                                        string rawSourceCity = "";
                                        string rawSourceCompany = "";
                                        string rawDestinationCity = "";
                                        string rawDestinationCompany = "";
                                        string rawIncome = "";

                                        try
                                        {
                                            if (!string.IsNullOrWhiteSpace(lastRawJson))
                                            {
                                                using var doc = JsonDocument.Parse(lastRawJson);
                                                var root = doc.RootElement;

                                                if (root.TryGetProperty("trailer", out var trailerEl))
                                                {
                                                    if (trailerEl.TryGetProperty("name", out var n))
                                                        rawTrailerName = JsonElementToString(n);

                                                    if (trailerEl.TryGetProperty("id", out var id))
                                                        rawTrailerId = JsonElementToString(id);
                                                }

                                                if (root.TryGetProperty("job", out var jobEl))
                                                {
                                                    if (jobEl.TryGetProperty("cargo", out var cargo))
                                                        rawCargoName = JsonElementToString(cargo);

                                                    if (jobEl.TryGetProperty("cargoName", out var cargoName))
                                                        rawCargoName = First(rawCargoName, JsonElementToString(cargoName));

                                                    if (jobEl.TryGetProperty("sourceCity", out var sc))
                                                        rawSourceCity = JsonElementToString(sc);

                                                    if (jobEl.TryGetProperty("sourceCompany", out var sco))
                                                        rawSourceCompany = JsonElementToString(sco);

                                                    if (jobEl.TryGetProperty("destinationCity", out var dc))
                                                        rawDestinationCity = JsonElementToString(dc);

                                                    if (jobEl.TryGetProperty("destinationCompany", out var dco))
                                                        rawDestinationCompany = JsonElementToString(dco);

                                                    if (jobEl.TryGetProperty("income", out var income))
                                                        rawIncome = JsonElementToString(income);
                                                }
                                            }
                                        }
                                        catch
                                        {
                                        }

                                        var fleetStore = new OverWatchELD.Services.Fleet.FleetCommandStore();
                                        var trucks = fleetStore.LoadAll()?.ToList();

                                        if (trucks == null)
                                        {
                                            trucks = new List<OverWatchELD.Models.Fleet.FleetCommandTruck>();
                                        }

                                        OverWatchELD.Models.Fleet.FleetCommandTruck? matchedTruck = null;

                                        var telemetryTruck = First(
                                            ToNullableString(GetMemberValue(lastSnapshot, "truckName")),
                                            ToNullableString(GetMemberValue(lastSnapshot, "truckMakeModel")),
                                            ToNullableString(GetMemberValue(telemetry, "truckName")),
                                            ToNullableString(GetMemberValue(telemetry, "truckMakeModel")),
                                            ToNullableString(GetMemberValue(telemetry, "truck")));

                                        if (!string.IsNullOrWhiteSpace(telemetryTruck))
                                        {
                                            matchedTruck = trucks.FirstOrDefault(t =>
                                            {
                                                var tn = ReadObjString(t, "TruckName");
                                                var model = ReadObjString(t, "Model");
                                                var num = ReadObjString(t, "TruckNumber");

                                                return string.Equals(tn, telemetryTruck, StringComparison.OrdinalIgnoreCase)
                                                    || string.Equals(model, telemetryTruck, StringComparison.OrdinalIgnoreCase)
                                                    || string.Equals(num, telemetryTruck, StringComparison.OrdinalIgnoreCase);
                                            });
                                        }

                                        if (matchedTruck == null)
                                        {
                                            matchedTruck = trucks
                                                .OrderByDescending(t => ReadObjDate(t, "UpdatedUtc"))
                                                .FirstOrDefault();
                                        }

                                        var routeLeft = First(
                                            rawSourceCity,
                                            ToNullableString(GetMemberValue(lastSnapshot, "sourceCity")));

                                        var routeRight = First(
                                            rawDestinationCity,
                                            ToNullableString(GetMemberValue(lastSnapshot, "destinationCity")));

                                        var finalRoute = !string.IsNullOrWhiteSpace(routeLeft) && !string.IsNullOrWhiteSpace(routeRight)
                                            ? routeLeft + " → " + routeRight
                                            : "";

                                        var finalTruckName = First(
                                            ToNullableString(GetMemberValue(lastSnapshot, "truckName")),
                                            ToNullableString(GetMemberValue(lastSnapshot, "truckMakeModel")),
                                            ReadObjString(matchedTruck, "TruckName"),
                                            ReadObjString(matchedTruck, "Name"),
                                            ReadObjString(matchedTruck, "DisplayName"),
                                            ReadObjString(matchedTruck, "Model"),
                                            telemetryTruck,
                                            "Current Truck Not Detected");

                                        var finalTruckId = First(
                                            ToNullableString(GetMemberValue(lastSnapshot, "truckId")),
                                            ToNullableString(GetMemberValue(telemetry, "truckId")),
                                            ToNullableString(GetMemberValue(telemetry, "unitNumber")),
                                            ReadObjString(matchedTruck, "TruckNumber"),
                                            ReadObjString(matchedTruck, "PlateNumber"),
                                            "No ID");

                                        var finalTrailer = First(
                                            rawTrailerName,
                                            rawTrailerId,
                                            ToNullableString(GetMemberValue(lastSnapshot, "trailerName")),
                                            ToNullableString(GetMemberValue(lastSnapshot, "trailer")),
                                            ToNullableString(GetMemberValue(lastSnapshot, "trailerId")),
                                            ToNullableString(GetMemberValue(telemetry, "trailer")),
                                            ToNullableString(GetMemberValue(telemetry, "trailerName")),
                                            ToNullableString(GetMemberValue(telemetry, "trailerId")),
                                            ReadObjString(matchedTruck, "Trailer"),
                                            ReadObjString(matchedTruck, "TrailerName"),
                                            ReadObjString(matchedTruck, "CurrentTrailer"),
                                            "No trailer detected");

                                        var finalCargo = First(
                                            rawCargoName,
                                            rawTrailerName,
                                            ToNullableString(GetMemberValue(lastSnapshot, "cargo")),
                                            ToNullableString(GetMemberValue(lastSnapshot, "cargoName")),
                                            ToNullableString(GetMemberValue(lastSnapshot, "loadCargo")),
                                            ToNullableString(GetMemberValue(lastSnapshot, "jobCargo")),
                                            ToNullableString(GetMemberValue(telemetry, "cargo")),
                                            ToNullableString(GetMemberValue(telemetry, "cargoName")),
                                            ToNullableString(GetMemberValue(telemetry, "loadCargo")),
                                            ToNullableString(GetMemberValue(telemetry, "currentCargo")),
                                            "No cargo detected");

                                        var fuelPct = ToNullableDouble(GetMemberValue(lastSnapshot, "fuelPct"));

                                        var finalFuel = First(
                                            fuelPct.HasValue ? Math.Round(fuelPct.Value, 1).ToString(CultureInfo.InvariantCulture) + "%" : null,
                                            ToNullableString(GetMemberValue(lastSnapshot, "fuelPercent")),
                                            ToNullableString(GetMemberValue(lastSnapshot, "fuel")),
                                            ToNullableString(GetMemberValue(telemetry, "fuelPercent")),
                                            ToNullableString(GetMemberValue(telemetry, "fuel")),
                                            ReadObjString(matchedTruck, "FuelPercent"),
                                            ReadObjString(matchedTruck, "Fuel"),
                                            "—");

                                        var lat = ToNullableDouble(GetMemberValue(lastSnapshot, "gpsLatitude"))
                                            ?? ToNullableDouble(GetMemberValue(lastSnapshot, "latitude"))
                                            ?? ToNullableDouble(GetMemberValue(telemetry, "latitude"));

                                        var lon = ToNullableDouble(GetMemberValue(lastSnapshot, "gpsLongitude"))
                                            ?? ToNullableDouble(GetMemberValue(lastSnapshot, "longitude"))
                                            ?? ToNullableDouble(GetMemberValue(telemetry, "longitude"));

                                        var finalCoords = lat.HasValue && lon.HasValue
                                            ? Math.Round(lat.Value, 5).ToString(CultureInfo.InvariantCulture) + ", " + Math.Round(lon.Value, 5).ToString(CultureInfo.InvariantCulture)
                                            : First(
                                                ToNullableString(GetMemberValue(lastSnapshot, "locationText")),
                                                ToNullableString(GetMemberValue(lastSnapshot, "city")),
                                                ToNullableString(GetMemberValue(telemetry, "coordinates")),
                                                ToNullableString(GetMemberValue(telemetry, "location")),
                                                "—");

                                        await WriteJsonAsync(ctx, 200, new
                                        {
                                            ok = true,
                                            truckName = finalTruckName,
                                            truckId = finalTruckId,
                                            trailer = finalTrailer,
                                            cargo = finalCargo,
                                            route = finalRoute,
                                            sourceCompany = rawSourceCompany,
                                            destinationCompany = rawDestinationCompany,
                                            income = rawIncome,
                                            fuel = finalFuel,
                                            coordinates = finalCoords
                                        }).ConfigureAwait(false);
                                    }
                                    catch (Exception ex)
                                    {
                                        await WriteJsonAsync(ctx, 500, new
                                        {
                                            ok = false,
                                            error = ex.Message
                                        }).ConfigureAwait(false);
                                    }
                                });

                                endpoints.MapGet("/api/debug/telemetry-fields", async ctx =>
                                {
                                    var telemetry = BuildMergedTelemetrySnapshot();

                                    var fields = new Dictionary<string, object?>();

                                    if (telemetry != null)
                                    {
                                        foreach (var p in telemetry.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                                        {
                                            try
                                            {
                                                fields[p.Name] = p.GetValue(telemetry);
                                            }
                                            catch
                                            {
                                            }
                                        }
                                    }

                                    await WriteJsonAsync(ctx, 200, new
                                    {
                                        ok = true,
                                        type = telemetry?.GetType().FullName,
                                        fields
                                    });
                                });

                                endpoints.MapGet("/api/duty", async ctx =>
                                {
                                    var duty = await BuildMergedDutySnapshotAsync().ConfigureAwait(false);

                                    await WriteJsonAsync(ctx, StatusCodes.Status200OK, new
                                    {
                                        ok = true,
                                        duty
                                    }).ConfigureAwait(false);
                                });

                                endpoints.MapPost("/api/duty", async ctx =>
                                {
                                    var bodyText = await ReadBodyAsStringAsync(ctx.Request).ConfigureAwait(false);
                                    var parsed = ParseIncomingBody(bodyText, ctx.Request.ContentType);

                                    string dutyValue =
                                        GetStringCaseInsensitive(parsed, "duty") ??
                                        GetStringCaseInsensitive(parsed, "status") ??
                                        GetStringCaseInsensitive(parsed, "mode") ??
                                        bodyText?.Trim() ??
                                        "";

                                    if (string.IsNullOrWhiteSpace(dutyValue))
                                    {
                                        await WriteJsonAsync(ctx, StatusCodes.Status400BadRequest, new
                                        {
                                            ok = false,
                                            error = "missing_duty"
                                        }).ConfigureAwait(false);
                                        return;
                                    }

                                    var success = await SafeSetDutyAsync(dutyValue).ConfigureAwait(false);
                                    var updated = await BuildMergedDutySnapshotAsync().ConfigureAwait(false);
                                    var synced = CompanionStateBridgeSafe.GetStateSnapshot();

                                    await WriteJsonAsync(ctx,
                                        success ? StatusCodes.Status200OK : StatusCodes.Status500InternalServerError,
                                        new
                                        {
                                            ok = success,
                                            duty = dutyValue,
                                            snapshot = updated,
                                            synced
                                        }).ConfigureAwait(false);
                                });

                                endpoints.MapGet("/api/sync/state", async ctx =>
                                {
                                    await WriteJsonAsync(ctx, StatusCodes.Status200OK,
                                        CompanionStateBridgeSafe.GetStateSnapshot()).ConfigureAwait(false);
                                });

                                endpoints.MapGet("/api/logs/today", async ctx =>
                                {
                                    await WriteJsonAsync(ctx, StatusCodes.Status200OK,
                                        CompanionStateBridgeSafe.GetTodayLogsSnapshot()).ConfigureAwait(false);
                                });

                                endpoints.MapGet("/api/logs", async ctx =>
                                {
                                    await WriteJsonAsync(ctx, StatusCodes.Status200OK,
                                        CompanionStateBridgeSafe.GetTodayLogsSnapshot()).ConfigureAwait(false);
                                });


                                endpoints.MapGet("/api/local/vtc/config", async ctx =>
                                {
                                    try
                                    {
                                        object? cfg = null;

                                        try
                                        {
                                            cfg = typeof(VtcConfigService)
                                                .GetMethod("Load", BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase)
                                                ?.Invoke(null, null);
                                        }
                                        catch
                                        {
                                        }

                                        string guildId =
    ReadObjString(cfg, "GuildId", "GuildID", "ServerId", "DiscordGuildId", "VtcGuildId");

                                        if (string.IsNullOrWhiteSpace(guildId))
                                        {
                                            try
                                            {
                                                var pairing = VtcPairingStore.Load();
                                                guildId = ReadObjString(pairing, "GuildId", "GuildID", "ServerId", "DiscordGuildId", "VtcGuildId");
                                            }
                                            catch { }
                                        }

                                        string vtcName =
                                            ReadObjString(cfg, "VtcName", "CompanyName", "Name", "GuildName", "ServerName");

                                        string botBaseUrl =
                                            ReadObjString(cfg, "BotBaseUrl", "BaseUrl", "ApiBaseUrl", "BotUrl", "ServerUrl");

                                        if (string.IsNullOrWhiteSpace(botBaseUrl))
                                            botBaseUrl = "https://overwatcheld.up.railway.app";

                                        await WriteJsonAsync(ctx, 200, new
                                        {
                                            ok = true,
                                            guildId,
                                            vtcName,
                                            botBaseUrl
                                        }).ConfigureAwait(false);
                                    }
                                    catch (Exception ex)
                                    {
                                        await WriteJsonAsync(ctx, 500, new
                                        {
                                            ok = false,
                                            error = ex.Message
                                        }).ConfigureAwait(false);
                                    }
                                });

                                endpoints.MapGet("/api/vtc/servers", async ctx =>
                                {
                                    try
                                    {
                                        object? cfg = null;

                                        try
                                        {
                                            cfg = typeof(VtcConfigService)
                                                .GetMethod("Load", BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase)
                                                ?.Invoke(null, null);
                                        }
                                        catch
                                        {
                                        }

                                        string guildId =
                                            ReadObjString(cfg, "GuildId", "GuildID", "ServerId", "DiscordGuildId", "VtcGuildId");

                                        string vtcName =
                                            ReadObjString(cfg, "VtcName", "CompanyName", "Name", "GuildName", "ServerName");

                                        if (string.IsNullOrWhiteSpace(guildId))
                                        {
                                            await WriteJsonAsync(ctx, 200, new
                                            {
                                                ok = true,
                                                servers = Array.Empty<object>()
                                            }).ConfigureAwait(false);
                                            return;
                                        }

                                        await WriteJsonAsync(ctx, 200, new
                                        {
                                            ok = true,
                                            servers = new[]
                                            {
                                                new
                                                {
                                                    id = guildId,
                                                    guildId,
                                                    serverId = guildId,
                                                    name = string.IsNullOrWhiteSpace(vtcName) ? "Linked VTC" : vtcName
                                                }
                                            }
                                        }).ConfigureAwait(false);
                                    }
                                    catch (Exception ex)
                                    {
                                        await WriteJsonAsync(ctx, 500, new
                                        {
                                            ok = false,
                                            error = ex.Message,
                                            servers = Array.Empty<object>()
                                        }).ConfigureAwait(false);
                                    }
                                });

                                endpoints.MapGet("/api/local/vtc/garages", async ctx =>
                                {
                                    try
                                    {
                                        var garages = VtcGarageStore.Load()
                                            .Where(g => g.IsOwned)
                                            .Select(g => new
                                            {
                                                id = g.Id,
                                                city = g.CityName,
                                                state = g.State,
                                                size = g.Size,
                                                capacity = g.TruckCapacity,
                                                assigned = g.AssignedTruckNumbers.Count,
                                                trucks = g.AssignedTruckNumbers,
                                                owned = g.IsOwned,
                                                homeGarage = g.IsHomeGarage,
                                                hasFuelStation = g.HasFuelStation,
                                                repairBays = g.RepairBays,
                                                incomeBonus = g.GarageIncomeBonusPercent,
                                                mapX = g.MapX,
                                                mapY = g.MapY
                                            });

                                        await WriteJsonAsync(ctx, 200, new
                                        {
                                            ok = true,
                                            garages
                                        }).ConfigureAwait(false);
                                    }
                                    catch (Exception ex)
                                    {
                                        await WriteJsonAsync(ctx, 500, new
                                        {
                                            ok = false,
                                            error = ex.Message
                                        }).ConfigureAwait(false);
                                    }
                                });

                                endpoints.MapGet("/api/inspection", async ctx =>
                                {
                                    var mode = (ctx.Request.Query["mode"].FirstOrDefault() ?? "pretrip").Trim();
                                    var inspection = await SafeGetInspectionSnapshotAsync(mode).ConfigureAwait(false);

                                    await WriteJsonAsync(ctx, StatusCodes.Status200OK, new
                                    {
                                        ok = true,
                                        mode,
                                        inspection
                                    }).ConfigureAwait(false);
                                });

                                endpoints.MapPost("/api/inspection", async ctx =>
                                {
                                    var mode = (ctx.Request.Query["mode"].FirstOrDefault() ?? "pretrip").Trim();
                                    var bodyText = await ReadBodyAsStringAsync(ctx.Request).ConfigureAwait(false);
                                    var parsed = ParseIncomingBody(bodyText, ctx.Request.ContentType);

                                    var success = await SafeSaveInspectionAsync(mode, parsed, bodyText).ConfigureAwait(false);

                                    await WriteJsonAsync(ctx,
                                        success ? StatusCodes.Status200OK : StatusCodes.Status500InternalServerError,
                                        new
                                        {
                                            ok = success,
                                            mode,
                                            inspection = await SafeGetInspectionSnapshotAsync(mode).ConfigureAwait(false)
                                        }).ConfigureAwait(false);
                                });

                                endpoints.MapGet("/api/messages", async ctx =>
                                {
                                    var messages = await SafeGetMessagesAsync().ConfigureAwait(false);
                                    await WriteJsonAsync(ctx, StatusCodes.Status200OK, messages).ConfigureAwait(false);
                                });

                                endpoints.MapGet("/api/messages/conversations", async ctx =>
                                {
                                    var conversations = await SafeGetConversationsAsync().ConfigureAwait(false);
                                    await WriteJsonAsync(ctx, StatusCodes.Status200OK, conversations).ConfigureAwait(false);
                                });



                                endpoints.MapPost("/api/messages/send", async ctx =>
                                {
                                    var bodyText = await ReadBodyAsStringAsync(ctx.Request).ConfigureAwait(false);
                                    var parsed = ParseIncomingBody(bodyText, ctx.Request.ContentType);

                                    string text =
                                        GetStringCaseInsensitive(parsed, "text") ??
                                        GetStringCaseInsensitive(parsed, "message") ??
                                        GetStringCaseInsensitive(parsed, "content") ??
                                        "";

                                    string? to =
                                        GetStringCaseInsensitive(parsed, "to") ??
                                        GetStringCaseInsensitive(parsed, "recipient");

                                    if (string.IsNullOrWhiteSpace(text))
                                    {
                                        await WriteJsonAsync(ctx, StatusCodes.Status400BadRequest, new
                                        {
                                            ok = false,
                                            error = "missing_text"
                                        }).ConfigureAwait(false);
                                        return;
                                    }

                                    var sent = await SafeSendMessageAsync(text, to, parsed).ConfigureAwait(false);

                                    await WriteJsonAsync(ctx,
                                        sent ? StatusCodes.Status200OK : StatusCodes.Status500InternalServerError,
                                        new
                                        {
                                            ok = sent,
                                            text,
                                            to
                                        }).ConfigureAwait(false);
                                });
                                endpoints.MapGet("/api/loadboard/options", async ctx =>
                                {
                                    var payload = SafeReadLoadBoardOptions();
                                    await WriteJsonAsync(ctx, StatusCodes.Status200OK, payload).ConfigureAwait(false);
                                });

                                endpoints.MapGet("/api/loadboard", async ctx =>
                                {
                                    var payload = SafeReadLoadBoardOptions();
                                    await WriteJsonAsync(ctx, StatusCodes.Status200OK, new
                                    {
                                        ok = true,
                                        generatedUtc = DateTime.UtcNow,
                                        options = payload
                                    }).ConfigureAwait(false);
                                });

                                endpoints.MapPost("/api/loadboard/create", async ctx =>
                                {
                                    var bodyText = await ReadBodyAsStringAsync(ctx.Request).ConfigureAwait(false);
                                    var parsed = ParseIncomingBody(bodyText, ctx.Request.ContentType);
                                    var objParsed = parsed.ToDictionary(
    k => k.Key,
    v => (object?)v.Value,
    StringComparer.OrdinalIgnoreCase
);
                                    var result = await SafeCreateLoadBoardLoadAsync(objParsed, bodyText).ConfigureAwait(false);

                                    await WriteJsonAsync(
                                        ctx,
                                        result.Ok ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest,
                                        result.Payload).ConfigureAwait(false);
                                });
                            });

                            app.Run(async ctx =>
                            {
                                if (!ctx.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) &&
                                    !ctx.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
                                {
                                    var indexPath = Path.Combine(root, "index.html");
                                    if (File.Exists(indexPath))
                                    {
                                        ctx.Response.StatusCode = StatusCodes.Status200OK;
                                        ctx.Response.ContentType = "text/html; charset=utf-8";
                                        await ctx.Response.SendFileAsync(indexPath).ConfigureAwait(false);
                                        return;
                                    }
                                }

                                await WriteJsonAsync(ctx, StatusCodes.Status404NotFound, new
                                {
                                    ok = false,
                                    error = "not_found"
                                }).ConfigureAwait(false);
                            });
                        });
                    })
                    .Build();

                _host.Start();
            }
        }

        private static object SafeReadLoadBoardOptions()
        {
            try
            {
                return AtsModScannerService.ReadLoadBoardOptionsPayload();
            }
            catch (Exception ex)
            {
                return new
                {
                    ok = false,
                    error = "loadboard_options_failed",
                    message = ex.Message,
                    cargoes = Array.Empty<object>(),
                    trailers = Array.Empty<object>(),
                    companies = Array.Empty<object>(),
                    cities = Array.Empty<object>(),
                    warnings = new[] { "LoadBoard options could not be built from ATS/mod definitions." }
                };
            }
        }

        private static async Task<(bool Ok, object Payload)> SafeCreateLoadBoardLoadAsync(
            IDictionary<string, object?> parsed,
            string rawBody)
        {
            await Task.Yield();

            try
            {
                string cargoToken =
                    (GetStringCaseInsensitive(parsed, "cargoToken")
                    ?? GetStringCaseInsensitive(parsed, "cargo")
                    ?? "").Trim();

                string trailerToken =
                    (GetStringCaseInsensitive(parsed, "trailerToken")
                    ?? GetStringCaseInsensitive(parsed, "trailer")
                    ?? "").Trim();

                string sourceCompanyToken =
                    (GetStringCaseInsensitive(parsed, "sourceCompanyToken")
                    ?? GetStringCaseInsensitive(parsed, "originCompanyToken")
                    ?? GetStringCaseInsensitive(parsed, "pickupCompanyToken")
                    ?? GetStringCaseInsensitive(parsed, "sourceCompany")
                    ?? "").Trim();

                string destinationCompanyToken =
                    (GetStringCaseInsensitive(parsed, "destinationCompanyToken")
                    ?? GetStringCaseInsensitive(parsed, "destCompanyToken")
                    ?? GetStringCaseInsensitive(parsed, "deliveryCompanyToken")
                    ?? GetStringCaseInsensitive(parsed, "destinationCompany")
                    ?? "").Trim();

                string? sourceCityToken =
                    (GetStringCaseInsensitive(parsed, "sourceCityToken")
                    ?? GetStringCaseInsensitive(parsed, "originCityToken")
                    ?? GetStringCaseInsensitive(parsed, "pickupCityToken")
                    ?? GetStringCaseInsensitive(parsed, "sourceCity"))?.Trim();

                string? destinationCityToken =
                    (GetStringCaseInsensitive(parsed, "destinationCityToken")
                    ?? GetStringCaseInsensitive(parsed, "destCityToken")
                    ?? GetStringCaseInsensitive(parsed, "deliveryCityToken")
                    ?? GetStringCaseInsensitive(parsed, "destinationCity"))?.Trim();

                string loadNumber =
                    (GetStringCaseInsensitive(parsed, "loadNumber")
                    ?? GetStringCaseInsensitive(parsed, "loadNo")
                    ?? GenerateLoadNumber()).Trim();

                double? distanceMiles =
                    GetDoubleCaseInsensitive(parsed, "distanceMiles")
                    ?? GetDoubleCaseInsensitive(parsed, "distance");

                double? weightPounds =
                    GetDoubleCaseInsensitive(parsed, "weightPounds")
                    ?? GetDoubleCaseInsensitive(parsed, "weightLb")
                    ?? GetDoubleCaseInsensitive(parsed, "weight");

                decimal? rate =
                    GetDecimalCaseInsensitive(parsed, "rate")
                    ?? GetDecimalCaseInsensitive(parsed, "revenue")
                    ?? GetDecimalCaseInsensitive(parsed, "income")
                    ?? GetDecimalCaseInsensitive(parsed, "pay");

                var cargo = AtsModScannerService.FindCargo(cargoToken);
                if (cargo == null)
                {
                    return (false, new
                    {
                        ok = false,
                        error = "invalid_cargo",
                        message = "Selected cargo was not found in ATS or active mod definitions.",
                        cargoToken
                    });
                }

                var trailer = AtsModScannerService.FindTrailer(trailerToken);
                if (trailer == null)
                {
                    return (false, new
                    {
                        ok = false,
                        error = "invalid_trailer",
                        message = "Selected trailer was not found in ATS or active mod definitions.",
                        trailerToken
                    });
                }

                if (!AtsModScannerService.IsTrailerAllowedForCargo(cargoToken, trailerToken))
                {
                    return (false, new
                    {
                        ok = false,
                        error = "incompatible_trailer",
                        message = $"Trailer '{trailer.DisplayName}' is not valid for cargo '{cargo.DisplayName}'.",
                        cargo = cargo.DisplayName,
                        trailer = trailer.DisplayName
                    });
                }

                var sourceCompany = AtsModScannerService.FindCompany(sourceCompanyToken);
                if (sourceCompany == null)
                {
                    return (false, new
                    {
                        ok = false,
                        error = "invalid_source_company",
                        message = "Origin company was not found in ATS or mod definitions.",
                        sourceCompanyToken
                    });
                }

                var destinationCompany = AtsModScannerService.FindCompany(destinationCompanyToken);
                if (destinationCompany == null)
                {
                    return (false, new
                    {
                        ok = false,
                        error = "invalid_destination_company",
                        message = "Destination company was not found in ATS or mod definitions.",
                        destinationCompanyToken
                    });
                }

                var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["LoadNumber"] = loadNumber,
                    ["CargoToken"] = cargo.Token,
                    ["CargoName"] = cargo.DisplayName,
                    ["TrailerToken"] = trailer.Token,
                    ["TrailerName"] = trailer.DisplayName,
                    ["SourceCompanyToken"] = sourceCompany.Token,
                    ["SourceCompanyName"] = sourceCompany.DisplayName,
                    ["SourceCityToken"] = !string.IsNullOrWhiteSpace(sourceCityToken) ? sourceCityToken : sourceCompany.CityToken,
                    ["DestinationCompanyToken"] = destinationCompany.Token,
                    ["DestinationCompanyName"] = destinationCompany.DisplayName,
                    ["DestinationCityToken"] = !string.IsNullOrWhiteSpace(destinationCityToken) ? destinationCityToken : destinationCompany.CityToken,
                    ["DistanceMiles"] = distanceMiles,
                    ["WeightPounds"] = weightPounds,
                    ["Rate"] = rate,
                    ["CreatedUtc"] = DateTime.UtcNow,
                    ["RawBody"] = rawBody
                };

                bool wroteToBackend = false;
                object? backendResult = null;

                try
                {
                    var backendType =
                        Type.GetType("OverWatchELD.Services.AtsDispatcherBackend, OverWatchELD", false, true)
                        ?? Type.GetType("OverWatchELD.Services.AtsSaveJobWriterService, OverWatchELD", false, true)
                        ?? Type.GetType("OverWatchELD.Services.AtsFreightMarketWriterService, OverWatchELD", false, true);

                    if (backendType != null)
                    {
                        var method =
                            backendType.GetMethod("CreateLoadFromPayload", BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase)
                            ?? backendType.GetMethod("CreateLoad", BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase)
                            ?? backendType.GetMethod("CreateJob", BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase)
                            ?? backendType.GetMethod("WriteJob", BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase)
                            ?? backendType.GetMethod("WriteFreightJob", BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);

                        if (method != null)
                        {
                            var parameters = method.GetParameters();
                            object? invokeResult = null;

                            if (parameters.Length == 1)
                            {
                                if (TryConvertArgument(payload, parameters[0].ParameterType, out var converted))
                                    invokeResult = method.Invoke(null, new[] { converted });
                                else
                                    invokeResult = method.Invoke(null, new object?[] { payload });
                            }
                            else if (parameters.Length == 2)
                            {
                                invokeResult = method.Invoke(null, new object?[] { payload, rawBody });
                            }

                            if (invokeResult is Task task)
                            {
                                await task.ConfigureAwait(false);
                                var resultProp = task.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public);
                                backendResult = resultProp?.GetValue(task);
                                wroteToBackend = true;
                            }
                            else if (invokeResult != null)
                            {
                                backendResult = invokeResult;
                                wroteToBackend = true;
                            }
                        }
                    }
                }
                catch
                {
                    wroteToBackend = false;
                }

                return (true, new
                {
                    ok = true,
                    createdUtc = DateTime.UtcNow,
                    wroteToBackend,
                    load = new
                    {
                        loadNumber,
                        cargoToken = cargo.Token,
                        cargoName = cargo.DisplayName,
                        trailerToken = trailer.Token,
                        trailerName = trailer.DisplayName,
                        sourceCompanyToken = sourceCompany.Token,
                        sourceCompanyName = sourceCompany.DisplayName,
                        sourceCityToken = !string.IsNullOrWhiteSpace(sourceCityToken) ? sourceCityToken : sourceCompany.CityToken,
                        destinationCompanyToken = destinationCompany.Token,
                        destinationCompanyName = destinationCompany.DisplayName,
                        destinationCityToken = !string.IsNullOrWhiteSpace(destinationCityToken) ? destinationCityToken : destinationCompany.CityToken,
                        distanceMiles,
                        weightPounds,
                        rate
                    },
                    backendResult,
                    message = wroteToBackend
                        ? "Load was validated and passed to ATS load backend."
                        : "Load was validated. No ATS backend writer method was found, so this is a validated preview payload only."
                });
            }
            catch (Exception ex)
            {
                return (false, new
                {
                    ok = false,
                    error = "loadboard_create_failed",
                    message = ex.Message
                });
            }
        }

        private static string GenerateLoadNumber()
        {
            var now = DateTime.UtcNow;
            var rand = Random.Shared.Next(1000, 9999);
            return $"{now:yyyyMMdd}{rand}";
        }

        private static double? GetDoubleCaseInsensitive(IDictionary<string, object?> parsed, string key)
        {
            try
            {
                if (!TryGetCaseInsensitive(parsed, key, out var value) || value == null)
                    return null;

                if (value is double d) return d;
                if (value is float f) return f;
                if (value is decimal m) return (double)m;
                if (value is int i) return i;
                if (value is long l) return l;

                if (value is JsonElement je)
                {
                    if (je.ValueKind == JsonValueKind.Number && je.TryGetDouble(out var jd))
                        return jd;

                    if (je.ValueKind == JsonValueKind.String &&
                        double.TryParse(je.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var js))
                        return js;
                }

                return double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var x)
                    ? x
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static decimal? GetDecimalCaseInsensitive(IDictionary<string, object?> parsed, string key)
        {
            try
            {
                if (!TryGetCaseInsensitive(parsed, key, out var value) || value == null)
                    return null;

                if (value is decimal m) return m;
                if (value is double d) return (decimal)d;
                if (value is float f) return (decimal)f;
                if (value is int i) return i;
                if (value is long l) return l;

                if (value is JsonElement je)
                {
                    if (je.ValueKind == JsonValueKind.Number && je.TryGetDecimal(out var jm))
                        return jm;

                    if (je.ValueKind == JsonValueKind.String &&
                        decimal.TryParse(je.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var js))
                        return js;
                }

                return decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var x)
                    ? x
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryGetCaseInsensitive(IDictionary<string, object?> parsed, string key, out object? value)
        {
            value = null;
            if (parsed == null || string.IsNullOrWhiteSpace(key))
                return false;

            foreach (var kvp in parsed)
            {
                if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = kvp.Value;
                    return true;
                }
            }

            return false;
        }
        private static async Task<bool> SafeSetDutyAsync(string dutyValue)
        {
            // Centralized sync path:
            // Companion button -> ELDStateService/Database -> DutyMachine notifications -> clock/log refresh.
            // This avoids duplicate log rows and makes phone + desktop agree immediately.
            return await CompanionStateBridgeSafe.SetDutyAsync(dutyValue, "companion").ConfigureAwait(false);
        }

        private static string NormalizeDutyLabel(string input)
        {
            var s = (input ?? "").Trim().ToLowerInvariant();

            return s switch
            {
                "off" => "Off Duty",
                "offduty" => "Off Duty",
                "off duty" => "Off Duty",

                "sb" => "Sleeper",
                "sleeper" => "Sleeper",
                "sleeper berth" => "Sleeper",
                "sleeperberth" => "Sleeper",

                "drive" => "Driving",
                "driving" => "Driving",

                "onduty" => "On Duty",
                "on duty" => "On Duty",

                "pc" => "Personal Conveyance",
                "personal conveyance" => "Personal Conveyance",
                "personalconveyance" => "Personal Conveyance",

                "ym" => "Yard Move",
                "yard move" => "Yard Move",
                "yardmove" => "Yard Move",

                _ => input.Trim()
            };
        }

        private static string NormalizeDutyEnumName(string input)
        {
            var s = (input ?? "").Trim().ToLowerInvariant();

            return s switch
            {
                "off" => "OffDuty",
                "offduty" => "OffDuty",
                "off duty" => "OffDuty",

                "sb" => "Sleeper",
                "sleeper" => "Sleeper",
                "sleeper berth" => "Sleeper",
                "sleeperberth" => "Sleeper",

                "drive" => "Driving",
                "driving" => "Driving",

                "onduty" => "OnDuty",
                "on duty" => "OnDuty",

                "pc" => "PersonalConveyance",
                "personal conveyance" => "PersonalConveyance",
                "personalconveyance" => "PersonalConveyance",

                "ym" => "YardMove",
                "yard move" => "YardMove",
                "yardmove" => "YardMove",

                _ => input.Replace(" ", "", StringComparison.OrdinalIgnoreCase)
            };
        }

        private static bool SetWritableMember(object target, string name, object? value)
        {
            try
            {
                var type = target.GetType();

                var prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (prop != null && prop.CanWrite && prop.GetIndexParameters().Length == 0)
                {
                    if (TryConvertArgument(value, prop.PropertyType, out var converted))
                    {
                        prop.SetValue(target, converted);
                        return true;
                    }
                }

                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (field != null)
                {
                    if (TryConvertArgument(value, field.FieldType, out var converted))
                    {
                        field.SetValue(target, converted);
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static async Task<bool> TryInvokeDutyTargetAsync(
            object target,
            string incoming,
            string canonicalLabel,
            string canonicalEnumName,
            object? enumValue)
        {
            var methodNames = new[]
            {
        "SetDutyAsync",
        "SetDuty",
        "SetStatusAsync",
        "SetStatus",
        "ChangeStatusAsync",
        "ChangeStatus",
        "SetDutyStatusAsync",
        "SetDutyStatus",
        "ChangeDutyStatusAsync",
        "ChangeDutyStatus",
        "TransitionToAsync",
        "TransitionTo",
        "ApplyDutyAsync",
        "ApplyDuty",
        "UpdateDutyAsync",
        "UpdateDuty",
        "SetCurrentAsync",
        "SetCurrent"
    };

            var candidateArgs = new List<object?[]>();

            if (enumValue != null)
            {
                candidateArgs.Add(new object?[] { enumValue });
                candidateArgs.Add(new object?[] { enumValue, DateTimeOffset.UtcNow });
                candidateArgs.Add(new object?[] { enumValue, DateTime.UtcNow });
            }

            candidateArgs.Add(new object?[] { canonicalLabel });
            candidateArgs.Add(new object?[] { canonicalEnumName });
            candidateArgs.Add(new object?[] { incoming });
            candidateArgs.Add(new object?[] { canonicalLabel, DateTimeOffset.UtcNow });
            candidateArgs.Add(new object?[] { canonicalEnumName, DateTimeOffset.UtcNow });
            candidateArgs.Add(new object?[] { incoming, DateTimeOffset.UtcNow });
            candidateArgs.Add(new object?[] { canonicalLabel, DateTime.UtcNow });
            candidateArgs.Add(new object?[] { canonicalEnumName, DateTime.UtcNow });
            candidateArgs.Add(new object?[] { incoming, DateTime.UtcNow });

            foreach (var methodName in methodNames)
            {
                foreach (var args in candidateArgs)
                {
                    try
                    {
                        var result = await InvokePossibleTaskAsync(target, methodName, args).ConfigureAwait(true);

                        if (result is bool b)
                            return b;

                        if (result != null)
                            return true;

                        var statusAfter =
                            FirstString(target, "Status", "Current", "CurrentStatus", "DutyStatus", "Mode", "DutyMode", "State");

                        if (!string.IsNullOrWhiteSpace(statusAfter))
                        {
                            var normalizedAfter = NormalizeDutyLabel(statusAfter);
                            if (string.Equals(normalizedAfter, canonicalLabel, StringComparison.OrdinalIgnoreCase))
                                return true;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return false;
        }

        private static bool TryWriteDutyEventToDatabase(string enumName, string label)
        {
            try
            {
                DatabaseService.Initialize();

                var dutyStatusType =
                    Type.GetType("OverWatchELD.Models.DutyStatus, OverWatchELD", false, true);

                var dutyEventType =
                    Type.GetType("OverWatchELD.Models.DutyEvent, OverWatchELD", false, true);

                if (dutyStatusType == null || dutyEventType == null)
                    return false;

                var status = Enum.Parse(dutyStatusType, enumName, true);
                var ev = Activator.CreateInstance(dutyEventType);

                if (ev == null)
                    return false;

                var now = EldClock.UtcNow;

                SetWritableMember(ev, "StartUtc", now);
                SetWritableMember(ev, "Status", status);
                SetWritableMember(ev, "Source", "companion");
                SetWritableMember(ev, "Notes", $"Companion duty change: {label}");

                var insert = typeof(DatabaseService).GetMethod("InsertDutyEvent");

                if (insert == null)
                    return false;

                insert.Invoke(null, new[] { ev });

                return true;
            }
            catch
            {
                return false;
            }
        }
        private static bool TrySetDutyProperties(
            object target,
            string canonicalLabel,
            string canonicalEnumName,
            object? enumValue)
        {
            var props = target.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);

            foreach (var prop in props)
            {
                if (!prop.CanWrite || prop.GetIndexParameters().Length != 0)
                    continue;

                var name = prop.Name;

                if (!string.Equals(name, "Status", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, "CurrentStatus", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, "DutyStatus", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, "Current", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, "Mode", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, "DutyMode", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, "State", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var pt = prop.PropertyType;
                    var nn = Nullable.GetUnderlyingType(pt) ?? pt;

                    if (enumValue != null && nn.IsEnum && nn.IsInstanceOfType(enumValue))
                    {
                        prop.SetValue(target, enumValue);
                        return true;
                    }

                    if (nn == typeof(string))
                    {
                        prop.SetValue(target, canonicalLabel);
                        return true;
                    }

                    if (nn.IsEnum)
                    {
                        var parsed = Enum.Parse(nn, canonicalEnumName, true);
                        prop.SetValue(target, parsed);
                        return true;
                    }
                }
                catch
                {
                }
            }

            var fields = target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public);
            foreach (var field in fields)
            {
                var name = field.Name;

                if (!string.Equals(name, "Status", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, "CurrentStatus", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, "DutyStatus", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, "Current", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, "Mode", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, "DutyMode", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, "State", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var ft = field.FieldType;
                    var nn = Nullable.GetUnderlyingType(ft) ?? ft;

                    if (enumValue != null && nn.IsEnum && nn.IsInstanceOfType(enumValue))
                    {
                        field.SetValue(target, enumValue);
                        return true;
                    }

                    if (nn == typeof(string))
                    {
                        field.SetValue(target, canonicalLabel);
                        return true;
                    }

                    if (nn.IsEnum)
                    {
                        var parsed = Enum.Parse(nn, canonicalEnumName, true);
                        field.SetValue(target, parsed);
                        return true;
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        private static async Task<bool> TryInvokeStaticDutyWriteAsync(
            string incoming,
            string canonicalLabel,
            string canonicalEnumName,
            object? enumValue)
        {
            var dbType = typeof(DatabaseService);

            var methodNames = new[]
            {
        "AddDutyEvent",
        "InsertDutyEvent",
        "AppendDutyEvent",
        "CreateDutyEvent",
        "LogDutyEvent",
        "WriteDutyEvent",
        "SetDuty",
        "SetStatus",
        "ChangeStatus"
    };

            var candidateArgs = new List<object?[]>();

            if (enumValue != null)
            {
                candidateArgs.Add(new object?[] { enumValue });
                candidateArgs.Add(new object?[] { enumValue, DateTimeOffset.UtcNow });
                candidateArgs.Add(new object?[] { enumValue, DateTime.UtcNow });
            }

            candidateArgs.Add(new object?[] { canonicalLabel });
            candidateArgs.Add(new object?[] { canonicalEnumName });
            candidateArgs.Add(new object?[] { incoming });
            candidateArgs.Add(new object?[] { canonicalLabel, DateTimeOffset.UtcNow });
            candidateArgs.Add(new object?[] { canonicalEnumName, DateTimeOffset.UtcNow });
            candidateArgs.Add(new object?[] { incoming, DateTimeOffset.UtcNow });
            candidateArgs.Add(new object?[] { canonicalLabel, DateTime.UtcNow });
            candidateArgs.Add(new object?[] { canonicalEnumName, DateTime.UtcNow });
            candidateArgs.Add(new object?[] { incoming, DateTime.UtcNow });

            foreach (var methodName in methodNames)
            {
                var methods = dbType
                    .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m => string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var method in methods)
                {
                    foreach (var args in candidateArgs)
                    {
                        try
                        {
                            var pars = method.GetParameters();
                            if (pars.Length != args.Length)
                                continue;

                            var invokeArgs = new object?[args.Length];
                            var ok = true;

                            for (int i = 0; i < pars.Length; i++)
                            {
                                if (!TryConvertArgument(args[i], pars[i].ParameterType, out var converted))
                                {
                                    ok = false;
                                    break;
                                }

                                invokeArgs[i] = converted;
                            }

                            if (!ok)
                                continue;

                            var result = method.Invoke(null, invokeArgs);

                            if (result is Task task)
                            {
                                await task.ConfigureAwait(false);

                                var resultProp = task.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public);
                                var taskResult = resultProp?.GetValue(task);

                                if (taskResult is bool tb)
                                    return tb;

                                return true;
                            }

                            if (result is bool b)
                                return b;

                            return true;
                        }
                        catch
                        {
                        }
                    }
                }
            }

            return false;
        }

        public static void Stop()
        {
            lock (_sync)
            {
                if (_host == null)
                    return;

                try { _host.StopAsync().GetAwaiter().GetResult(); } catch { }
                try { _host.Dispose(); } catch { }

                _host = null;
            }
        }

        public static IReadOnlyList<string> GetReachableUrls()
        {
            var urls = new List<string>
            {
                $"http://127.0.0.1:{Port}/",
                $"http://localhost:{Port}/"
            };

            foreach (var ip in GetLanIPv4Addresses())
                urls.Add($"http://{ip}:{Port}/");

            return urls
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static async Task<string> ReadBodyAsStringAsync(HttpRequest req)
        {
            req.EnableBuffering();
            using var reader = new StreamReader(req.Body, Encoding.UTF8, true, 4096, true);
            var body = await reader.ReadToEndAsync().ConfigureAwait(false);
            req.Body.Position = 0;
            return body ?? "";
        }

        private static async Task WriteJsonAsync(HttpContext ctx, int statusCode, object payload)
        {
            ctx.Response.StatusCode = statusCode;
            ctx.Response.ContentType = "application/json; charset=utf-8";

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });

            await ctx.Response.WriteAsync(json, Encoding.UTF8).ConfigureAwait(false);
        }

        private static async Task<object> BuildCompanionDashboardAsync()
        {
            var telemetry = BuildMergedTelemetrySnapshot();
            var duty = await BuildMergedDutySnapshotAsync().ConfigureAwait(false);
            var messages = await SafeGetMessagesAsync().ConfigureAwait(false);

            var sourceObj = GetMemberValue(telemetry, "source");
            var lastSnapshot = GetMemberValue(sourceObj, "lastSnapshot");

            return new
            {
                ok = true,
                generatedUtc = DateTime.UtcNow,
                companion = new
                {
                    port = Port,
                    urls = GetReachableUrls()
                },
                duty,
                telemetry,
                messages
            };
        }

        private static object BuildMergedTelemetrySnapshot()
        {
            var raw = SafeGetTelemetryObject();
            var app = System.Windows.Application.Current as App;

            var lastSnapshot = GetMemberValue(raw, "lastSnapshot");

            string city =
                FirstString(raw, "City", "CurrentCity", "NavCity") ??
                FirstString(lastSnapshot, "City", "CurrentCity", "SourceCity", "NavCity") ??
                "";

            string state =
                FirstString(raw, "State", "CurrentState", "NavState") ??
                FirstString(lastSnapshot, "State", "CurrentState", "NavState") ??
                "";

            string location =
                FirstString(raw, "Location", "LocationText") ??
                FirstString(lastSnapshot, "Location", "LocationText") ??
                CombineLocation(city, state);

            double? conditionPercent =
                FirstDouble(raw, "ConditionPercent", "Condition", "HealthPercent", "Health", "DamagePercent") ??
                FirstDouble(lastSnapshot, "ConditionPercent", "Condition", "HealthPercent", "Health", "DamagePercent");

            object? session = null;
            try { session = app?.Session; } catch { }

            string driverName = "";
            string driverId = "";

            if (session != null)
            {
                driverName = FirstString(session, "DriverName", "UserName", "DisplayName", "DiscordUsername") ?? "";
                driverId = FirstString(session, "DriverId", "UserId", "DiscordUserId") ?? "";
            }

            try
            {
                var identity = DiscordIdentityStore.Load();

                if (string.IsNullOrWhiteSpace(driverName))
                    driverName = FirstString(identity, "DiscordUsername", "Username", "DisplayName") ?? "";

                if (string.IsNullOrWhiteSpace(driverId))
                    driverId = FirstString(identity, "DiscordUserId", "UserId", "Id") ?? "";
            }
            catch { }

            try
            {
                var pairing = VtcPairingStore.Load();

                if (string.IsNullOrWhiteSpace(driverName))
                    driverName = FirstString(pairing, "DiscordUsername", "DriverName", "DisplayName", "UserName") ?? "";

                if (string.IsNullOrWhiteSpace(driverId))
                    driverId = FirstString(pairing, "DiscordUserId", "DriverId", "UserId") ?? "";
            }
            catch { }

            string truckName =
    FirstString(raw, "TruckName", "Truck", "TruckMakeModel", "VehicleName", "Vehicle") ??
    FirstString(lastSnapshot, "TruckName", "TruckMakeModel") ??
    "";

            string truckId =
                FirstString(raw, "TruckId", "TruckNumber", "UnitNumber", "Unit") ??
                FirstString(lastSnapshot, "TruckId", "TruckNumber", "UnitNumber") ??
                "";

            double? odometer =
                FirstDouble(raw, "OdometerMiles", "Odometer", "Mileage", "Miles") ??
                FirstDouble(lastSnapshot, "OdometerMiles", "Odometer", "Mileage", "Miles");

            double? fuelPercent =
                FirstDouble(raw, "FuelPercent", "FuelPct", "FuelPercentRemaining", "Fuel") ??
                FirstDouble(lastSnapshot, "FuelPercent", "FuelPct", "FuelPercentRemaining", "Fuel");

            double? fuelGallons =
                FirstDouble(raw, "FuelGallons") ??
                FirstDouble(lastSnapshot, "FuelGallons");

            double? fuelCapacityGallons =
                FirstDouble(raw, "FuelCapacityGallons") ??
                FirstDouble(lastSnapshot, "FuelCapacityGallons");

            string trailer =
                FirstString(raw, "TrailerName", "Trailer", "CurrentTrailer", "TrailerId") ??
                FirstString(lastSnapshot, "TrailerName", "Trailer", "CurrentTrailer", "TrailerId") ??
                "";

            string cargo =
                FirstString(raw, "CargoName", "Cargo", "CurrentCargo", "LoadCargo") ??
                FirstString(lastSnapshot, "CargoName", "Cargo", "CurrentCargo", "LoadCargo", "TrailerName") ??
                "";

            double? lat = raw != null ? FirstDouble(raw, "Latitude", "Lat") : null;
            double? lon = raw != null ? FirstDouble(raw, "Longitude", "Lon", "Lng") : null;

            // Maintenance board is fallback only. Current telemetry truck wins.
            try
            {
                var maintenanceState = Stores.VtcMaintenanceStore.Load();

                var matchedTruck = maintenanceState.Trucks.FirstOrDefault(t =>
                    (!string.IsNullOrWhiteSpace(truckName) &&
                     string.Equals(t.TruckName, truckName, StringComparison.OrdinalIgnoreCase))
                    ||
                    (!string.IsNullOrWhiteSpace(driverName) &&
                     string.Equals(t.AssignedDriver, driverName, StringComparison.OrdinalIgnoreCase)));

                if (matchedTruck != null)
                {
                    if (string.IsNullOrWhiteSpace(truckName) || truckName.Equals("No Truck Assigned", StringComparison.OrdinalIgnoreCase))
                        truckName = matchedTruck.TruckName;

                    if (string.IsNullOrWhiteSpace(truckId))
                        truckId = matchedTruck.UnitNumber;

                    if (string.IsNullOrWhiteSpace(location))
                        location = matchedTruck.Location;

                    if (!odometer.HasValue || odometer.Value <= 0)
                        odometer = matchedTruck.OdometerMiles;

                    if (!fuelPercent.HasValue || fuelPercent.Value <= 0)
                        fuelPercent = matchedTruck.FuelPercent;

                    if (!conditionPercent.HasValue || conditionPercent.Value <= 0)
                        conditionPercent = matchedTruck.ConditionPercent;
                }
            }
            catch { }

            try
            {
                var master = DriverProfileMasterStore.Find(driverId, driverName, driverName);
                if (master != null)
                {
                    if (string.IsNullOrWhiteSpace(driverId))
                        driverId = master.DiscordUserId;
                    if (string.IsNullOrWhiteSpace(driverName) || driverName.Equals("Unknown Driver", StringComparison.OrdinalIgnoreCase))
                        driverName = !string.IsNullOrWhiteSpace(master.DisplayName) ? master.DisplayName : (!string.IsNullOrWhiteSpace(master.DiscordName) ? master.DiscordName : driverName);

                    var currentTruck = master.ConnectedTrucks?
                        .OrderByDescending(x => x.IsCurrent)
                        .ThenByDescending(x => x.UpdatedUtc)
                        .FirstOrDefault();

                    if (currentTruck != null)
                    {
                        if (string.IsNullOrWhiteSpace(truckId))
                            truckId = currentTruck.TruckNumber;
                        if (string.IsNullOrWhiteSpace(truckName) || truckName.Equals("No Truck Assigned", StringComparison.OrdinalIgnoreCase) || truckName.Equals("Current Truck Not Detected", StringComparison.OrdinalIgnoreCase))
                            truckName = !string.IsNullOrWhiteSpace(currentTruck.TruckName) ? currentTruck.TruckName : (!string.IsNullOrWhiteSpace(currentTruck.TruckNumber) ? currentTruck.TruckNumber : truckName);
                    }
                }
            }
            catch { }

            return new
            {
                available = raw != null,
                driverName = string.IsNullOrWhiteSpace(driverName) ? "Unknown Driver" : driverName,
                driverId,

                truckName = string.IsNullOrWhiteSpace(truckName) ? "Current Truck Not Detected" : truckName,
                truckMakeModel = string.IsNullOrWhiteSpace(truckName) ? "Current Truck Not Detected" : truckName,
                truckId,
                unitNumber = truckId,

                engineOn = raw != null ? GetBool(raw, "EngineOn", "IsEngineOn") : null,
                paused = raw != null ? GetBool(raw, "Paused", "IsPaused") : null,
                speedMph = raw != null ? RoundNullable(ToNullableDouble(GetMemberValue(raw, "SpeedMph") ?? GetMemberValue(raw, "SpeedMPH"))) : null,
                heading = raw != null ? RoundNullable(ToNullableDouble(GetMemberValue(raw, "Heading"))) : null,

                odometerMiles = RoundNullable(odometer),
                fuelPercent = RoundNullable(fuelPercent),
                conditionPercent = RoundNullable(conditionPercent),

                fuelGallons = RoundNullable(fuelGallons),
                fuelCapacityGallons = RoundNullable(fuelCapacityGallons),

                city,
                state,
                location = string.IsNullOrWhiteSpace(location) ? "Unknown" : location,
                latitude = RoundNullable(lat),
                longitude = RoundNullable(lon),

                cargo,
                trailer,
                source = raw
            };
        }

        private static async Task<object> BuildMergedDutySnapshotAsync()
        {
            await Task.Yield();

            var app = System.Windows.Application.Current as App;

            object? dutyMachine = null;
            try { dutyMachine = app?.DutyMachine; } catch { }

            string status = "Unknown";

            // First source: actual synced companion/ELD bridge state
            try
            {
                var bridge = CompanionStateBridgeSafe.GetStateSnapshot();
                var bridgeDuty = GetMemberValue(bridge, "Duty") ?? GetMemberValue(bridge, "duty");

                status =
                    FirstString(bridgeDuty, "Status", "CurrentStatus", "DutyStatus", "Mode", "State")
                    ?? FirstString(bridge, "Status", "CurrentStatus", "DutyStatus", "Mode", "State")
                    ?? status;
            }
            catch { }

            // Second source: DutyMachine
            if (string.IsNullOrWhiteSpace(status) || status.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            {
                status =
                    dutyMachine != null
                        ? FirstString(dutyMachine, "Status", "Current", "CurrentStatus", "DutyStatus", "Mode", "DutyMode", "State") ?? "Unknown"
                        : "Unknown";
            }

            // Third source: current app session
            if (string.IsNullOrWhiteSpace(status) || status.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (app?.Session != null)
                        status = FirstString(app.Session, "DutyStatus", "CurrentDutyStatus", "Status", "Mode") ?? status;
                }
                catch { }
            }

            status = NormalizeDutyLabel(status);

            bool? isDriving = dutyMachine != null ? FirstBool(dutyMachine, "IsDriving", "Driving", "CurrentlyDriving") : null;
            bool? isOnDuty = dutyMachine != null ? FirstBool(dutyMachine, "IsOnDuty", "OnDuty") : null;
            bool? isOffDuty = dutyMachine != null ? FirstBool(dutyMachine, "IsOffDuty", "OffDuty") : null;
            bool? isSleeper = dutyMachine != null ? FirstBool(dutyMachine, "IsSleeper", "Sleeper") : null;

            try
            {
                var hos = HosCalculator2.ComputeSnapshot();

                double driveHours = Math.Max(0, hos.DriveRemaining.TotalHours);
                double shiftHours = Math.Max(0, hos.ShiftRemaining.TotalHours);
                double cycleHours = Math.Max(0, hos.CycleRemaining.TotalHours);
                double breakHours = Math.Max(0, hos.BreakRemaining.TotalHours);

                return new
                {
                    available = true,
                    status,

                    isDriving,
                    isOnDuty,
                    isOffDuty,
                    isSleeper,

                    driveRemaining = RoundNullable(driveHours),
                    shiftRemaining = RoundNullable(shiftHours),
                    cycleRemaining = RoundNullable(cycleHours),
                    breakRemaining = RoundNullable(breakHours),

                    driveRemainingHours = RoundNullable(driveHours),
                    shiftRemainingHours = RoundNullable(shiftHours),
                    cycleRemainingHours = RoundNullable(cycleHours),
                    breakRemainingHours = RoundNullable(breakHours),

                    driveMaxHours = Math.Max(0, hos.DriveLimit.TotalHours),
                    shiftMaxHours = Math.Max(0, hos.ShiftLimit.TotalHours),
                    cycleMaxHours = Math.Max(0, hos.CycleLimit.TotalHours),
                    breakMaxHours = Math.Max(0, hos.BreakLimit.TotalHours),

                    source = "HosCalculator2",
                    hosSource = new
                    {
                        breakRemaining = hos.BreakRemaining,
                        driveRemaining = hos.DriveRemaining,
                        shiftRemaining = hos.ShiftRemaining,
                        cycleRemaining = hos.CycleRemaining,
                        breakLimit = hos.BreakLimit,
                        driveLimit = hos.DriveLimit,
                        shiftLimit = hos.ShiftLimit,
                        cycleLimit = hos.CycleLimit
                    }
                };
            }
            catch
            {
                // fallback only if calculator fails for some reason
                var raw = await SafeGetDutySnapshotAsync().ConfigureAwait(false);
                object? primary = raw ?? dutyMachine;

                var searchRoots = new List<object>();
                if (primary != null) searchRoots.Add(primary);

                if (primary != null)
                {
                    TryAddRoot(searchRoots, GetMemberValue(primary, "Hos"));
                    TryAddRoot(searchRoots, GetMemberValue(primary, "HOS"));
                    TryAddRoot(searchRoots, GetMemberValue(primary, "Clocks"));
                    TryAddRoot(searchRoots, GetMemberValue(primary, "Clock"));
                    TryAddRoot(searchRoots, GetMemberValue(primary, "Summary"));
                    TryAddRoot(searchRoots, GetMemberValue(primary, "HosSummary"));
                    TryAddRoot(searchRoots, GetMemberValue(primary, "DutyClocks"));
                    TryAddRoot(searchRoots, GetMemberValue(primary, "Dashboard"));
                    TryAddRoot(searchRoots, GetMemberValue(primary, "DashboardSnapshot"));
                }

                var candidates = new List<(string Path, double Value)>();
                foreach (var root in searchRoots)
                    ScanNumericCandidates(root, "", 0, candidates);

                double? driveHours = PickBestClockCandidate(candidates, "drive", 11.0);
                double? shiftHours = PickBestClockCandidate(candidates, "shift", 14.0)
                                  ?? PickBestClockCandidate(candidates, "onduty", 14.0)
                                  ?? PickBestClockCandidate(candidates, "on_duty", 14.0);
                double? cycleHours = PickBestClockCandidate(candidates, "cycle", 70.0)
                                  ?? PickBestClockCandidate(candidates, "weekly", 70.0);
                double? breakHours = PickBestClockCandidate(candidates, "break", 8.0)
                                  ?? PickBestClockCandidate(candidates, "restbreak", 8.0)
                                  ?? PickBestClockCandidate(candidates, "rest_break", 8.0);

                if (breakHours.HasValue && breakHours.Value > 8.0 && breakHours.Value <= 480.0)
                    breakHours = breakHours.Value / 60.0;

                return new
                {
                    available = true,
                    status,

                    isDriving,
                    isOnDuty,
                    isOffDuty,
                    isSleeper,

                    driveRemaining = RoundNullable(driveHours),
                    shiftRemaining = RoundNullable(shiftHours),
                    cycleRemaining = RoundNullable(cycleHours),
                    breakRemaining = RoundNullable(breakHours),

                    driveRemainingHours = RoundNullable(driveHours),
                    shiftRemainingHours = RoundNullable(shiftHours),
                    cycleRemainingHours = RoundNullable(cycleHours),
                    breakRemainingHours = RoundNullable(breakHours),

                    driveMaxHours = 11.0,
                    shiftMaxHours = 14.0,
                    cycleMaxHours = 70.0,
                    breakMaxHours = 8.0,

                    source = primary,
                    hosSource = "reflection-fallback",
                    debugCandidates = candidates
                        .OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                        .Select(x => new { path = x.Path, value = Math.Round(x.Value, 4) })
                        .ToArray()
                };
            }
        }

        private static void TryAddRoot(List<object> roots, object? value)
        {
            if (value == null)
                return;

            if (value is string)
                return;

            if (!roots.Contains(value))
                roots.Add(value);
        }

        private static void ScanNumericCandidates(object obj, string path, int depth, List<(string Path, double Value)> results)
        {
            if (obj == null || depth > 2)
                return;

            var type = obj.GetType();

            if (type == typeof(string))
                return;

            if (type.IsPrimitive || type == typeof(decimal) || type == typeof(double) || type == typeof(float))
                return;

            var props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
            foreach (var prop in props)
            {
                if (prop.GetIndexParameters().Length != 0)
                    continue;

                object? value = null;
                try { value = prop.GetValue(obj); } catch { }

                if (value == null)
                    continue;

                var childPath = string.IsNullOrWhiteSpace(path) ? prop.Name : path + "." + prop.Name;

                var n = ToNullableDouble(value);
                if (n.HasValue)
                {
                    results.Add((childPath, n.Value));
                    continue;
                }

                var valueType = value.GetType();
                if (valueType == typeof(string))
                    continue;

                if (!valueType.IsPrimitive && valueType != typeof(decimal))
                    ScanNumericCandidates(value, childPath, depth + 1, results);
            }

            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
            foreach (var field in fields)
            {
                object? value = null;
                try { value = field.GetValue(obj); } catch { }

                if (value == null)
                    continue;

                var childPath = string.IsNullOrWhiteSpace(path) ? field.Name : path + "." + field.Name;

                var n = ToNullableDouble(value);
                if (n.HasValue)
                {
                    results.Add((childPath, n.Value));
                    continue;
                }

                var valueType = value.GetType();
                if (valueType == typeof(string))
                    continue;

                if (!valueType.IsPrimitive && valueType != typeof(decimal))
                    ScanNumericCandidates(value, childPath, depth + 1, results);
            }
        }

        private static double? PickBestClockCandidate(List<(string Path, double Value)> candidates, string keyword, double maxHours)
        {
            var normalizedKeyword = keyword.Replace("_", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();

            var matches = candidates
                .Where(c =>
                {
                    var p = c.Path.Replace("_", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();

                    if (!p.Contains(normalizedKeyword))
                        return false;

                    return p.Contains("remaining") || p.Contains("left") || p.Contains("avail") || p.Contains("time");
                })
                .Select(c =>
                {
                    double value = c.Value;
                    var p = c.Path.ToLowerInvariant();

                    if (maxHours <= 1.0 && value > 1.0 && value <= 60.0 && (p.Contains("minute") || p.Contains("min")))
                        value = value / 60.0;

                    return (c.Path, Value: value);
                })
                .Where(c => c.Value > 0 && c.Value <= maxHours + 1.0)
                .OrderByDescending(c => ScoreClockCandidate(c.Path))
                .ThenByDescending(c => c.Value)
                .ToList();

            if (matches.Count == 0)
                return null;

            return matches[0].Value;
        }

        private static int ScoreClockCandidate(string path)
        {
            var p = path.ToLowerInvariant();
            int score = 0;

            if (p.Contains("remaining")) score += 10;
            if (p.Contains("hours")) score += 8;
            if (p.Contains("left")) score += 6;
            if (p.Contains("hos")) score += 4;
            if (p.Contains("clock")) score += 4;
            if (p.Contains("summary")) score += 2;
            if (p.Contains("used")) score -= 8;
            if (p.Contains("elapsed")) score -= 8;
            if (p.Contains("total")) score -= 5;

            return score;
        }

        private static bool HasInterestingClockFields(object obj)
        {
            var type = obj.GetType();
            var names = type
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(p => p.Name)
                .Concat(type.GetFields(BindingFlags.Instance | BindingFlags.Public).Select(f => f.Name))
                .ToArray();

            return names.Any(n =>
            {
                var x = n.ToLowerInvariant();
                return x.Contains("drive") || x.Contains("shift") || x.Contains("cycle") || x.Contains("break") || x.Contains("onduty");
            });
        }

        private static object? SafeGetTelemetryObject()
        {
            try
            {
                var app = System.Windows.Application.Current as App;

                var candidates = new List<object?>();

                if (_telemetrySvc != null)
                    candidates.Add(_telemetrySvc);

                try
                {
                    if (app?.Telemetry != null)
                        candidates.Add(app.Telemetry);
                }
                catch { }

                foreach (var svc in candidates)
                {
                    if (svc == null)
                        continue;

                    var obj =
                        InvokeNoArg(svc, "GetSnapshot") ??
                        InvokeNoArg(svc, "GetLatest") ??
                        InvokeNoArg(svc, "CreateSnapshot") ??
                        GetMemberValue(svc, "Snapshot") ??
                        GetMemberValue(svc, "CurrentSnapshot") ??
                        GetMemberValue(svc, "LatestSnapshot") ??
                        GetMemberValue(svc, "CurrentTelemetry") ??
                        GetMemberValue(svc, "LatestTelemetry") ??
                        GetMemberValue(svc, "Current") ??
                        GetMemberValue(svc, "Latest");

                    if (IsUsefulComplexObject(obj))
                        return obj;

                    if (IsUsefulComplexObject(svc))
                        return svc;
                }
            }
            catch
            {
            }

            return null;
        }

        private static async Task<object?> SafeGetDutySnapshotAsync()
        {
            try
            {
                var app = System.Windows.Application.Current as App;

                var candidates = new List<object?>();

                if (_dutySvc != null)
                    candidates.Add(_dutySvc);

                try
                {
                    if (app?.DutyMachine != null)
                        candidates.Add(app.DutyMachine);
                }
                catch { }

                string? statusText = null;

                foreach (var svc in candidates)
                {
                    if (svc == null)
                        continue;

                    var obj =
                        await InvokePossibleTaskAsync(svc, "GetSnapshotAsync").ConfigureAwait(false) ??
                        await InvokePossibleTaskAsync(svc, "GetCurrentAsync").ConfigureAwait(false) ??
                        await InvokePossibleTaskAsync(svc, "GetHosSnapshotAsync").ConfigureAwait(false) ??
                        await InvokePossibleTaskAsync(svc, "GetDashboardSnapshotAsync").ConfigureAwait(false) ??
                        InvokeNoArg(svc, "GetSnapshot") ??
                        InvokeNoArg(svc, "GetCurrent") ??
                        InvokeNoArg(svc, "GetHosSnapshot") ??
                        GetMemberValue(svc, "Snapshot") ??
                        GetMemberValue(svc, "CurrentSnapshot") ??
                        GetMemberValue(svc, "Hos") ??
                        GetMemberValue(svc, "HOS") ??
                        GetMemberValue(svc, "Clocks") ??
                        GetMemberValue(svc, "Clock") ??
                        GetMemberValue(svc, "Summary") ??
                        GetMemberValue(svc, "HosSummary") ??
                        GetMemberValue(svc, "DutyClocks") ??
                        GetMemberValue(svc, "Current");

                    if (obj != null)
                    {
                        if (obj is string s)
                            statusText ??= s;
                        else if (obj.GetType().IsEnum || IsNumericLike(obj))
                            statusText ??= Convert.ToString(obj, CultureInfo.InvariantCulture);
                        else if (IsUsefulComplexObject(obj))
                            return obj;
                    }

                    if (IsUsefulComplexObject(svc))
                        return svc;
                }

                return new
                {
                    Status = statusText ?? "Unknown"
                };
            }
            catch
            {
                return new
                {
                    Status = "Unknown"
                };
            }
        }

        private static bool IsUsefulComplexObject(object? obj)
        {
            if (obj == null)
                return false;

            var t = obj.GetType();

            if (t == typeof(string))
                return false;

            if (t.IsPrimitive || t.IsEnum)
                return false;

            if (t == typeof(decimal) || t == typeof(double) || t == typeof(float) ||
                t == typeof(int) || t == typeof(long) || t == typeof(short) ||
                t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort) ||
                t == typeof(byte) || t == typeof(sbyte))
                return false;

            return true;
        }

        private static bool IsNumericLike(object? obj)
        {
            if (obj == null)
                return false;

            var t = obj.GetType();

            if (t.IsEnum)
                return true;

            return t == typeof(decimal) || t == typeof(double) || t == typeof(float) ||
                   t == typeof(int) || t == typeof(long) || t == typeof(short) ||
                   t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort) ||
                   t == typeof(byte) || t == typeof(sbyte);
        }

        private static async Task<object?> SafeGetInspectionSnapshotAsync(string mode)
        {
            var svc = _inspectionSvc;
            if (svc == null) return new { mode, available = false };

            try
            {
                var obj =
                    await InvokePossibleTaskAsync(svc, "GetInspectionAsync", mode).ConfigureAwait(false) ??
                    await InvokePossibleTaskAsync(svc, "LoadInspectionAsync", mode).ConfigureAwait(false) ??
                    InvokeWithArgs(svc, "GetInspection", mode) ??
                    InvokeWithArgs(svc, "LoadInspection", mode);

                return obj ?? new { mode, available = false };
            }
            catch
            {
                return new { mode, available = false };
            }
        }

        private static async Task<bool> SafeSaveInspectionAsync(string mode, Dictionary<string, string> values, string rawText)
        {
            var svc = _inspectionSvc;
            if (svc == null) return false;

            try
            {
                var result =
                    await InvokePossibleTaskAsync(svc, "SaveInspectionAsync", mode, values).ConfigureAwait(false) ??
                    await InvokePossibleTaskAsync(svc, "SaveInspectionAsync", mode, rawText).ConfigureAwait(false) ??
                    await InvokePossibleTaskAsync(svc, "SaveAsync", mode, values).ConfigureAwait(false) ??
                    await InvokePossibleTaskAsync(svc, "SaveAsync", mode, rawText).ConfigureAwait(false) ??
                    InvokeWithArgs(svc, "SaveInspection", mode, values) ??
                    InvokeWithArgs(svc, "SaveInspection", mode, rawText) ??
                    InvokeWithArgs(svc, "Save", mode, values) ??
                    InvokeWithArgs(svc, "Save", mode, rawText);

                if (result is bool b) return b;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<object> SafeGetMessagesAsync()
        {
            var svc = _dispatchInboxSvc;
            if (svc == null) return Array.Empty<object>();

            try
            {
                var obj =
                    await InvokePossibleTaskAsync(svc, "GetMessagesAsync").ConfigureAwait(false) ??
                    await InvokePossibleTaskAsync(svc, "GetSnapshotAsync").ConfigureAwait(false) ??
                    InvokeNoArg(svc, "GetMessages") ??
                    InvokeNoArg(svc, "GetSnapshot") ??
                    GetMemberValue(svc, "Messages");

                return obj ?? Array.Empty<object>();
            }
            catch
            {
                return Array.Empty<object>();
            }
        }

        private static async Task<object> SafeGetConversationsAsync()
        {
            var svc = _dispatchInboxSvc;
            if (svc == null) return Array.Empty<object>();

            try
            {
                var obj =
                    await InvokePossibleTaskAsync(svc, "GetConversationsAsync").ConfigureAwait(false) ??
                    InvokeNoArg(svc, "GetConversations");

                return obj ?? Array.Empty<object>();
            }
            catch
            {
                return Array.Empty<object>();
            }
        }

        private static async Task<bool> SafeSendMessageAsync(string text, string? to, Dictionary<string, string> values)
        {
            var svc = _dispatchInboxSvc;
            if (svc == null) return false;

            try
            {
                var result =
                    await InvokePossibleTaskAsync(svc, "SendMessageAsync", text).ConfigureAwait(false) ??
                    await InvokePossibleTaskAsync(svc, "SendAsync", text).ConfigureAwait(false) ??
                    await InvokePossibleTaskAsync(svc, "PostMessageAsync", text).ConfigureAwait(false);

                if (result != null)
                {
                    if (result is bool b1) return b1;
                    return true;
                }

                result =
                    await InvokePossibleTaskAsync(svc, "SendMessageAsync", text, to ?? "").ConfigureAwait(false) ??
                    await InvokePossibleTaskAsync(svc, "SendAsync", text, to ?? "").ConfigureAwait(false);

                if (result != null)
                {
                    if (result is bool b2) return b2;
                    return true;
                }

                result =
                    await InvokePossibleTaskAsync(svc, "SendMessageAsync", values).ConfigureAwait(false) ??
                    await InvokePossibleTaskAsync(svc, "SendAsync", values).ConfigureAwait(false) ??
                    InvokeWithArgs(svc, "SendMessage", text) ??
                    InvokeWithArgs(svc, "Send", text);

                if (result is bool b3) return b3;
                return result != null;
            }
            catch
            {
                return false;
            }
        }

        private static string ResolveWebRoot(string? explicitWebRoot)
        {
            if (!string.IsNullOrWhiteSpace(explicitWebRoot))
                return Path.GetFullPath(explicitWebRoot);

            var baseDir = AppContext.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "wwwroot", "companion"),
                Path.Combine(baseDir, "wwwroot"),
                Path.Combine(baseDir, "CompanionWeb"),
            };

            foreach (var c in candidates)
            {
                try
                {
                    Directory.CreateDirectory(c);
                    return Path.GetFullPath(c);
                }
                catch { }
            }

            var fallback = Path.Combine(baseDir, "wwwroot");
            Directory.CreateDirectory(fallback);
            return Path.GetFullPath(fallback);
        }

        private static IReadOnlyList<string> GetLanIPv4Addresses()
        {
            var list = new List<string>();

            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    var props = ni.GetIPProperties();
                    foreach (var ua in props.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;

                        var ip = ua.Address.ToString();
                        if (ip.StartsWith("169.254.", StringComparison.Ordinal)) continue;

                        list.Add(ip);
                    }
                }
            }
            catch { }

            return list
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static Dictionary<string, string> ParseIncomingBody(string bodyText, string? contentType)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(bodyText)) return dict;

            try
            {
                if (!string.IsNullOrWhiteSpace(contentType) &&
                    contentType.IndexOf("application/json", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    using var doc = JsonDocument.Parse(bodyText);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var p in doc.RootElement.EnumerateObject())
                            dict[p.Name] = JsonElementToString(p.Value);
                    }
                    return dict;
                }
            }
            catch { }

            if (bodyText.Contains('=') && bodyText.Contains('&'))
            {
                foreach (var part in bodyText.Split('&'))
                {
                    var idx = part.IndexOf('=');
                    if (idx <= 0) continue;

                    var k = Uri.UnescapeDataString(part[..idx].Replace('+', ' '));
                    var v = Uri.UnescapeDataString(part[(idx + 1)..].Replace('+', ' '));
                    dict[k] = v;
                }
                return dict;
            }

            dict["text"] = bodyText.Trim();
            return dict;
        }

        private static string? GetStringCaseInsensitive(IDictionary<string, object?> dict, string key)
        {
            try
            {
                if (dict == null || string.IsNullOrWhiteSpace(key))
                    return null;

                foreach (var kvp in dict)
                {
                    if (!string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (kvp.Value == null)
                        return null;

                    if (kvp.Value is string s)
                        return s;

                    if (kvp.Value is JsonElement je)
                    {
                        if (je.ValueKind == JsonValueKind.String)
                            return je.GetString();

                        if (je.ValueKind == JsonValueKind.Null || je.ValueKind == JsonValueKind.Undefined)
                            return null;

                        return je.ToString();
                    }

                    return Convert.ToString(kvp.Value, CultureInfo.InvariantCulture);
                }
            }
            catch
            {
            }

            return null;
        }

        private static string? GetStringCaseInsensitive(Dictionary<string, string> dict, string key)
        {
            if (dict.TryGetValue(key, out var value))
                return value;

            foreach (var kv in dict)
            {
                if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            }

            return null;
        }

        private static string JsonElementToString(JsonElement e)
        {
            return e.ValueKind switch
            {
                JsonValueKind.String => e.GetString() ?? "",
                JsonValueKind.Number => e.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => "",
                _ => e.GetRawText()
            };
        }

        private static double? FirstDouble(object obj, params string[] names)
        {
            foreach (var n in names)
            {
                var v = GetMemberValue(obj, n);
                var d = ToNullableDouble(v);
                if (d != null)
                    return d;
            }

            return null;
        }

        private static string? FirstString(object obj, params string[] names)
        {
            foreach (var n in names)
            {
                var v = GetMemberValue(obj, n);
                var s = ToNullableString(v);
                if (!string.IsNullOrWhiteSpace(s))
                    return s;
            }

            return null;
        }

        private static bool? FirstBool(object obj, params string[] names)
        {
            foreach (var n in names)
            {
                var v = GetMemberValue(obj, n);
                if (v == null)
                    continue;

                if (v is bool b)
                    return b;

                if (bool.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out var parsed))
                    return parsed;
            }

            return null;
        }

        private static string CombineLocation(string? city, string? state)
        {
            city = (city ?? "").Trim();
            state = (state ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(city) && !string.IsNullOrWhiteSpace(state)) return $"{city}, {state}";
            if (!string.IsNullOrWhiteSpace(city)) return city;
            if (!string.IsNullOrWhiteSpace(state)) return state;
            return "";
        }

        private static double? RoundNullable(double? value) => value.HasValue ? Math.Round(value.Value, 2) : null;

        private static bool? GetBool(object obj, params string[] names)
        {
            foreach (var n in names)
            {
                var v = GetMemberValue(obj, n);
                if (v == null) continue;

                if (v is bool b) return b;

                if (bool.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out var parsed))
                    return parsed;
            }

            return null;
        }

        private static string? ToNullableString(object? value)
        {
            var s = Convert.ToString(value, CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }

        private static double? ToNullableDouble(object? value)
        {
            if (value == null) return null;
            if (value is double d) return d;
            if (value is float f) return f;
            if (value is decimal m) return (double)m;
            if (value is int i) return i;
            if (value is long l) return l;

            if (double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var parsed))
                return parsed;

            return null;
        }

        private static object? GetMemberValue(object target, string name)
        {
            try
            {
                var type = target.GetType();

                var prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (prop != null && prop.GetIndexParameters().Length == 0)
                    return prop.GetValue(target);

                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (field != null)
                    return field.GetValue(target);
            }
            catch { }

            return null;
        }

        private static object? InvokeNoArg(object target, string methodName)
        {
            try
            {
                var m = target.GetType()
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .FirstOrDefault(x =>
                        string.Equals(x.Name, methodName, StringComparison.OrdinalIgnoreCase) &&
                        x.GetParameters().Length == 0);

                return m?.Invoke(target, null);
            }
            catch
            {
                return null;
            }
        }

        private static object? InvokeWithArgs(object target, string methodName, params object?[] args)
        {
            try
            {
                var methods = target.GetType()
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .Where(x => string.Equals(x.Name, methodName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var m in methods)
                {
                    var pars = m.GetParameters();
                    if (pars.Length != args.Length) continue;

                    var invokeArgs = new object?[args.Length];
                    var ok = true;

                    for (int i = 0; i < pars.Length; i++)
                    {
                        if (!TryConvertArgument(args[i], pars[i].ParameterType, out var converted))
                        {
                            ok = false;
                            break;
                        }
                        invokeArgs[i] = converted;
                    }

                    if (!ok) continue;

                    return m.Invoke(target, invokeArgs);
                }
            }
            catch { }

            return null;
        }

        private static async Task<object?> InvokePossibleTaskAsync(object target, string methodName, params object?[] args)
        {
            var result = InvokeWithArgs(target, methodName, args);
            if (result == null) return null;

            if (result is Task task)
            {
                await task.ConfigureAwait(false);
                var resultProp = task.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public);
                return resultProp?.GetValue(task);
            }

            return result;
        }

        private static bool TryConvertArgument(object? input, Type targetType, out object? converted)
        {
            converted = null;

            try
            {
                if (targetType == typeof(object))
                {
                    converted = input;
                    return true;
                }

                var nn = Nullable.GetUnderlyingType(targetType) ?? targetType;

                if (input == null)
                {
                    if (!nn.IsValueType || Nullable.GetUnderlyingType(targetType) != null)
                    {
                        converted = null;
                        return true;
                    }
                    return false;
                }

                if (nn.IsInstanceOfType(input))
                {
                    converted = input;
                    return true;
                }

                if (nn == typeof(string))
                {
                    converted = Convert.ToString(input, CultureInfo.InvariantCulture) ?? "";
                    return true;
                }

                if (nn.IsEnum)
                {
                    var s = Convert.ToString(input, CultureInfo.InvariantCulture);
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        converted = Enum.Parse(nn, s, true);
                        return true;
                    }
                }

                if (nn == typeof(bool))
                {
                    if (input is bool b)
                    {
                        converted = b;
                        return true;
                    }

                    var s = Convert.ToString(input, CultureInfo.InvariantCulture);
                    if (bool.TryParse(s, out var parsedBool))
                    {
                        converted = parsedBool;
                        return true;
                    }
                }

                if (input is Dictionary<string, string> dict)
                {
                    if (nn == typeof(string))
                    {
                        converted = JsonSerializer.Serialize(dict);
                        return true;
                    }

                    if (!nn.IsPrimitive && nn != typeof(decimal))
                    {
                        var json = JsonSerializer.Serialize(dict);
                        converted = JsonSerializer.Deserialize(json, nn, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                        return converted != null;
                    }
                }

                converted = Convert.ChangeType(input, nn, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void EnsureDefaultWebFiles(string root)
        {
            Directory.CreateDirectory(root);

            var index = Path.Combine(root, "index.html");

            var html = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8"/>
<meta name="viewport" content="width=device-width,initial-scale=1"/>
<title>OverWatch ELD Companion</title>
<style>
:root{
  --bg:#0b1220;
  --panel:#111827;
  --line:#23324a;
  --text:#e5e7eb;
  --muted:#94a3b8;
  --green:#22c55e;
  --yellow:#f59e0b;
  --red:#ef4444;
  --blue:#38bdf8;
}
*{box-sizing:border-box}
html,body{margin:0;padding:0;background:linear-gradient(180deg,#09111f,var(--bg));color:var(--text);font-family:Segoe UI,Arial,sans-serif}
.wrap{max-width:1320px;margin:0 auto;padding:16px}
.top{display:flex;justify-content:space-between;align-items:center;gap:12px;margin-bottom:14px;flex-wrap:wrap}
.title{font-size:28px;font-weight:800;letter-spacing:.02em}
.sub{font-size:13px;color:var(--muted)}
.toolbar{display:flex;gap:8px;flex-wrap:wrap}
.btn{border:1px solid var(--line);background:#0d1627;color:var(--text);padding:10px 14px;border-radius:12px;cursor:pointer;font-weight:600}
.btn:hover{background:#122039}
.btn.green{border-color:#14532d}
.btn.blue{border-color:#0c4a6e}
.grid{display:grid;gap:14px}
.cards-4{grid-template-columns:repeat(auto-fit,minmax(240px,1fr))}
.cards-3{grid-template-columns:repeat(auto-fit,minmax(320px,1fr))}
.cards-2{grid-template-columns:repeat(auto-fit,minmax(420px,1fr))}
.card{background:rgba(17,24,39,.96);border:1px solid var(--line);border-radius:18px;padding:16px;box-shadow:0 10px 24px rgba(0,0,0,.22)}

.inspection-card{min-height:360px}
.inspection-hero{display:flex;justify-content:space-between;align-items:center;gap:12px;border:1px solid var(--line);border-radius:18px;background:linear-gradient(135deg,#0b1324,#111827);padding:14px;margin:8px 0 14px}
.inspection-hero-left{display:flex;align-items:center;gap:12px}
.insp-icon{width:54px;height:54px;border-radius:16px;display:flex;align-items:center;justify-content:center;font-size:30px;border:1px solid var(--line);background:#0b1324}
.insp-title{font-size:20px;font-weight:800}
.insp-sub{font-size:13px;color:var(--muted);margin-top:3px}
.status-pill{display:inline-flex;align-items:center;gap:7px;border-radius:12px;padding:8px 10px;font-size:12px;font-weight:800;text-transform:uppercase;letter-spacing:.05em;border:1px solid var(--line);background:#0d1627}
.status-pill.ok{color:var(--green);border-color:#166534;background:rgba(34,197,94,.08)}
.status-pill.warn{color:var(--yellow);border-color:#92400e;background:rgba(245,158,11,.08)}
.status-pill.danger{color:var(--red);border-color:#7f1d1d;background:rgba(239,68,68,.08)}
.status-pill.neutral{color:var(--muted)}
.insp-mini-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px}
.insp-wide{grid-column:1 / -1}
.insp-line{border:1px solid var(--line);border-radius:16px;background:#0b1324;padding:12px}
.insp-line-title{font-size:11px;color:var(--muted);text-transform:uppercase;letter-spacing:.1em;margin-bottom:6px}
.insp-line-value{font-size:18px;font-weight:800}
.insp-line-note{font-size:12px;color:var(--muted);margin-top:4px}
.alert-box{display:flex;gap:10px;align-items:flex-start;border-radius:16px;border:1px solid var(--line);padding:12px;background:#0b1324;margin-top:12px}
.alert-box.ok{border-color:#166534;background:rgba(34,197,94,.07)}
.alert-box.warn{border-color:#92400e;background:rgba(245,158,11,.09)}
.alert-box.danger{border-color:#7f1d1d;background:rgba(239,68,68,.09)}
.alert-mark{font-size:24px;line-height:1}
.alert-title{font-weight:800}
.alert-note{font-size:12px;color:var(--muted);margin-top:3px}

.label{color:var(--muted);font-size:11px;text-transform:uppercase;letter-spacing:.11em;margin-bottom:6px}
.value{font-size:28px;font-weight:800;line-height:1.1}
.small{font-size:13px;color:var(--muted)}
.row{display:flex;gap:10px;align-items:center;justify-content:space-between}
.row.wrap{flex-wrap:wrap}
.pill{display:inline-flex;align-items:center;gap:6px;border:1px solid var(--line);background:#0d1627;padding:6px 10px;border-radius:999px;font-size:12px;color:var(--text)}
.clock-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:14px}
.clock-card{
  background:linear-gradient(180deg,#0f172a,#0b1324);
  border:1px solid var(--line);
  border-radius:20px;
  padding:14px;
  text-align:center;
  transition:
    border-color .35s ease,
    box-shadow .35s ease,
    transform .2s ease;
}
.clock-card.warn{
  border-color: rgba(245,158,11,.65);
  box-shadow: 0 0 0 1px rgba(245,158,11,.18), 0 0 22px rgba(245,158,11,.12);
}
.clock-card.danger{
  border-color: rgba(239,68,68,.7);
  box-shadow: 0 0 0 1px rgba(239,68,68,.2), 0 0 24px rgba(239,68,68,.14);
}
.clock-card.ok{
  border-color: rgba(34,197,94,.45);
}
.clock-wrap{width:150px;height:150px;margin:4px auto 10px auto;position:relative}
.clock-svg{width:150px;height:150px;transform:rotate(-90deg);overflow:visible}
.clock-svg circle{
  transition:
    stroke-dashoffset .75s ease,
    stroke .35s ease,
    opacity .35s ease,
    filter .35s ease;
}
.clock-center{position:absolute;inset:0;display:flex;flex-direction:column;align-items:center;justify-content:center;pointer-events:none}
.clock-time{
  font-size:26px;
  font-weight:800;
  line-height:1;
  transition: color .35s ease, text-shadow .35s ease;
}
.clock-time.warn{
  color:#fbbf24;
  text-shadow: 0 0 12px rgba(245,158,11,.16);
}
.clock-time.danger{
  color:#fca5a5;
  text-shadow: 0 0 14px rgba(239,68,68,.18);
}
.clock-time.ok{
  color:#e5e7eb;
}
.clock-unit{font-size:11px;color:var(--muted);margin-top:4px;letter-spacing:.08em;text-transform:uppercase}
.clock-title{font-size:14px;font-weight:700;margin-top:2px}
.clock-sub{
  font-size:12px;
  color:var(--muted);
  margin-top:4px;
  transition: color .35s ease;
}
.clock-sub.warn{ color:#fbbf24; }
.clock-sub.danger{ color:#fca5a5; }
.pulse-danger{
  animation: oweldPulseDanger 1.15s ease-in-out infinite;
}
@keyframes oweldPulseDanger{
  0%   { transform: scale(1); }
  50%  { transform: scale(1.025); }
  100% { transform: scale(1); }
}
textarea,input{width:100%;background:#09111f;color:var(--text);border:1px solid var(--line);border-radius:12px;padding:11px;font:inherit}
textarea{min-height:110px;resize:vertical}
.actions{display:flex;gap:8px;flex-wrap:wrap;margin-top:10px}
.quick-duty{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:8px}
.stat-list{display:grid;grid-template-columns:repeat(auto-fit,minmax(160px,1fr));gap:10px}
.mini{background:#0b1324;border:1px solid var(--line);border-radius:14px;padding:12px}
.mini .k{font-size:11px;color:var(--muted);text-transform:uppercase;letter-spacing:.08em}
.mini .v{font-size:18px;font-weight:700;margin-top:5px}
pre{white-space:pre-wrap;word-break:break-word;background:#08101d;border:1px solid var(--line);border-radius:12px;padding:12px;overflow:auto;color:#cbd5e1;font-size:12px;max-height:320px}
.listbox{background:#08101d;border:1px solid var(--line);border-radius:12px;padding:10px;max-height:320px;overflow:auto}
.msg{border-bottom:1px solid #152238;padding:10px 4px}
.msg:last-child{border-bottom:none}
.msg-top{display:flex;justify-content:space-between;gap:8px;font-size:12px}
.msg-from{font-weight:700}
.msg-time{color:var(--muted)}
.msg-body{margin-top:6px;color:#dbe4ef;font-size:13px;white-space:pre-wrap}
.footer-note{font-size:12px;color:var(--muted);margin-top:8px}
@media (max-width:760px){
  .quick-duty{grid-template-columns:repeat(2,minmax(0,1fr))}
  .clock-wrap,.clock-svg{width:132px;height:132px}
  .clock-time{font-size:23px}
}

<style>

body{
  background:#0b1220;
}

/* existing styles above */


/* ===== CREATE BOL MODAL ===== */

.modalOverlay{
  position:fixed;
  inset:0;
  background:rgba(0,0,0,.65);
  display:flex;
  align-items:center;
  justify-content:center;
  z-index:9999;
}

.modalCard{
  width:520px;
  max-width:95%;
  background:#111827;
  border-radius:18px;
  padding:18px;
  border:1px solid #243041;
}

.modalHeader{
  display:flex;
  justify-content:space-between;
  align-items:center;
  margin-bottom:16px;
}

.modalBody{
  display:flex;
  flex-direction:column;
  gap:10px;
}

.field{
  display:flex;
  flex-direction:column;
  gap:4px;
}

.field input{
  background:#0b1220;
  border:1px solid #253247;
  border-radius:10px;
  padding:10px;
  color:white;
}

.closeBtn{
  background:none;
  border:none;
  color:white;
  font-size:20px;
  cursor:pointer;
}

.movableCard{
  cursor:grab;
  user-select:none;
}

.movableCard.dragging{
  cursor:grabbing;
  opacity:.85;
  z-index:50;
}

.inspectionModalCard{
  width:980px;
  max-width:96vw;
  max-height:92vh;
  overflow:auto;
}

.inspectionTopBar{
  display:grid;
  grid-template-columns:150px 1fr 1fr auto;
  gap:10px;
  margin-bottom:14px;
}

.inspCols{
  display:grid;
  grid-template-columns:1fr 1fr;
  gap:14px;
}

.inspPanel{
  background:#0b1220;
  border:1px solid #253247;
  border-radius:14px;
  padding:12px;
}

.inspPanelTitle{
  font-weight:800;
  margin-bottom:10px;
  color:#e5eefc;
}

.checkRow{
  display:grid;
  grid-template-columns:1fr auto auto;
  gap:8px;
  align-items:center;
  padding:8px;
  border-bottom:1px solid rgba(255,255,255,.07);
}

.checkRow:last-child{
  border-bottom:none;
}

.checkName{
  color:#dbeafe;
  font-weight:650;
}

.checkCat{
  font-size:11px;
  color:#94a3b8;
}

.noteInput{
  grid-column:1 / 4;
  margin-top:4px;
}

.certLine{
  display:flex;
  align-items:center;
  gap:8px;
  margin-top:12px;
  color:#dbeafe;
}

@media(max-width:850px){
  .inspectionTopBar,
  .inspCols{
    grid-template-columns:1fr;
  }

.big-map-btn{
    width:100%;
    height:52px;
    font-size:18px;
    font-weight:700;
    border-radius:14px;
}
}
</style>
</style>
</head>
<body>
<div class="wrap">
  <div class="top">
    <div>
      <div class="title">OverWatch ELD Companion</div>
      <div class="sub">Live mirror of dashboard, clocks, status, dispatch, and inspection data</div>
    </div>
    <div class="toolbar">
      <button class="btn green" onclick="refreshAll()">Refresh</button>
      <button class="btn blue" onclick="loadMessages()">Messages</button>
<button class="btn blue" onclick="showTab('logsTab'); loadLogs();">Logs</button>
    </div>
  </div>

  <section id="logsTab" class="tabPanel" style="display:none">
  <div class="card">
    <div class="label">ELD Logs</div>
    <div class="muted">Mirrors the exact current ELD log snapshot.</div>

    <div style="margin-top:12px">
      <button class="btn green" onclick="loadLogs()">Refresh Logs</button>
    </div>

    <div id="logsStatus" class="small" style="margin-top:10px">Ready</div>
    <div id="logsBox" style="margin-top:14px"></div>
  </div>
</section>

  <div class="grid cards-4">
    <div class="card">
      <div class="label">Driver</div>
      <div class="value" id="driverName">—</div>
      <div class="small" id="driverMeta">—</div>
    </div>
    <div class="card">
      <div class="label">Current Duty</div>
      <div class="value" id="dutyStatus">—</div>
      <div class="small" id="engineDuty">—</div>
    </div>
    <div class="card">
      <div class="label">Speed</div>
      <div class="value" id="speed">—</div>
      <div class="small" id="heading">—</div>
    </div>
    <div class="card">
      <div class="label">Location</div>
      <div class="value" id="location">—</div>
      <div class="small" id="odometer">—</div>
    </div>
  </div>

  <div class="card" style="margin-top:14px">
    <div class="row wrap" style="margin-bottom:10px">
      <div>
        <div class="label">HOS Clocks</div>
        <div class="small">Companion reads the normalized duty payload so the clocks can use real ELD values when available.</div>
      </div>
      <div class="pill">Live Clock View</div>
    </div>

    <div class="clock-grid">
      <div class="clock-card" id="driveCard">
        <div class="clock-wrap">
          <svg class="clock-svg" viewBox="0 0 160 160">
            <circle cx="80" cy="80" r="62" stroke="#1e293b" stroke-width="12" fill="none" />
            <circle id="driveArc" cx="80" cy="80" r="62" stroke="#22c55e" stroke-width="12" fill="none" stroke-linecap="round" />
          </svg>
          <div class="clock-center">
            <div class="clock-time" id="driveTime">—</div>
            <div class="clock-unit">Remaining</div>
          </div>
        </div>
        <div class="clock-title">Drive</div>
        <div class="clock-sub" id="driveSub">11h clock</div>
      </div>

      <div class="clock-card" id="shiftCard">
        <div class="clock-wrap">
          <svg class="clock-svg" viewBox="0 0 160 160">
            <circle cx="80" cy="80" r="62" stroke="#1e293b" stroke-width="12" fill="none" />
            <circle id="shiftArc" cx="80" cy="80" r="62" stroke="#38bdf8" stroke-width="12" fill="none" stroke-linecap="round" />
          </svg>
          <div class="clock-center">
            <div class="clock-time" id="shiftTime">—</div>
            <div class="clock-unit">Remaining</div>
          </div>
        </div>
        <div class="clock-title">Shift</div>
        <div class="clock-sub" id="shiftSub">14h clock</div>
      </div>

      <div class="clock-card" id="cycleCard">
        <div class="clock-wrap">
          <svg class="clock-svg" viewBox="0 0 160 160">
            <circle cx="80" cy="80" r="62" stroke="#1e293b" stroke-width="12" fill="none" />
            <circle id="cycleArc" cx="80" cy="80" r="62" stroke="#f59e0b" stroke-width="12" fill="none" stroke-linecap="round" />
          </svg>
          <div class="clock-center">
            <div class="clock-time" id="cycleTime">—</div>
            <div class="clock-unit">Remaining</div>
          </div>
        </div>
        <div class="clock-title">Cycle</div>
        <div class="clock-sub" id="cycleSub">70h / 8 day</div>
      </div>

      <div class="clock-card" id="breakCard">
        <div class="clock-wrap">
          <svg class="clock-svg" viewBox="0 0 160 160">
            <circle cx="80" cy="80" r="62" stroke="#1e293b" stroke-width="12" fill="none" />
            <circle id="breakArc" cx="80" cy="80" r="62" stroke="#ef4444" stroke-width="12" fill="none" stroke-linecap="round" />
          </svg>
          <div class="clock-center">
            <div class="clock-time" id="breakTime">—</div>
            <div class="clock-unit">Remaining</div>
          </div>
        </div>
        <div class="clock-title">Break</div>
        <div class="clock-sub" id="breakSub">30m break</div>
      </div>
    </div>
  </div>

  <div id="companionCardsGrid" class="grid cards-3 topRow" style="margin-top:14px">

<div id="truckLoadCard" class="card movableCard">
  <div class="label">Truck / Load</div>

  <div class="actions" style="margin:8px 0 10px 0">
    <button class="btn green" onclick="connectTruck()">Connect Truck</button>
    <span class="small" id="connectTruckStatus">Polls latest ELD telemetry</span>
  </div>

  <div class="stat-list">
    <div class="mini"><div class="k">Truck</div><div class="v" id="truckName">—</div></div>
    <div class="mini"><div class="k">Truck ID</div><div class="v" id="truckId">—</div></div>
    <div class="mini"><div class="k">Trailer</div><div class="v" id="trailer">—</div></div>
    <div class="mini"><div class="k">Cargo</div><div class="v" id="cargo">—</div></div>
    <div class="mini"><div class="k">Fuel</div><div class="v" id="fuel">—</div></div>
    <div class="mini"><div class="k">Coordinates</div><div class="v" id="coords">—</div></div>
  </div>
</div>

<div id="dutyActionsCard" class="card movableCard">
  <div class="label">Duty Actions</div>

  <div class="quick-duty">
    <button class="btn" onclick="setDuty('Off Duty')">Off Duty</button>
    <button class="btn" onclick="setDuty('Sleeper')">Sleeper</button>
    <button class="btn" onclick="setDuty('Driving')">Driving</button>
    <button class="btn" onclick="setDuty('On Duty')">On Duty</button>
  </div>

  <div class="footer-note">These buttons hit the same companion duty API used by the phone mirror.</div>

  <div style="margin-top:10px">
    <button class="btn purple" onclick="openBolModal()">Create BOL</button>
    <button class="btn blue" onclick="openLiveMap()">Open Live Map</button>
  </div>

  <div style="flex:1"></div>
</div>

<div id="inspectionCard" class="card inspection-card movableCard">
  <div class="label">Inspection Check</div>

  <div class="inspection-hero">
    <div class="inspection-hero-left">
      <div class="insp-icon" id="inspIcon">📋</div>
      <div>
        <div class="insp-title" id="inspTitle">Pretrip Inspection</div>
        <div class="insp-sub" id="inspSub">Checking inspection status...</div>
      </div>
    </div>
    <div class="status-pill neutral" id="inspPill">Checking</div>
    <div style="margin-top:14px">
  <button class="btn orange" onclick="openInspectionForm()">
    Open Inspection Form
  </button>
</div>
  </div>
      <div class="insp-mini-grid">
        <div class="insp-line">
          <div class="insp-line-title">Inspection Mode</div>
          <div class="insp-line-value" id="inspMode">Pretrip</div>
          <div class="insp-line-note" id="inspRequiredNote">Required before driving</div>
        </div>
        <div class="insp-line">
          <div class="insp-line-title">Inspection Status</div>
          <div class="insp-line-value" id="inspStatus">—</div>
          <div class="insp-line-note" id="inspLastDone">—</div>
        </div>
        <div class="insp-line">
          <div class="insp-line-title">Truck Inspection</div>
          <div class="insp-line-value" id="inspVehicle">—</div>
          <div class="insp-line-note" id="inspVehicleNote">—</div>
        </div>
        <div class="insp-line">
          <div class="insp-line-title">Trailer Inspection</div>
          <div class="insp-line-value" id="inspTrailer">—</div>
          <div class="insp-line-note" id="inspTrailerNote">—</div>
        </div>
      </div>

      <div class="alert-box ok" id="inspMalfunctionBox">
        <div class="alert-mark" id="inspMalfunctionIcon">✅</div>
        <div>
          <div class="alert-title" id="inspMalfunctionTitle">No malfunction indicators</div>
          <div class="alert-note" id="inspMalfunctionNote">ELD and truck systems reporting normal.</div>
        </div>
      </div>

      <div class="alert-box ok" id="inspDefectBox">
        <div class="alert-mark" id="inspDefectIcon">✅</div>
        <div>
          <div class="alert-title" id="inspDefectTitle">No defects found</div>
          <div class="alert-note" id="inspDefectNote">Truck and trailer inspection info will show here.</div>
        </div>
      </div>

      <div class="footer-note">Inspections are recorded in accordance with FMCSA 396.11 and 396.13.</div>
    </div>
  </div>

  <div class="grid cards-2" style="margin-top:14px">
    <div class="card">
      <div class="label">Dispatch</div>
      <input id="msgTo" placeholder="Optional recipient / conversation" />
      <textarea id="msgText" placeholder="Type a dispatch message..."></textarea>
      <div class="actions">
        <button class="btn green" onclick="sendMessage()">Send Message</button>
        <button class="btn" onclick="loadMessages()">Reload Inbox</button>
      </div>
      <div class="listbox" id="messagesBox" style="margin-top:12px">Loading...</div>
    </div>

    <div class="card">
      <div class="label">Live Data</div>
      <pre id="raw">Loading...</pre>
    </div>
  </div>
</div>

<script>
const CIRC = 2 * Math.PI * 62;

function setText(id, value){
  const el = document.getElementById(id);
  if (!el) return;
  el.textContent = (value === undefined || value === null || value === '') ? '—' : String(value);
}

function safeNum(v){
  const n = Number(v);
  return Number.isFinite(n) ? n : null;
}

function fmt1(v){
  const n = safeNum(v);
  return n === null ? '—' : n.toFixed(1);
}

function fmtTime(hours){
  const h = safeNum(hours);
  if (h === null) return '—';
  const totalMin = Math.max(0, Math.round(h * 60));
  const hh = Math.floor(totalMin / 60);
  const mm = totalMin % 60;
  return hh + ':' + String(mm).padStart(2,'0');
}

function pctOf(remaining, max){
  const r = safeNum(remaining);
  const m = safeNum(max);
  if (r === null || m === null || m <= 0) return 0;
  return Math.max(0, Math.min(1, r / m));
}

function setArc(id, percent, color){
  const p = Math.max(0, Math.min(1, percent || 0));
  const arc = document.getElementById(id);
  if (!arc) return;
  arc.setAttribute('stroke-dasharray', String(CIRC));
  arc.setAttribute('stroke-dashoffset', String(CIRC * (1 - p)));
  if (color) arc.setAttribute('stroke', color);
  arc.style.opacity = p <= 0 ? '0.35' : '1';
  arc.style.filter = p <= 0.15 ? 'drop-shadow(0 0 8px rgba(239,68,68,.18))' : 'none';
}

async function getJson(url){
  const r = await fetch(url, { cache:'no-store' });
  return await r.json();
}

function firstVal(){
  for(let i = 0; i < arguments.length; i++){
    const v = arguments[i];
    if(v !== undefined && v !== null && String(v).trim() !== '') return v;
  }
  return '';
}

function getClockState(remaining, max, mode){
  const r = safeNum(remaining);
  const m = safeNum(max);

  if (r === null || m === null || m <= 0) {
    return {
      state: 'none',
      color: '#334155',
      percent: 0,
      subClass: '',
      timeClass: '',
      cardClass: ''
    };
  }

  const pct = Math.max(0, Math.min(1, r / m));

  let warnAt = mode === 'break' ? 0.40 : 0.25;
  let dangerAt = mode === 'break' ? 0.18 : 0.10;

  let state = 'ok';
  let color = '#22c55e';

  if (pct <= dangerAt) {
    state = 'danger';
    color = '#ef4444';
  } else if (pct <= warnAt) {
    state = 'warn';
    color = '#f59e0b';
  } else {
    color = '#22c55e';
  }

  return {
    state,
    color,
    percent: pct,
    subClass: state === 'ok' ? '' : state,
    timeClass: state,
    cardClass: state
  };
}

function applyClockVisuals(cardId, timeId, subId, stateInfo, waitingText, normalText){
  const card = document.getElementById(cardId);
  const time = document.getElementById(timeId);
  const sub = document.getElementById(subId);

  if (card) {
    card.classList.remove('ok', 'warn', 'danger', 'pulse-danger');
    if (stateInfo.cardClass) card.classList.add(stateInfo.cardClass);
    if (stateInfo.state === 'danger') card.classList.add('pulse-danger');
  }

  if (time) {
    time.classList.remove('ok', 'warn', 'danger');
    if (stateInfo.timeClass) time.classList.add(stateInfo.timeClass);
  }

  if (sub) {
    sub.classList.remove('warn', 'danger');
    if (stateInfo.subClass) sub.classList.add(stateInfo.subClass);
    sub.textContent = stateInfo.state === 'none' ? waitingText : normalText;
  }
}

function renderClocks(duty){
  duty = duty || {};

  const drive = duty.driveRemainingHours ?? duty.driveRemaining;
  const shift = duty.shiftRemainingHours ?? duty.shiftRemaining;
  const cycle = duty.cycleRemainingHours ?? duty.cycleRemaining;
  const brk   = duty.breakRemainingHours ?? duty.breakRemaining;

  setText('driveTime', fmtTime(drive));
  setText('shiftTime', fmtTime(shift));
  setText('cycleTime', fmtTime(cycle));
  setText('breakTime', fmtTime(brk));

  const driveState = getClockState(drive, duty.driveMaxHours ?? 11, 'drive');
  const shiftState = getClockState(shift, duty.shiftMaxHours ?? 14, 'shift');
  const cycleState = getClockState(cycle, duty.cycleMaxHours ?? 70, 'cycle');
  const breakState = getClockState(brk, duty.breakMaxHours ?? 0.5, 'break');

  setArc('driveArc', driveState.percent, driveState.color);
  setArc('shiftArc', shiftState.percent, shiftState.color);
  setArc('cycleArc', cycleState.percent, cycleState.color);
  setArc('breakArc', breakState.percent, breakState.color);

  applyClockVisuals('driveCard', 'driveTime', 'driveSub', driveState, 'Waiting for duty data', '11h clock');
  applyClockVisuals('shiftCard', 'shiftTime', 'shiftSub', shiftState, 'Waiting for duty data', '14h clock');
  applyClockVisuals('cycleCard', 'cycleTime', 'cycleSub', cycleState, 'Waiting for duty data', '70h / 8 day');
  applyClockVisuals('breakCard', 'breakTime', 'breakSub', breakState, 'Waiting for duty data', '30m break');
}

function escapeHtml(s){
  return String(s)
    .replaceAll('&','&amp;')
    .replaceAll('<','&lt;')
    .replaceAll('>','&gt;')
    .replaceAll('"','&quot;');
}

function renderMessages(messages){
  const box = document.getElementById('messagesBox');
  if(!box) return;

  if(!messages || !Array.isArray(messages) || messages.length === 0){
    box.innerHTML = '<div style="color:#94a3b8">No messages yet.</div>';
    return;
  }

  box.innerHTML = messages.map(function(m){
    const from = firstVal(m.from, m.sender, m.driverName, 'Unknown');
    const text = firstVal(m.text, m.body, m.message, '');
    const when = firstVal(m.createdUtc, m.timestamp, m.sentUtc, '');
    return '<div class="msg">'
      + '<div class="msg-top">'
      + '<div class="msg-from">' + escapeHtml(from) + '</div>'
      + '<div class="msg-time">' + escapeHtml(when) + '</div>'
      + '</div>'
      + '<div class="msg-body">' + escapeHtml(text) + '</div>'
      + '</div>';
  }).join('');
}

function getByPath(obj, path){
  try{
    return path.split('.').reduce(function(o,k){ return o == null ? null : o[k]; }, obj);
  }catch{ return null; }
}

function readFirst(obj, names){
  for(const n of names){
    const v = getByPath(obj, n);
    if(v !== undefined && v !== null && String(v).trim() !== '') return v;
  }
  return '';
}

function setClass(id, base, cls){
  const el = document.getElementById(id);
  if(!el) return;
  el.className = base + (cls ? (' ' + cls) : '');
}

function fmtDate(v){
  if(!v) return '';
  try{
    const d = new Date(v);
    if(isNaN(d.getTime())) return String(v);
    return d.toLocaleString();
  }catch{ return String(v); }
}

function collectAtsMalfunctions(telemetry){
  const alerts = [];
  const rawJson = readFirst(telemetry, ['source.lastRawJson']);
  if(rawJson){
    try{
      const raw = JSON.parse(rawJson);
      const truck = raw.truck || {};
      if(truck.oilPressureWarningOn) alerts.push('Oil pressure warning');
      if(truck.waterTemperatureWarningOn) alerts.push('Water temperature warning');
      if(truck.batteryVoltageWarningOn) alerts.push('Battery voltage warning');
      if(truck.airPressureWarningOn || truck.airPressureEmergencyOn) alerts.push('Air pressure warning');
      if(truck.fuelWarningOn) alerts.push('Low fuel warning');
      if(raw.trailer && raw.trailer.attached && Number(raw.trailer.wear || 0) > 0.08) alerts.push('Trailer wear above normal');
    }catch{}
  }

  const damage = safeNum(readFirst(telemetry, ['source.lastSnapshot.damagePct', 'conditionPercent']));
  if(damage !== null && damage > 10) alerts.push('Truck damage reported: ' + fmt1(damage) + '%');

  return alerts;
}

async function openInspectionForm(){
  document.getElementById('inspectionModal').style.display = 'flex';

  setInput('inspVehicleBox', firstVal(
    document.getElementById('truckName')?.innerText,
    ''
  ));

  setInput('inspTrailerBox', firstVal(
    document.getElementById('trailer')?.innerText,
    ''
  ));

  await loadInspectionForm();
}

async function loadInspectionForm(){
  const mode = document.getElementById('inspModeSelect')?.value || 'pretrip';

  try{
    setText('inspFormStatus','Loading inspection...');

    const res = await fetch('/api/inspection?mode=' + encodeURIComponent(mode), {
      cache:'no-store'
    });

    const json = await res.json();
    const inspection = json?.inspection || json || {};

    const tractor = inspection.tractor || inspection.Tractor || [];
    const trailer = inspection.trailer || inspection.Trailer || [];

    renderInspectionList('inspTractorList', tractor);
    renderInspectionList('inspTrailerList', trailer);

    setText('inspFormStatus','Loaded ' + mode + ' inspection.');
  }
  catch(e){
    setText('inspFormStatus','Failed to load inspection.');
  }
}

function renderInspectionList(id, items){
  const box = document.getElementById(id);
  if(!box) return;

  if(!Array.isArray(items) || items.length === 0){
    box.innerHTML = '<div class="muted">No checklist items found.</div>';
    return;
  }

  box.innerHTML = items.map((x, i) => {
    const category = escapeHtml(firstVal(x.category, x.Category, 'General'));
    const name = escapeHtml(firstVal(x.name, x.Name, 'Inspection Item'));
    const ok = x.isOk === true || x.IsOk === true;
    const defect = x.isDefect === true || x.IsDefect === true;
    const note = escapeHtml(firstVal(x.note, x.Note, ''));

    return `
      <div class="checkRow" data-index="${i}">
        <div>
          <div class="checkName">${name}</div>
          <div class="checkCat">${category}</div>
        </div>

        <label><input type="checkbox" class="okBox" ${ok ? 'checked' : ''}> OK</label>
        <label><input type="checkbox" class="defectBox" ${defect ? 'checked' : ''}> Defect</label>

        <input class="input noteInput" value="${note}" placeholder="Note">
      </div>
    `;
  }).join('');
}

function collectInspectionList(id){
  const box = document.getElementById(id);
  if(!box) return [];

  return [...box.querySelectorAll('.checkRow')].map(row => {
    return {
      category: row.querySelector('.checkCat')?.innerText || '',
      name: row.querySelector('.checkName')?.innerText || '',
      isOk: row.querySelector('.okBox')?.checked === true,
      isDefect: row.querySelector('.defectBox')?.checked === true,
      note: row.querySelector('.noteInput')?.value || ''
    };
  });
}

async function saveInspectionForm(){
  const mode = document.getElementById('inspModeSelect')?.value || 'pretrip';

  const payload = {
    mode,
    vehicle: val('inspVehicleBox'),
    trailerId: val('inspTrailerBox'),
    tractor: collectInspectionList('inspTractorList'),
    trailer: collectInspectionList('inspTrailerList'),
    remarks: val('inspRemarks'),
    sign: document.getElementById('inspCertified')?.checked === true,
    driverSigName: document.getElementById('driverName')?.innerText || ''
  };

  try{
    const res = await fetch('/api/inspection?mode=' + encodeURIComponent(mode), {
      method:'POST',
      headers:{'Content-Type':'application/json'},
      body:JSON.stringify(payload)
    });

    const json = await res.json();

    if(json?.ok){
      setText('inspFormStatus','Inspection saved.');
      await loadInspectionStatus?.();
      closeInspectionModal();
    }else{
      setText('inspFormStatus','Save failed.');
    }
  }
  catch{
    setText('inspFormStatus','Save failed.');
  }
}

function showTab(id){
  document.querySelectorAll('.tabPanel').forEach(x => x.style.display = 'none');
  const el = document.getElementById(id);
  if(el) el.style.display = 'block';
}

async function openInspectionForm(){

  try{

    const res = await fetch('/api/inspection?mode=pretrip', {
      cache:'no-store'
    });

    const json = await res.json();

    const existing =
      json?.inspection ||
      {};

    document.getElementById('inspectionModal').style.display = 'flex';

    setInput('inspTruck', firstVal(
      existing.truckName,
      document.getElementById('truckName')?.innerText,
      ''
    ));

    setInput('inspTrailer', firstVal(
      existing.trailer,
      document.getElementById('trailer')?.innerText,
      ''
    ));

    setInput('inspOdometer', firstVal(
      existing.odometerMiles,
      ''
    ));

    setInput('inspNotes', firstVal(
      existing.notes,
      ''
    ));

  }catch{

    document.getElementById('inspectionModal').style.display = 'flex';
  }
}

function closeInspectionModal(){
  document.getElementById('inspectionModal').style.display = 'none';
}

async function submitInspection(){

  const payload = {
    truckName: val('inspTruck'),
    trailer: val('inspTrailer'),
    odometerMiles: val('inspOdometer'),
    notes: val('inspNotes'),
    passed: document.getElementById('inspPassed').checked,
    completedUtc: new Date().toISOString()
  };

  try{

    const res = await fetch('/api/inspection?mode=pretrip', {
      method:'POST',
      headers:{
        'Content-Type':'application/json'
      },
      body:JSON.stringify(payload)
    });

    const json = await res.json();

    if(json?.ok){

      document.getElementById('inspSub').innerText =
        'Inspection completed';

      document.getElementById('inspPill').innerText =
        'Complete';

      document.getElementById('inspPill').className =
        'status-pill good';

      closeInspectionModal();
    }

  }catch{
  }
}

function setInput(id,val){
  const el = document.getElementById(id);
  if(el) el.value = val || '';
}

async function loadLogs(){
  const status = document.getElementById('logsStatus');
  const box = document.getElementById('logsBox');

  try{
    if(status) status.textContent = 'Loading ELD logs...';

    const res = await fetch('/api/logs', { cache:'no-store' });
    const json = await res.json();

    const rows =
      json?.events ||
      json?.logs ||
      json?.items ||
      json?.entries ||
      json?.dutyEvents ||
      [];

    if(!Array.isArray(rows) || rows.length === 0){
      box.innerHTML = '<div class="muted">No log rows found.</div>';
      if(status) status.textContent = 'No logs available.';
      return;
    }

    box.innerHTML = `
      <div style="overflow:auto">
        <table style="width:100%; border-collapse:collapse">
          <thead>
            <tr>
              <th>Time</th>
              <th>Status</th>
              <th>Location</th>
              <th>Notes</th>
            </tr>
          </thead>
          <tbody>
            ${rows.map(r => `
              <tr>
                <td>${escapeHtml(firstVal(r.startLocal, r.startUtc, r.time, r.timestamp, '—'))}</td>
                <td>${escapeHtml(firstVal(r.status, r.dutyStatus, r.mode, '—'))}</td>
                <td>${escapeHtml(firstVal(r.location, r.locationText, r.city, '—'))}</td>
                <td>${escapeHtml(firstVal(r.notes, r.remark, r.comment, ''))}</td>
              </tr>
            `).join('')}
          </tbody>
        </table>
      </div>
    `;

    if(status) status.textContent = 'Logs synced from ELD.';
  }
  catch(e){
    if(status) status.textContent = 'Failed to load logs.';
    if(box) box.innerHTML = '<div class="bad">Could not read /api/logs.</div>';
  }
}

function escapeHtml(v){
  return String(v ?? '')
    .replaceAll('&','&amp;')
    .replaceAll('<','&lt;')
    .replaceAll('>','&gt;')
    .replaceAll('"','&quot;')
    .replaceAll("'","&#039;");
}

function collectInspectionDefects(inspection){
  const defects = [];
  const roots = [inspection, inspection.inspection, inspection.snapshot, inspection.result].filter(Boolean);

  for(const root of roots){
    const possibleArrays = [root.defects, root.Defects, root.items, root.Items, root.failures, root.Failures].filter(Array.isArray);
    for(const arr of possibleArrays){
      for(const item of arr){
        if(!item) continue;
        const failed = item.failed === true || item.isFailed === true || item.ok === false || item.checked === false || item.hasDefect === true || item.defect === true;
        const text = firstVal(item.name, item.item, item.label, item.description, item.notes, 'Inspection item');
        if(failed) defects.push(text);
      }
    }
  }

  const defectText = firstVal(
    readFirst(inspection, ['defect', 'defectsText', 'defectText', 'notes', 'issue', 'failureReason']),
    ''
  );
  if(defectText && !/none|no defect|pass/i.test(defectText)) defects.push(defectText);

  return defects;
}

function renderInspection(inspection, telemetry){
  inspection = inspection || {};
  telemetry = telemetry || {};

  const savedTruck = (() => { try { return JSON.parse(localStorage.getItem('ow_connected_truck') || '{}'); } catch { return {}; } })();
  const rawJson = readFirst(telemetry, ['source.lastRawJson']);
  let raw = {};
  try{ raw = rawJson ? JSON.parse(rawJson) : {}; }catch{}

  const truckName = firstVal(
    savedTruck.truckName,
    readFirst(telemetry, ['source.lastSnapshot.truckName','source.lastSnapshot.truckMakeModel','truckName','truckMakeModel']),
    raw.truck ? [raw.truck.make, raw.truck.model].filter(Boolean).join(' ') : '',
    'Truck not detected'
  );

  const truckId = firstVal(savedTruck.truckId, readFirst(telemetry, ['unitNumber','truckId','source.lastSnapshot.truckId']), raw.truck && raw.truck.id, 'No ID');
  const trailerName = firstVal(savedTruck.trailer, readFirst(telemetry, ['source.lastSnapshot.trailerName','trailer']), raw.trailer && raw.trailer.name, raw.trailer && raw.trailer.id, 'No trailer detected');
  const trailerAttached = (raw.trailer && raw.trailer.attached === true) || trailerName !== 'No trailer detected';

  const mode = firstVal(inspection.mode, inspection.inspectionMode, 'Pretrip');
  const statusRaw = firstVal(inspection.status, inspection.result, inspection.state, inspection.completedStatus, '');
  const complete = /complete|completed|pass|passed|done|ok|ready|compliant/i.test(statusRaw) || inspection.completed === true || inspection.isComplete === true || inspection.available === true;
  const unavailable = inspection.available === false;
  const defects = collectInspectionDefects(inspection);
  const malfunctions = collectAtsMalfunctions(telemetry);

  let state = 'warn';
  let title = 'Pretrip Inspection Required';
  let sub = 'Complete a pretrip inspection before driving.';
  let pill = 'Needs Inspection';
  let icon = '⚠️';

  if(unavailable){
    state = 'warn'; title = 'Inspection Needed'; sub = 'No completed pretrip inspection found yet.'; pill = 'Required'; icon = '📋';
  } else if(defects.length > 0 || malfunctions.length > 0){
    state = defects.length > 0 ? 'danger' : 'warn'; title = defects.length > 0 ? 'Inspection Has Defects' : 'Malfunction Active'; sub = 'Review truck/trailer items before moving.'; pill = defects.length > 0 ? 'Defects Found' : 'Malfunction'; icon = '⚠️';
  } else if(complete){
    state = 'ok'; title = 'Pretrip Inspection Complete'; sub = 'Truck and trailer inspection look good.'; pill = 'Compliant'; icon = '✅';
  }

  setText('inspIcon', icon);
  setText('inspTitle', title);
  setText('inspSub', sub);
  setText('inspPill', pill);
  setClass('inspPill', 'status-pill', state);

  setText('inspMode', mode);
  setText('inspStatus', firstVal(statusRaw, complete ? 'Complete' : 'Required'));
  setText('inspLastDone', firstVal(fmtDate(readFirst(inspection, ['completedUtc','lastCompletedUtc','updatedUtc','createdUtc','timestamp','date'])), complete ? 'Completed inspection found' : 'No completion time found'));
  setText('inspRequiredNote', complete ? 'No inspection required right now' : 'Required before driving');

  setText('inspVehicle', truckName);
  setText('inspVehicleNote', truckId);
  setText('inspTrailer', trailerName);
  setText('inspTrailerNote', trailerAttached ? 'Trailer attached / included in check' : 'No attached trailer detected');

  const malBoxState = malfunctions.length > 0 ? 'warn' : 'ok';
  setClass('inspMalfunctionBox', 'alert-box', malBoxState);
  setText('inspMalfunctionIcon', malfunctions.length > 0 ? '⚠️' : '✅');
  setText('inspMalfunctionTitle', malfunctions.length > 0 ? 'Malfunction / warning active' : 'No malfunction indicators');
  setText('inspMalfunctionNote', malfunctions.length > 0 ? malfunctions.join(' • ') : 'ELD and truck systems reporting normal.');

  const defectState = defects.length > 0 ? 'danger' : 'ok';
  setClass('inspDefectBox', 'alert-box', defectState);
  setText('inspDefectIcon', defects.length > 0 ? '⚠️' : '✅');
  setText('inspDefectTitle', defects.length > 0 ? (defects.length + ' defect' + (defects.length === 1 ? '' : 's') + ' noted') : 'No defects found');
  setText('inspDefectNote', defects.length > 0 ? defects.slice(0,4).join(' • ') : 'Truck and trailer inspection info is clear.');
}

function renderDashboard(data){
  const telemetry = data && data.telemetry ? data.telemetry : {};
  const duty = data && data.duty ? data.duty : {};
  const messages = data && data.messages ? data.messages : [];

  const truckDisplay = firstVal(telemetry.truckName, telemetry.truckMakeModel, 'No Truck Assigned');
  const truckIdDisplay = firstVal(telemetry.unitNumber, telemetry.truckId, 'No ID');

  setText('driverName', firstVal(telemetry.driverName, telemetry.driverId, 'Unknown Driver'));
  setText('driverMeta', truckDisplay + ' • ' + truckIdDisplay);

  setText('dutyStatus', firstVal(duty.status, duty.current, duty.mode, 'Unknown'));
  setText('engineDuty', (telemetry.engineOn ? 'Engine On' : 'Engine Off') + ' • ' + (telemetry.paused ? 'Paused' : 'Live'));

  setText('speed', fmt1(firstVal(telemetry.speedMph, 0)) + ' mph');
  setText('heading', firstVal(telemetry.heading != null ? ('Heading ' + fmt1(telemetry.heading) + '°') : '', 'No heading'));

  setText('location', firstVal(telemetry.location, [telemetry.city, telemetry.state].filter(Boolean).join(', '), 'Unknown'));
  setText('odometer', 'Odometer: ' + firstVal(telemetry.odometerMiles != null ? (fmt1(telemetry.odometerMiles) + ' mi') : '', '—'));

  setText('truckName', truckDisplay);
  setText('truckId', truckIdDisplay);
  setText('trailer', firstVal(telemetry.trailer, 'No trailer detected'));
  setText('cargo', firstVal(telemetry.cargo, 'No cargo detected'));

  setText('fuel',
    telemetry.fuelPercent != null
      ? (fmt1(telemetry.fuelPercent) + '%')
      : (telemetry.fuelGallons != null
          ? (fmt1(telemetry.fuelGallons) + ' gal' + (telemetry.fuelCapacityGallons != null ? (' / ' + fmt1(telemetry.fuelCapacityGallons)) : ''))
          : '—'));

  setText('coords',
    telemetry.latitude != null && telemetry.longitude != null
      ? (fmt1(telemetry.latitude) + ', ' + fmt1(telemetry.longitude))
      : '—');

  try{
    const savedTruck = JSON.parse(localStorage.getItem('ow_connected_truck') || '{}');

    if (savedTruck.truckName || savedTruck.truckId || savedTruck.trailer || savedTruck.cargo) {
      setText('truckName', savedTruck.truckName || 'Current Truck Not Detected');
      setText('truckId', savedTruck.truckId || 'No ID');
      setText('trailer', savedTruck.trailer || 'No trailer detected');
      setText('cargo', firstVal(savedTruck.cargo, savedTruck.route, 'No cargo detected'));
      setText('fuel', savedTruck.fuel || '—');
      setText('coords', savedTruck.coordinates || '—');
    }
  }catch{}

  renderClocks(duty);
  renderMessages(messages);

  const raw = document.getElementById('raw');
  if(raw) raw.textContent = JSON.stringify(data, null, 2);
}
async function refreshAll(){
  try{
    const data = await getJson('/api/dashboard');
    let inspection = {};
    try{
      const inspPayload = await getJson('/api/inspection?mode=pretrip');
      inspection = inspPayload && inspPayload.inspection ? inspPayload.inspection : inspPayload;
      if(inspPayload && inspPayload.mode && inspection && !inspection.mode) inspection.mode = inspPayload.mode;
    }catch{}
    renderDashboard(data);
    renderInspection(inspection, data && data.telemetry ? data.telemetry : {});
  }catch(err){
    const raw = document.getElementById('raw');
    if(raw) raw.textContent = String(err);
  }
}

function openLiveMap() {
    const base = window.location.origin || "http://127.0.0.1:5234";
    window.location.href = `${base}/map.html`;
}

async function connectTruck(){
  const status = document.getElementById('connectTruckStatus');

  try{
    if(status) status.textContent = 'Polling ELD telemetry...';

    const res = await fetch('/api/connect-truck', {
      method:'POST',
      cache:'no-store'
    });

    const json = await res.json();

    if(!json || json.ok !== true){
      if(status) status.textContent = 'No truck found.';
      return;
    }

    const cargoText = firstVal(
      json.cargo && json.route ? (json.cargo + ' • ' + json.route) : '',
      json.cargo,
      json.route,
      'No cargo detected'
    );

    setText('truckName', json.truckName || 'Current Truck Not Detected');
    setText('truckId', json.truckId || 'No ID');
    setText('trailer', json.trailer || 'No trailer detected');
    setText('cargo', cargoText);
    setText('fuel', json.fuel || '—');
    setText('coords', json.coordinates || '—');

    localStorage.setItem('ow_connected_truck', JSON.stringify({
      truckName: json.truckName || '',
      truckId: json.truckId || '',
      trailer: json.trailer || '',
      cargo: cargoText || '',
      route: json.route || '',
      fuel: json.fuel || '',
      coordinates: json.coordinates || ''
    }));

    if(status) status.textContent = 'Truck/load connected from ELD.';
  }
  catch{
    if(status) status.textContent = 'Connect truck failed.';
  }
}
async function setDuty(duty){
  await fetch('/api/duty', {
    method:'POST',
    headers:{ 'Content-Type':'application/json' },
    body: JSON.stringify({ duty: duty })
  });
}

function openBolModal(){

  document.getElementById('bolModal').style.display = 'flex';

  document.getElementById('bolDriver').value =
    document.getElementById('driverName')?.innerText || '';

  document.getElementById('bolTruck').value =
    document.getElementById('truckName')?.innerText || '';

  document.getElementById('bolCargo').value =
    document.getElementById('cargo')?.innerText || '';

  document.getElementById('bolTrailer').value =
    document.getElementById('trailer')?.innerText || '';

  document.getElementById('bolLoadNumber').value =
    'BOL-' + Date.now();
}

function closeBolModal(){
  document.getElementById('bolModal').style.display = 'none';
}

async function saveBol(){

  const payload = {
    loadNumber: val('bolLoadNumber'),
    pickup: val('bolPickup'),
    destination: val('bolDestination'),
    cargo: val('bolCargo'),
    trailer: val('bolTrailer'),
    weight: val('bolWeight'),
    driver: val('bolDriver'),
    truck: val('bolTruck'),
    createdUtc: new Date().toISOString()
  };

  try{

    localStorage.setItem(
      'ow_last_bol',
      JSON.stringify(payload)
    );

    document.getElementById('bolStatus').innerText =
      'BOL saved locally.';

  }catch{

    document.getElementById('bolStatus').innerText =
      'Failed to save BOL.';
  }
}

function val(id){
  return document.getElementById(id)?.value || '';
}

async function sendMessage(){
  const text = document.getElementById('msgText').value.trim();
  const to = document.getElementById('msgTo').value.trim();
  if(!text) return;

  const r = await fetch('/api/messages/send', {
    method:'POST',
    headers:{ 'Content-Type':'application/json' },
    body: JSON.stringify({ text: text, to: to })
  });

  const j = await r.json();
  if(j && j.ok){
    document.getElementById('msgText').value = '';
  }
  await loadMessages();
  await refreshAll();
}

async function loadMessages(){
  try{
    const data = await getJson('/api/messages');
    renderMessages(data);
  }catch(err){
    const box = document.getElementById('messagesBox');
    if(box) box.textContent = String(err);
  }
}

refreshAll();
setInterval(refreshAll, 5000);
</script>
    <div id="bolModal" class="modalOverlay" style="display:none">
  <div class="modalCard">

    <div class="modalHeader">
      <div class="title">Create Bill Of Lading</div>

      <button class="closeBtn" onclick="closeBolModal()">✕</button>
    </div>

    <div class="modalBody">

      <div class="field">
        <label>Load Number</label>
        <input id="bolLoadNumber" />
      </div>

      <div class="field">
        <label>Pickup Company</label>
        <input id="bolPickup" />
      </div>

      <div class="field">
        <label>Destination Company</label>
        <input id="bolDestination" />
      </div>

      <div class="field">
        <label>Cargo</label>
        <input id="bolCargo" />
      </div>

      <div class="field">
        <label>Trailer</label>
        <input id="bolTrailer" />
      </div>

      <div class="field">
        <label>Weight</label>
        <input id="bolWeight" />
      </div>

      <div class="field">
        <label>Driver</label>
        <input id="bolDriver" />
      </div>

      <div class="field">
        <label>Truck</label>
        <input id="bolTruck" />
      </div>

      <div style="margin-top:16px">
        <button class="btn green" onclick="saveBol()">
          Save BOL
        </button>
      </div>

      <div id="bolStatus" class="small" style="margin-top:12px"></div>

    </div>
  </div>
</div>
<div id="inspectionModal" class="modalOverlay" style="display:none">
  <div class="modalCard inspectionModalCard">

    <div class="modalHeader">
      <div>
        <div class="title">Truck / Trailer Inspection</div>
        <div class="muted">Mirrors ELD Pre-Trip / Post-Trip inspection checklist</div>
      </div>
      <button class="closeBtn" onclick="closeInspectionModal()">✕</button>
    </div>

    <div class="inspectionTopBar">
      <select id="inspModeSelect" class="input" onchange="loadInspectionForm()">
        <option value="pretrip">Pre-Trip</option>
        <option value="posttrip">Post-Trip</option>
        <option value="vehicle">Vehicle</option>
      </select>

      <input id="inspVehicleBox" class="input" placeholder="Vehicle / Truck" />
      <input id="inspTrailerBox" class="input" placeholder="Trailer" />

      <button class="btn green" onclick="saveInspectionForm()">Save</button>
    </div>

    <div class="inspCols">
      <div class="inspPanel">
        <div class="inspPanelTitle">Tractor</div>
        <div id="inspTractorList" class="checklist"></div>
      </div>

      <div class="inspPanel">
        <div class="inspPanelTitle">Trailer</div>
        <div id="inspTrailerList" class="checklist"></div>
      </div>
    </div>

    <textarea id="inspRemarks" class="input" placeholder="Remarks / defects / notes"></textarea>

    <label class="certLine">
      <input type="checkbox" id="inspCertified" checked />
      Driver certifies this inspection is accurate
    </label>

    <div id="inspFormStatus" class="small"></div>
  </div>
</div>

    </div>

  </div>

</div>
</body>
</html>
""";

            File.WriteAllText(index, html, Encoding.UTF8);
        }
    }
}