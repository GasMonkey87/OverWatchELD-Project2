using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OverWatchELD.Services
{
    /// <summary>
    /// Pings the bot so roster can show "Online" while ELD is open.
    /// Under-the-hood only (no UI changes).
    /// </summary>
    public sealed class VtcPresencePingService : IDisposable
    {
        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private Timer? _timer;

        public void Start()
        {
            Stop();
            _timer = new Timer(async _ => await TickAsync(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30));
        }

        public void Stop()
        {
            try { _timer?.Dispose(); } catch { }
            _timer = null;
        }

        public void Dispose()
        {
            Stop();
            try { _http.Dispose(); } catch { }
        }

        private async Task TickAsync()
        {
            try
            {
                // Only ping if linked + discord enabled
                var cfg = VtcConfigService.Load();
                var link = VtcLinkService.GetLink();

                if (!cfg.Enabled) return;
                if (!link.Linked) return;

                // bot URL (default 8080)
                var botBase = (cfg.BotApiBaseUrl ?? "").Trim().TrimEnd('/');

                var driverName = (link.DriverName ?? "").Trim();
                var driverKey = (link.DriverKey ?? "").Trim();

                if (string.IsNullOrWhiteSpace(driverKey))
                    driverKey = VtcLinkService.GetDriverKey(driverName);

                if (string.IsNullOrWhiteSpace(driverName))
                    driverName = "Driver";

                var gid = (cfg.Discord?.GuildId ?? "0").Trim();
                var url = botBase + "/api/vtc/presence" + (string.IsNullOrWhiteSpace(gid) || gid=="0" ? "" : ("?guildId=" + Uri.EscapeDataString(gid)));

                var payload = JsonSerializer.Serialize(new
                {
                    driverKey,
                    driverName
                });

                using var resp = await _http.PostAsync(url, new StringContent(payload, Encoding.UTF8, "application/json"));
                // ignore response (best-effort)
            }
            catch
            {
                // best-effort: never crash ELD
            }
        }
    }
}