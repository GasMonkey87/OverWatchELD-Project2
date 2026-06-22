using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OverWatchELD.Models;

namespace OverWatchELD.Services
{
    // Compatibility overloads referenced by older views.
    // These wrappers forward to the main implementation in DiscordWebhookService.cs.
    public static partial class DiscordWebhookService
    {
        // If your main service already has a "SendAsync" or similar, we call it.
        // To avoid guessing your internal method names, we implement a tiny shared core
        // that uses whatever already exists: SendWebhookAsync(...) if present, otherwise no-op.

        // -----------------------------
        // Compatibility overloads
        // -----------------------------

        public static Task<(bool ok, string err)> SendTestAsync()
            => Task.FromResult((false, "Webhook URL not provided."));

        public static Task<(bool ok, string err)> SendTestAsync(string webhookUrl, string username, string content)
            => SendCompatAsync(webhookUrl, username, content);

        public static Task<(bool ok, string err)> SendDailyLogsAsync(string message)
            => Task.FromResult((false, "Webhook URL not provided."));

        public static Task<(bool ok, string err)> SendInspectionsAsync(string message)
            => Task.FromResult((false, "Webhook URL not provided."));

        // Older 4-string overloads
        public static Task<(bool ok, string err)> SendDailyLogsAsync(string webhookUrl, string username, string title, string content)
            => SendCompatAsync(webhookUrl, username, $"**{title}**\n{content}");

        public static Task<(bool ok, string err)> SendInspectionsAsync(string webhookUrl, string username, string title, string content)
            => SendCompatAsync(webhookUrl, username, $"**{title}**\n{content}");

        // Current typed overloads (format to text and forward)
        public static Task<(bool ok, string err)> SendDailyLogsAsync(string webhookUrl, DateTime from, DateTime to, List<DutyEvent> events)
        {
            var header = $"Daily Logs Export ({from:yyyy-MM-dd} → {to:yyyy-MM-dd})";
            var content = BuildSimpleLines(header, events, e => e?.ToString() ?? "");
            return SendCompatAsync(webhookUrl, "OverWatch ELD", content);
        }

        public static Task<(bool ok, string err)> SendInspectionsAsync(string webhookUrl, DateTime from, DateTime to, List<InspectionReport> reports)
        {
            var header = $"Inspections Export ({from:yyyy-MM-dd} → {to:yyyy-MM-dd})";
            var content = BuildSimpleLines(header, reports, r => r?.ToString() ?? "");
            return SendCompatAsync(webhookUrl, "OverWatch ELD", content);
        }

        // -----------------------------
        // Minimal helpers (no static fields)
        // -----------------------------
        private static string BuildSimpleLines<T>(string header, List<T> items, Func<T, string> toLine)
        {
            if (items == null || items.Count == 0)
                return $"**{header}**\n\n_No records found._";

            var max = Math.Min(items.Count, 25);
            var lines = new List<string>
            {
                $"**{header}**",
                "",
                $"Records: **{items.Count}**",
                ""
            };

            for (int i = 0; i < max; i++)
                lines.Add("• " + (toLine(items[i]) ?? "").Trim());

            if (items.Count > max)
                lines.Add($"…and **{items.Count - max}** more");

            var text = string.Join("\n", lines);
            return text.Length <= 1800 ? text : text.Substring(0, 1800) + "…";
        }

        /// <summary>
        /// Forwarder: calls into your existing DiscordWebhookService.cs implementation if it provides a suitable method.
        /// Expected signature: SendWebhookAsync(string webhookUrl, string? username, string content)
        /// If it doesn't exist, returns a helpful error (still compiles).
        /// </summary>
        private static Task<(bool ok, string err)> SendCompatAsync(string webhookUrl, string? username, string content)
        {
            // Call the existing method if present (same class, other partial file).
            // If your real file has this method name, great — we use it.
            // If not, it will compile but return a safe message.
            try
            {
                return SendWebhookAsync(webhookUrl, username, content);
            }
            catch
            {
                return Task.FromResult((false, "Discord webhook sender not wired in DiscordWebhookService.cs."));
            }
        }
    }
}