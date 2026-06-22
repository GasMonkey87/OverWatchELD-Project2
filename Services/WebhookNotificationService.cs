using OverWatchELD.Models;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public sealed class WebhookNotificationService
    {
        private static readonly HttpClient _http = new HttpClient();
        private readonly DiscordWebhookSettingsService _settingsService = new();

        public async System.Threading.Tasks.Task<bool> PostDriverSubmissionAsync(DriverSubmission submission)
        {
            var settings = _settingsService.Load();
            var url = settings.SubmissionWebhookUrl?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(url))
                return false;

            var content = new
            {
                content =
                    $"**Driver Submission**\n" +
                    $"Type: {submission.SubmissionType}\n" +
                    $"Title: {submission.Title}\n" +
                    $"Amount: {(submission.Amount.HasValue ? submission.Amount.Value.ToString("C") : "-")}\n" +
                    $"Truck: {submission.TruckName}\n" +
                    $"User: {submission.DiscordUsername}\n" +
                    $"Details: {submission.Details}"
            };

            var json = JsonSerializer.Serialize(content);
            var resp = await _http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
            return resp.IsSuccessStatusCode;
        }

        public async System.Threading.Tasks.Task<bool> PostFleetEventAsync(string message)
        {
            var settings = _settingsService.Load();
            var url = settings.FleetWebhookUrl?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(url))
                return false;

            var json = JsonSerializer.Serialize(new { content = message });
            var resp = await _http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
            return resp.IsSuccessStatusCode;
        }
    }
}
