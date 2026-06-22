using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OverWatchELD.Services
{
    /// <summary>
    /// Local + remote VTC announcements service.
    /// - Saves announcements locally to %AppData%\ATS_ELD\vtc_announcements.json
    /// - Can also publish announcements to the bot API
    /// </summary>
    public static class CompanyAnnouncementsService
    {
        private static readonly object _lock = new();

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        private static readonly string Folder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ATS_ELD");

        private static readonly string FilePath = Path.Combine(Folder, "vtc_announcements.json");

        public sealed class Announcement
        {
            public string Id { get; set; } = Guid.NewGuid().ToString("N");
            public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
            public string Title { get; set; } = "";
            public string Body { get; set; } = "";
            public string Author { get; set; } = "";
        }

        public sealed class PublishResult
        {
            public bool Ok { get; set; }
            public string Error { get; set; } = "";
            public string ResponseText { get; set; } = "";
            public Announcement? Announcement { get; set; }
        }

        public static List<Announcement> LoadAll()
        {
            lock (_lock)
            {
                try
                {
                    EnsureStorage();

                    if (!File.Exists(FilePath))
                        return new List<Announcement>();

                    var json = File.ReadAllText(FilePath);
                    var list = JsonSerializer.Deserialize<List<Announcement>>(json, JsonOpts) ?? new List<Announcement>();

                    return list
                        .OrderByDescending(a => a.CreatedUtc)
                        .ToList();
                }
                catch
                {
                    return new List<Announcement>();
                }
            }
        }

        public static Announcement Add(string title, string body, string author)
        {
            lock (_lock)
            {
                EnsureStorage();

                var list = LoadAll();

                var a = new Announcement
                {
                    Id = Guid.NewGuid().ToString("N"),
                    CreatedUtc = DateTimeOffset.UtcNow,
                    Title = (title ?? "").Trim(),
                    Body = (body ?? "").Trim(),
                    Author = (author ?? "").Trim()
                };

                list.Insert(0, a);

                // Keep last 50
                list = list.Take(50).ToList();

                var json = JsonSerializer.Serialize(list, JsonOpts);
                File.WriteAllText(FilePath, json);

                return a;
            }
        }

        public static bool Delete(string id)
        {
            lock (_lock)
            {
                try
                {
                    EnsureStorage();

                    var list = LoadAll();
                    var removed = list.RemoveAll(x => string.Equals(x.Id, id ?? "", StringComparison.OrdinalIgnoreCase)) > 0;

                    if (!removed)
                        return false;

                    var json = JsonSerializer.Serialize(list.Take(50).ToList(), JsonOpts);
                    File.WriteAllText(FilePath, json);

                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static async Task<PublishResult> AddAndPublishAsync(
            string botApiBaseUrl,
            string guildId,
            string title,
            string body,
            string author)
        {
            var local = Add(title, body, author);

            var publish = await PublishToBotAsync(botApiBaseUrl, guildId, local.Title, local.Body, local.Author);
            publish.Announcement = local;
            return publish;
        }

        public static async Task<PublishResult> PublishToBotAsync(
            string botApiBaseUrl,
            string guildId,
            string title,
            string body,
            string author)
        {
            try
            {
                var baseUrl = NormalizeBaseUrl(botApiBaseUrl);
                var gid = (guildId ?? "").Trim();
                var cleanTitle = (title ?? "").Trim();
                var cleanBody = (body ?? "").Trim();
                var cleanAuthor = (author ?? "").Trim();

                if (string.IsNullOrWhiteSpace(baseUrl))
                    return Fail("MissingBotApiBaseUrl");

                if (string.IsNullOrWhiteSpace(gid))
                    return Fail("MissingGuildId");

                var text = BuildAnnouncementText(cleanTitle, cleanBody);
                if (string.IsNullOrWhiteSpace(text))
                    return Fail("EmptyText");

                var payload = new
                {
                    GuildId = gid,
                    Author = string.IsNullOrWhiteSpace(cleanAuthor) ? "OverWatch ELD" : cleanAuthor,
                    Text = text,
                    Title = cleanTitle,
                    Body = cleanBody
                };

                var url = $"{baseUrl}/api/vtc/announcements/post";
                var json = JsonSerializer.Serialize(payload, JsonOpts);

                // 1) JSON POST
                using (var jsonResp = await Http.PostAsync(
                    url,
                    new StringContent(json, Encoding.UTF8, "application/json")))
                {
                    var respText = await SafeReadAsync(jsonResp);

                    if (jsonResp.IsSuccessStatusCode)
                    {
                        return new PublishResult
                        {
                            Ok = true,
                            ResponseText = respText
                        };
                    }
                }

                // 2) Form POST fallback (important for older/alternate handlers)
                using (var formResp = await Http.PostAsync(
                    url,
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["guildId"] = gid,
                        ["author"] = string.IsNullOrWhiteSpace(cleanAuthor) ? "OverWatch ELD" : cleanAuthor,
                        ["text"] = text,
                        ["title"] = cleanTitle,
                        ["body"] = cleanBody
                    })))
                {
                    var respText = await SafeReadAsync(formResp);

                    if (formResp.IsSuccessStatusCode)
                    {
                        return new PublishResult
                        {
                            Ok = true,
                            ResponseText = respText
                        };
                    }

                    // 3) GET fallback
                    var qs =
                        $"?guildId={Uri.EscapeDataString(gid)}" +
                        $"&author={Uri.EscapeDataString(string.IsNullOrWhiteSpace(cleanAuthor) ? "OverWatch ELD" : cleanAuthor)}" +
                        $"&text={Uri.EscapeDataString(text)}";

                    using var getResp = await Http.GetAsync(url + qs);
                    var getText = await SafeReadAsync(getResp);

                    if (getResp.IsSuccessStatusCode)
                    {
                        return new PublishResult
                        {
                            Ok = true,
                            ResponseText = getText
                        };
                    }

                    return new PublishResult
                    {
                        Ok = false,
                        Error = $"HTTP {(int)getResp.StatusCode}",
                        ResponseText = getText
                    };
                }
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        private static string BuildAnnouncementText(string title, string body)
        {
            var t = (title ?? "").Trim();
            var b = (body ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(t) && !string.IsNullOrWhiteSpace(b))
                return $"**{t}**\n{b}";

            if (!string.IsNullOrWhiteSpace(t))
                return t;

            return b;
        }

        private static void EnsureStorage()
        {
            if (!Directory.Exists(Folder))
                Directory.CreateDirectory(Folder);
        }

        private static string NormalizeBaseUrl(string url)
        {
            var s = (url ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s))
                return "";

            return s.TrimEnd('/');
        }

        private static async Task<string> SafeReadAsync(HttpResponseMessage resp)
        {
            try
            {
                return await resp.Content.ReadAsStringAsync();
            }
            catch
            {
                return "";
            }
        }

        private static PublishResult Fail(string error) =>
            new()
            {
                Ok = false,
                Error = error ?? "UnknownError"
            };
    }
}