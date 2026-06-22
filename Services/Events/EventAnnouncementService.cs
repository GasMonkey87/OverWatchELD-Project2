using OverWatchELD.Models.Events;
using System;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OverWatchELD.Services.Events
{
    public sealed class EventAnnouncementService
    {
        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        public static async Task FireAsync(EventItem item)
        {
            try
            {
                if (item == null)
                    return;

                var settings = VtcConfigService.Load();
                if (settings == null)
                    return;

                var webhook = (settings.EventWebhookUrl ?? "").Trim();
                var botBase = (settings.BotApiBaseUrl ?? "").Trim().TrimEnd('/');
                var guildId = (settings.GuildId ?? "").Trim();

                var startLocal = item.EventDate.ToString("f", CultureInfo.InvariantCulture);
                var endLocal = "";

                // ---------------------------------
                // 1) Direct Discord webhook post
                // ---------------------------------
                if (!string.IsNullOrWhiteSpace(webhook))
                {
                    var webhookPayload = new
                    {
                        content = "@everyone",
                        embeds = new[]
                        {
                            new
                            {
                                title = $"📢 New Event Created: {item.Title}",
                                description =
                                    $"**Type:** {(item.EventType ?? "").Trim()}\n" +
                                    $"**Date:** {startLocal}\n" +
                                    $"**Location:** {(item.Location ?? "").Trim()}\n" +
                                    $"**Host:** {(item.Host ?? "").Trim()}\n\n" +
                                    $"{(item.Notes ?? "").Trim()}",
                                footer = new
                                {
                                    text = "OverWatch ELD Events"
                                },
                                timestamp = DateTime.UtcNow.ToString("o")
                            }
                        }
                    };

                    using var webhookBody = new StringContent(
                        JsonSerializer.Serialize(webhookPayload),
                        Encoding.UTF8,
                        "application/json");

                    await Http.PostAsync(webhook, webhookBody);
                }

                // ---------------------------------
                // 2) Bot announcements route
                // ---------------------------------
                if (!string.IsNullOrWhiteSpace(botBase) && !string.IsNullOrWhiteSpace(guildId))
                {
                    var botPayload = new
                    {
                        guildId = guildId,
                        title = (item.Title ?? "").Trim(),
                        description = (item.Notes ?? "").Trim(),
                        location = (item.Location ?? "").Trim(),
                        startLocal = startLocal,
                        endLocal = endLocal,
                        createdBy = (item.Host ?? "").Trim(),
                        mentionText = "@everyone"
                    };

                    using var botBody = new StringContent(
                        JsonSerializer.Serialize(botPayload),
                        Encoding.UTF8,
                        "application/json");

                    await Http.PostAsync($"{botBase}/api/vtc/events/announce", botBody);
                }
            }
            catch
            {
                // Never break event creation if posting fails
            }
        }
    }
}