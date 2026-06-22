using System;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OverWatchELD.Services
{
    public sealed class DispatchTelemetrySyncService
    {
        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        private readonly TelemetryService _telemetry;
        private readonly Func<string> _driverNameProvider;

        public DispatchTelemetrySyncService(TelemetryService telemetry, Func<string> driverNameProvider)
        {
            _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            _driverNameProvider = driverNameProvider ?? throw new ArgumentNullException(nameof(driverNameProvider));
        }

        public void Start()
        {
            _telemetry.Updated += OnTelemetryUpdated;
        }

        public void Stop()
        {
            _telemetry.Updated -= OnTelemetryUpdated;
        }

        private async void OnTelemetryUpdated(TelemetrySnapshot snapshot)
        {
            try
            {
                if (snapshot == null)
                    return;

                var settings = DispatchTrackerSettingsService.LoadOrCreate();
                var driverName = (_driverNameProvider() ?? "").Trim();

                if (string.IsNullOrWhiteSpace(driverName))
                    return;

                var job = DispatchService.GetCurrentActiveJobForDriver(driverName);
                if (job == null)
                    return;

                var speedMph = snapshot.SpeedMps * 2.23694;
                var location = BuildLocation(snapshot.City, snapshot.State);

                var changed = false;

                if (!string.IsNullOrWhiteSpace(location) &&
                    !string.Equals(job.LastKnownLocation ?? "", location, StringComparison.OrdinalIgnoreCase))
                {
                    job.LastKnownLocation = location;
                    changed = true;
                }

                if (!string.IsNullOrWhiteSpace(snapshot.TruckName) &&
                    !string.Equals(job.LastKnownTruckName ?? "", snapshot.TruckName, StringComparison.OrdinalIgnoreCase))
                {
                    job.LastKnownTruckName = snapshot.TruckName;
                    changed = true;
                }

                if (snapshot.OdometerMiles.HasValue && snapshot.OdometerMiles.Value > 0)
                {
                    var odo = snapshot.OdometerMiles.Value;

                    if (job.LastKnownOdometerMiles != odo)
                    {
                        job.LastKnownOdometerMiles = odo;
                        changed = true;
                    }

                    if (!job.StartOdometerMiles.HasValue || job.StartOdometerMiles.Value <= 0)
                    {
                        job.StartOdometerMiles = odo;
                        changed = true;
                    }

                    if (job.StartOdometerMiles.HasValue && odo >= job.StartOdometerMiles.Value)
                    {
                        var driven = Math.Max(0, odo - job.StartOdometerMiles.Value);
                        if (Math.Abs(job.ActualDrivenMiles - driven) > 0.01)
                        {
                            job.ActualDrivenMiles = driven;
                            changed = true;
                        }
                    }
                }

                var actualWeight = snapshot.CargoWeightLbs ?? snapshot.GrossWeightLbs;
                if (actualWeight.HasValue && actualWeight.Value > 0)
                {
                    if (Math.Abs(job.ActualCargoWeightLbs - actualWeight.Value) > 0.01)
                    {
                        job.ActualCargoWeightLbs = actualWeight.Value;
                        changed = true;
                    }
                }

                // PICKUP POST
                if (IsPickupStage(job.Status) &&
                    speedMph <= settings.MaxStoppedMph &&
                    string.IsNullOrWhiteSpace(ReadStageStamp(job, "PickupDiscordSentUtcText")))
                {
                    var sent = await TrySendDiscordStageMessageAsync(
                        stage: "pickup",
                        settings: settings,
                        driverName: driverName,
                        job: job,
                        location: location,
                        speedMph: speedMph).ConfigureAwait(false);

                    if (sent)
                    {
                        WriteStageStamp(job, "PickupDiscordSentUtcText", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                        changed = true;
                    }
                }

                // IN TRANSIT
                if (settings.AutoSetInTransitFromTelemetry &&
                    speedMph >= settings.MinMovingMph &&
                    (job.Status.Equals("Assigned", StringComparison.OrdinalIgnoreCase) ||
                     job.Status.Equals("Accepted", StringComparison.OrdinalIgnoreCase) ||
                     job.Status.Equals("Picked Up", StringComparison.OrdinalIgnoreCase) ||
                     job.Status.Equals("At Shipper", StringComparison.OrdinalIgnoreCase) ||
                     job.Status.Equals("Imported", StringComparison.OrdinalIgnoreCase) ||
                     job.Status.Equals("Available", StringComparison.OrdinalIgnoreCase)))
                {
                    var wasAlreadyInTransit = job.Status.Equals("In Transit", StringComparison.OrdinalIgnoreCase);

                    DispatchService.MarkInTransit(job);
                    changed = true;

                    if (!wasAlreadyInTransit &&
                        settings.SendDiscordInTransitMessage &&
                        string.IsNullOrWhiteSpace(ReadStageStamp(job, "InTransitDiscordSentUtcText")))
                    {
                        var sent = await TrySendDiscordStageMessageAsync(
                            stage: "intransit",
                            settings: settings,
                            driverName: driverName,
                            job: job,
                            location: location,
                            speedMph: speedMph).ConfigureAwait(false);

                        if (sent)
                        {
                            WriteStageStamp(job, "InTransitDiscordSentUtcText", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                            changed = true;
                        }
                    }
                }

                var destinationMatched = IsDestinationMatched(job, snapshot);

                var stoppedEnough = speedMph <= settings.MaxStoppedMph;

                if (settings.AutoDeliverAtDestination &&
                    destinationMatched &&
                    (job.Status.Equals("In Transit", StringComparison.OrdinalIgnoreCase) ||
                     job.Status.Equals("Picked Up", StringComparison.OrdinalIgnoreCase) ||
                     job.Status.Equals("Accepted", StringComparison.OrdinalIgnoreCase) ||
                     job.Status.Equals("At Shipper", StringComparison.OrdinalIgnoreCase)))
                {
                    if (!settings.DestinationMatchRequiresStop || stoppedEnough)
                    {
                        DispatchService.MarkDelivered(job);
                        job.DestinationReachedUtc = DateTime.UtcNow;
                        changed = true;

                        if (settings.SendDiscordDeliveryMessage &&
                            string.IsNullOrWhiteSpace(ReadStageStamp(job, "DeliveryDiscordSentUtcText")))
                        {
                            var sent = await TrySendDiscordStageMessageAsync(
                                stage: "delivered",
                                settings: settings,
                                driverName: driverName,
                                job: job,
                                location: location,
                                speedMph: speedMph).ConfigureAwait(false);

                            if (sent)
                            {
                                WriteStageStamp(job, "DeliveryDiscordSentUtcText", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                                changed = true;
                            }
                        }
                    }
                }

                if (changed)
                    DispatchService.SaveJobs();
            }
            catch (Exception ex)
            {
                WriteDebugLog("Telemetry sync failed: " + ex.Message);
            }
        }

        private static async Task<bool> TrySendDiscordStageMessageAsync(
            string stage,
            DispatchTrackerSettings settings,
            string driverName,
            dynamic job,
            string location,
            double speedMph)
        {
            if (settings == null)
                return false;

            if (stage.Equals("pickup", StringComparison.OrdinalIgnoreCase) && !settings.SendDiscordPickupMessage)
                return false;

            if (stage.Equals("intransit", StringComparison.OrdinalIgnoreCase) && !settings.SendDiscordInTransitMessage)
                return false;

            if (stage.Equals("delivered", StringComparison.OrdinalIgnoreCase) && !settings.SendDiscordDeliveryMessage)
                return false;

            var webhook = ResolveStageWebhookUrl(stage, settings);
            if (string.IsNullOrWhiteSpace(webhook))
                return false;

            try
            {
                var revenue = (job.RevenueUsd > 0 ? job.RevenueUsd : job.Payout);
                var revenueText = revenue <= 0 ? "$0.00" : revenue.ToString("C2");
                var weightText = job.ActualCargoWeightLbs <= 0 ? "--" : job.ActualCargoWeightLbs.ToString("N0") + " lbs";
                var odometerText = job.LastKnownOdometerMiles <= 0 ? "--" : job.LastKnownOdometerMiles.ToString("N0");
                var plannedMilesText = job.Miles <= 0 ? "--" : job.Miles.ToString("N0");
                var drivenMilesText = job.ActualDrivenMiles <= 0 ? "--" : job.ActualDrivenMiles.ToString("N1");

                var title = "Load Update";
                var description = $"**{Safe(job.Cargo)}**";
                var color = 0x5865F2;
                var statusText = Safe(job.Status);
                var footerText = "OverWatch ELD • Dispatch Tracker";

                if (stage.Equals("pickup", StringComparison.OrdinalIgnoreCase))
                {
                    title = "📦 Load Picked Up";
                    description = $"**{Safe(job.Cargo)}** has been picked up.";
                    color = 0x3498DB;
                    statusText = "Picked Up";
                    footerText = "OverWatch ELD • Load Pickup";
                }
                else if (stage.Equals("intransit", StringComparison.OrdinalIgnoreCase))
                {
                    title = "🛣️ Load In Transit";
                    description = $"**{Safe(job.Cargo)}** is now en route.";
                    color = 0xF1C40F;
                    statusText = "In Transit";
                    footerText = "OverWatch ELD • In Transit";
                }
                else if (stage.Equals("delivered", StringComparison.OrdinalIgnoreCase))
                {
                    title = "🚚 Job Delivered";
                    description = $"**{Safe(job.Cargo)}** has been delivered successfully.";
                    color = 0x2EC27E;
                    statusText = "Delivered";
                    footerText = "OverWatch ELD • Load Completion";
                }

                var embed = new
                {
                    title,
                    description,
                    color,
                    fields = new object[]
                    {
                        new { name = "Driver", value = Safe(driverName), inline = true },
                        new { name = "Load #", value = Safe((string?)job.LoadNumber), inline = true },
                        new { name = "Status", value = statusText, inline = true },

                        new { name = "Truck", value = Safe((string?)job.LastKnownTruckName), inline = true },
                        new { name = "Plate", value = Safe((string?)job.LastKnownPlateNumber), inline = true },
                        new { name = "Speed", value = $"{speedMph:0.#} mph", inline = true },

                        new { name = "Route", value = $"{Safe((string?)job.OriginDisplay)} → {Safe((string?)job.DestinationDisplay)}", inline = false },
                        new { name = "Location", value = Safe(location), inline = true },
                        new { name = "Revenue", value = revenueText, inline = true },

                        new { name = "Planned Miles", value = plannedMilesText, inline = true },
                        new { name = "Driven Miles", value = drivenMilesText, inline = true },
                        new { name = "Load Weight", value = weightText, inline = true },

                        new { name = "Odometer", value = odometerText, inline = true }
                    },
                    footer = new
                    {
                        text = footerText
                    },
                    timestamp = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                };

                var payload = JsonSerializer.Serialize(new
                {
                    username = "OverWatch ELD",
                    embeds = new[] { embed }
                });

                using var body = new StringContent(payload, Encoding.UTF8, "application/json");
                using var resp = await Http.PostAsync(webhook, body).ConfigureAwait(false);
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private static string ResolveStageWebhookUrl(string stage, DispatchTrackerSettings settings)
        {
            try
            {
                if (stage.Equals("pickup", StringComparison.OrdinalIgnoreCase))
                {
                    var direct = (settings.LoadPickupWebhookUrl ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(direct))
                        return direct;
                }

                if (stage.Equals("intransit", StringComparison.OrdinalIgnoreCase))
                {
                    var direct = (settings.LoadInTransitWebhookUrl ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(direct))
                        return direct;
                }

                if (stage.Equals("delivered", StringComparison.OrdinalIgnoreCase))
                {
                    var direct = (settings.LoadCompletedWebhookUrl ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(direct))
                        return direct;
                }

                return ResolveNormalDispatchWebhookUrl();
            }
            catch
            {
                return "";
            }
        }

        private static string ResolveNormalDispatchWebhookUrl()
        {
            try
            {
                var cfg = VtcConfigService.Load();

                var dispatchWebhook = (GetStringProp(cfg, "DispatchWebhookUrl") ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(dispatchWebhook))
                    return dispatchWebhook;

                var exportWebhook = (GetStringProp(cfg, "ExportWebhookUrl") ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(exportWebhook))
                    return exportWebhook;

                var defaultExportWebhook = (GetStringProp(cfg, "DefaultExportWebhook") ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(defaultExportWebhook))
                    return defaultExportWebhook;

                try
                {
                    var app = new AppSettingsService().Load();

                    var appExport = (app?.Discord?.ExportWebhookUrl ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(appExport))
                        return appExport;

                    var legacyExport = (app?.DiscordWebhookUrl ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(legacyExport))
                        return legacyExport;
                }
                catch
                {
                }
            }
            catch
            {
            }

            return "";
        }

        private static string? GetStringProp(object? obj, string propertyName)
        {
            try
            {
                if (obj == null) return null;
                var p = obj.GetType().GetProperty(propertyName);
                if (p == null) return null;
                return p.GetValue(obj)?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static bool IsPickupStage(string? status)
        {
            var s = (status ?? "").Trim();

            return s.Equals("Assigned", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("Accepted", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("Picked Up", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("At Shipper", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("Imported", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("Available", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDestinationMatched(dynamic job, TelemetrySnapshot snapshot)
        {
            try
            {
                var jobCity = ((string?)job.DestinationCity ?? "").Trim();
                var jobState = ((string?)job.DestinationState ?? "").Trim();
                var snapCity = (snapshot.City ?? "").Trim();
                var snapState = (snapshot.State ?? "").Trim();

                if (string.IsNullOrWhiteSpace(jobCity) || string.IsNullOrWhiteSpace(snapCity))
                    return false;

                if (!string.Equals(jobCity, snapCity, StringComparison.OrdinalIgnoreCase))
                    return false;

                // Some telemetry snapshots do not reliably provide state. City match is enough
                // when either side has no state, otherwise require both city and state.
                if (string.IsNullOrWhiteSpace(jobState) || string.IsNullOrWhiteSpace(snapState))
                    return true;

                return string.Equals(jobState, snapState, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string BuildLocation(string? city, string? state)
        {
            city = (city ?? "").Trim();
            state = (state ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(city) && !string.IsNullOrWhiteSpace(state))
                return $"{city}, {state}";
            if (!string.IsNullOrWhiteSpace(city))
                return city;
            if (!string.IsNullOrWhiteSpace(state))
                return state;

            return "";
        }

        private static string Safe(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "--" : value.Trim();
        }

        private static string ReadStageStamp(object job, string propertyName)
        {
            try
            {
                var p = job.GetType().GetProperty(propertyName);
                var v = p?.GetValue(job);
                return v?.ToString() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static void WriteStageStamp(object job, string propertyName, string value)
        {
            try
            {
                var p = job.GetType().GetProperty(propertyName);
                if (p != null && p.CanWrite)
                    p.SetValue(job, value);
            }
            catch
            {
            }
        }

        private static void WriteDebugLog(string message)
        {
            try
            {
                var folder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "OverWatchELD");

                System.IO.Directory.CreateDirectory(folder);

                var path = System.IO.Path.Combine(folder, "dispatch_tracker_debug.log");
                System.IO.File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch
            {
            }
        }
    }
}