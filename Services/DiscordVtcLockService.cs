using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OverWatchELD.Models;

namespace OverWatchELD.Services
{
    /// <summary>
    /// Polls a Discord-bot HTTP endpoint to lock the ELD to a single VTC (1 guild = 1 VTC).
    /// If the user is a member of the guild, the VTC/company is forced and cannot be changed.
    /// Leaving the guild unlocks the VTC.
    /// </summary>
    public sealed class DiscordVtcLockService : IDisposable
    {
        private readonly HttpClient _http;
        private readonly Timer _timer;

        private volatile bool _isRunning;
        private volatile bool _disposed;

        public event Action<DiscordVtcBinding>? BindingUpdated;

        public TimeSpan PollInterval { get; private set; } = TimeSpan.FromSeconds(30);

        public DiscordVtcLockService()
        {
            _http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(6)
            };

            // start stopped
            _timer = new Timer(async _ => await TickAsync().ConfigureAwait(false));
        }

        public void Start(TimeSpan? interval = null)
        {
            if (_disposed) return;
            if (interval.HasValue) PollInterval = interval.Value;

            _isRunning = true;
            _timer.Change(TimeSpan.Zero, PollInterval);
        }

        public void Stop()
        {
            if (_disposed) return;
            _isRunning = false;
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        private async Task TickAsync()
        {
            if (_disposed || !_isRunning) return;

            try
            {
                var info = VtcInfoStore.Load();

                // Only enable when configured
                if (string.IsNullOrWhiteSpace(info.DiscordGuildId) ||
                    string.IsNullOrWhiteSpace(info.DiscordUserId) ||
                    string.IsNullOrWhiteSpace(info.DiscordLockApiBaseUrl))
                {
                    return;
                }

                // Endpoint is owned by your bot; it enforces 1 guild -> 1 VTC.
                var baseUrl = info.DiscordLockApiBaseUrl.Trim().TrimEnd('/');
                var url = $"{baseUrl}/api/vtc-binding?discordUserId={Uri.EscapeDataString(info.DiscordUserId)}";

                var json = await _http.GetStringAsync(url).ConfigureAwait(false);
                var binding = JsonSerializer.Deserialize<DiscordVtcBinding>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new DiscordVtcBinding { locked = false, isMember = false };

                ApplyBinding(binding);
                BindingUpdated?.Invoke(binding);
            }
            catch
            {
                // Silent fail: bot could be offline. We do NOT unlock automatically on transient errors.
            }
        }

        private static void ApplyBinding(DiscordVtcBinding binding)
        {
            var info = VtcInfoStore.Load();
            var wasLocked = info.IsLockedToDiscord;

            // If user is member and endpoint says locked => lock it
            if (binding.isMember && binding.locked)
            {
                info.IsLockedToDiscord = true;
                info.DiscordGuildId = info.DiscordGuildId; // keep

                if (!string.IsNullOrWhiteSpace(binding.guildId))
                    info.DiscordGuildId = binding.guildId!;

                if (!string.IsNullOrWhiteSpace(binding.vtcId))
                    info.VtcId = binding.vtcId!;

                if (!string.IsNullOrWhiteSpace(binding.vtcName))
                {
                    info.VtcName = binding.vtcName!;
                    info.CompanyName = binding.vtcName!;
                }
            }
            else
            {
                // Not a member => unlock
                info.IsLockedToDiscord = false;
                info.VtcId = info.VtcId ?? "";
                info.VtcName = info.VtcName ?? "";
            }

            // Only save if changed
            if (wasLocked != info.IsLockedToDiscord)
            {
                VtcInfoStore.Save(info);
            }
            else
            {
                // Still locked: save if company name / ids updated by binding
                if (info.IsLockedToDiscord && !string.IsNullOrWhiteSpace(binding.vtcName) && info.CompanyName != binding.vtcName)
                {
                    info.CompanyName = binding.vtcName!;
                    VtcInfoStore.Save(info);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _timer.Dispose(); } catch { }
            try { _http.Dispose(); } catch { }
        }
    }
}
