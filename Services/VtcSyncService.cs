using System;
using DutyStatus = OverWatchELD.Models.DutyStatus;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OverWatchELD.Models;

namespace OverWatchELD.Services
{
    /// <summary>
    /// VTC server sync.
    /// Legacy behavior: still supports the old webhook post on duty changes.
    /// Phase 7 behavior: also syncs the current driver/VTC record to the website portal backend.
    /// </summary>
    public static class VtcSyncService
    {
        private static readonly HttpClient _http = new HttpClient();
        private static DateTimeOffset _lastPostUtc = DateTimeOffset.MinValue;
        private static DateTimeOffset _lastWebsiteSyncUtc = DateTimeOffset.MinValue;
        private static DutyStatus _lastStatus = DutyStatus.Off;
        private static DateTimeOffset _lastStatusStartUtc = DateTimeOffset.MinValue;

        public static void Initialize()
        {
            try
            {
                EldEngine.StatusChanged += OnStatusChanged;
            }
            catch { }
        }

        public static async System.Threading.Tasks.Task PullWebsiteNowAsync()
        {
            await new VtcWebsiteSyncService().PullFromWebsiteAsync().ConfigureAwait(false);
        }

        public static async System.Threading.Tasks.Task PushWebsiteNowAsync()
        {
            await new VtcWebsiteSyncService().PushToWebsiteAsync().ConfigureAwait(false);
            await new VtcWebsiteSyncService().PushCurrentDriverAsync().ConfigureAwait(false);
            await new VtcWebsiteSyncService().PushGaragesAsync().ConfigureAwait(false);
        }

        private static async void OnStatusChanged(DutyStatus newStatus)
        {
            try
            {
                var info = VtcInfoStore.Load();
                var settings = new AppSettingsService().Load();
                var now = EldClock.UtcNow;
                var statusStart = EldEngine.CurrentStatusStartUtc;

                // basic rate limit so we don't spam if status toggles rapidly
                if ((now - _lastPostUtc) < TimeSpan.FromSeconds(2))
                    return;

                if (newStatus == _lastStatus && statusStart == _lastStatusStartUtc)
                    return;

                _lastPostUtc = now;
                _lastStatus = newStatus;
                _lastStatusStartUtc = statusStart;

                // Phase 7: keep website roster/profile fresh on duty changes.
                try
                {
                    if ((now - _lastWebsiteSyncUtc) > TimeSpan.FromSeconds(20))
                    {
                        _lastWebsiteSyncUtc = now;
                        await new VtcWebsiteSyncService().PushCurrentDriverAsync().ConfigureAwait(false);
                    }
                }
                catch
                {
                    // ignore network failures
                }

                // Backwards compat: keep supporting legacy webhook if configured.
                var webhook = (settings.Discord?.VtcSyncWebhookUrl ?? "").Trim();
                if (string.IsNullOrWhiteSpace(webhook))
                    webhook = (info.WebhookUrl ?? "").Trim();

                if (string.IsNullOrWhiteSpace(webhook))
                    return;

                var payload = new
                {
                    type = "duty_status",
                    utc = now,
                    company = info.CompanyName,
                    driver = info.DriverName,
                    unit = info.UnitNumber,
                    status = newStatus.ToString(),
                    statusStartUtc = statusStart
                };

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });
                var req = new HttpRequestMessage(HttpMethod.Post, webhook)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

                if (!string.IsNullOrWhiteSpace(info.ApiKey))
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", info.ApiKey);

                await _http.SendAsync(req).ConfigureAwait(false);
            }
            catch
            {
                // ignore network failures
            }
        }
    }
}
