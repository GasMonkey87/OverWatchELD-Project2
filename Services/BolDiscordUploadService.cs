using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace OverWatchELD.Services
{
    public sealed class BolDiscordUploadService
    {
        public static BolDiscordUploadService Shared { get; } = new();

        private readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        private string LogPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OverWatchELD",
                "bol_upload.log");

        private BolDiscordUploadService()
        {
        }

        public async Task UploadAsync(string loadNumber, string pdfPath, string status)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(loadNumber) ||
                    string.IsNullOrWhiteSpace(pdfPath) ||
                    !File.Exists(pdfPath))
                {
                    Log("Upload skipped: missing load number or pdf path.");
                    return;
                }

                var cfg = VtcConfigService.LoadOrCreate();
                var baseUrl = (cfg.BotApiBaseUrl ?? "").Trim().TrimEnd('/');
                if (string.IsNullOrWhiteSpace(baseUrl))
                {
                    Log("Upload skipped: BotApiBaseUrl empty.");
                    return;
                }

                using var form = new MultipartFormDataContent();
                form.Add(new StringContent(loadNumber), "loadNumber");
                form.Add(new StringContent(status ?? ""), "status");

                await using var fs = File.OpenRead(pdfPath);
                using var fileContent = new StreamContent(fs);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                form.Add(fileContent, "file", Path.GetFileName(pdfPath));

                var resp = await _http.PostAsync($"{baseUrl}/api/loads/bol/upload", form);
                var body = await resp.Content.ReadAsStringAsync();

                Log($"UPLOAD {loadNumber} status={status} HTTP {(int)resp.StatusCode} {body}");

                if (!resp.IsSuccessStatusCode)
                    throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {body}");
            }
            catch (Exception ex)
            {
                Log("Upload failed: " + ex);
            }
        }

        private void Log(string message)
        {
            try
            {
                var dir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch
            {
            }
        }
    }
}
