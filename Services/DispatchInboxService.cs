// Services/DispatchInboxService.cs
// ✅ FULL COPY/REPLACE
// Fixes:
// - CS0161: SendDecisionAsync always returns
// - Keeps compatibility with partials: Shared + MessageReceived exist
// - string -> DispatchDecision conversion safely
// Notes:
// - Does NOT change any UI (locked)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OverWatchELD.Models;

namespace OverWatchELD.Services
{
    public sealed partial class DispatchInboxService
    {
        // ✅ singleton used by VtcHttpEndpoints and other services
        public static DispatchInboxService Shared { get; } = new DispatchInboxService();

        private readonly object _gate = new();

        private readonly HttpClient _http;
        private readonly JsonSerializerOptions _json;

        // in-memory cache per driver
        private readonly Dictionary<string, List<DispatchMessage>> _byDriver = new(StringComparer.OrdinalIgnoreCase);

        // Optional: if you have a hub, set it here so decision posts can be forwarded
        public string HubBaseUrl { get; set; } = "";

        public event Action? Changed;

        public DispatchInboxService(HttpClient? http = null)
        {
            _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            _json = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };
        }

        // ✅ compatibility hook for ToastEvent.partial.cs (and any older code)
        private void MessageReceived(DispatchMessage msg)
        {
            AddIncoming(msg);
        }

        // ---------------- Public API ----------------

        public void AddIncoming(DispatchMessage msg)
        {
            if (msg == null) return;

            msg.DriverKey = NormalizeDriverKey(msg.DriverKey);

            lock (_gate)
            {
                var list = GetOrLoadList_NoLock(msg.DriverKey);

                if (!string.IsNullOrWhiteSpace(msg.Id) &&
                    list.Any(x => string.Equals(x.Id, msg.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    // already exists
                }
                else
                {
                    list.Insert(0, msg);
                }

                SaveToDiskFor_NoLock(msg.DriverKey, list);
            }

            Changed?.Invoke();
        }

        public IReadOnlyList<DispatchMessage> GetInbox(string driverKey)
        {
            driverKey = NormalizeDriverKey(driverKey);

            lock (_gate)
            {
                var list = GetOrLoadList_NoLock(driverKey);
                return list.ToList();
            }
        }

        public void MarkRead(string driverKey, string messageId)
        {
            driverKey = NormalizeDriverKey(driverKey);
            if (string.IsNullOrWhiteSpace(messageId)) return;

            lock (_gate)
            {
                var list = GetOrLoadList_NoLock(driverKey);
                var msg = list.FirstOrDefault(m => string.Equals(m.Id, messageId, StringComparison.OrdinalIgnoreCase));
                if (msg == null) return;

                msg.IsRead = true;
                SaveToDiskFor_NoLock(driverKey, list);
            }

            Changed?.Invoke();
        }

        public async Task<bool> SendDecisionAsync(string driverKey, string decision, CancellationToken ct)
        {
            driverKey = NormalizeDriverKey(driverKey);

            try
            {
                if (string.IsNullOrWhiteSpace(driverKey)) return false;

                DispatchMessage? msg;
                lock (_gate)
                {
                    var list = GetOrLoadList_NoLock(driverKey);
                    msg = list.FirstOrDefault(m => !m.IsRead) ?? list.FirstOrDefault();
                    if (msg == null) return false;
                }

                // Optional hub post (doesn't block local save)
                _ = await TryPostDecisionToHubAsync(driverKey, msg.Id ?? "", decision ?? "", ct).ConfigureAwait(false);

                lock (_gate)
                {
                    if (!Enum.TryParse<DispatchDecision>(decision ?? "", ignoreCase: true, out var parsed))
                        parsed = DispatchDecision.None;

                    msg.Decision = parsed;
                    msg.DecisionUtc = DateTimeOffset.UtcNow;
                    msg.IsRead = true;

                    var list = GetOrLoadList_NoLock(driverKey);
                    var idx = list.FindIndex(m => string.Equals(m.Id, msg.Id, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0) list[idx] = msg;

                    SaveToDiskFor_NoLock(driverKey, list);
                }

                Changed?.Invoke();
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ---------------- Internals ----------------

        private async Task<bool> TryPostDecisionToHubAsync(string driverKey, string messageId, string decision, CancellationToken ct)
        {
            try
            {
                var hub = (HubBaseUrl ?? "").Trim();
                if (string.IsNullOrWhiteSpace(hub)) return false;

                var url = hub.TrimEnd('/') + "/api/dispatch/decision";

                var payload = new { driverKey, messageId, decision };
                var json = JsonSerializer.Serialize(payload, _json);

                using var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

                using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private List<DispatchMessage> GetOrLoadList_NoLock(string driverKey)
        {
            driverKey = NormalizeDriverKey(driverKey);

            if (_byDriver.TryGetValue(driverKey, out var list))
                return list;

            list = LoadFromDisk(driverKey);
            _byDriver[driverKey] = list;
            return list;
        }

        private static string NormalizeDriverKey(string? k)
        {
            k = (k ?? "").Trim();
            return string.IsNullOrWhiteSpace(k) ? "default" : k;
        }

        private static string RootDir()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OverWatchELD",
                "dispatch_inbox");

            try { Directory.CreateDirectory(dir); } catch { }
            return dir;
        }

        private static string FilePathFor(string driverKey)
        {
            var safe = new string((driverKey ?? "default")
                .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
                .ToArray());

            if (string.IsNullOrWhiteSpace(safe)) safe = "default";
            return Path.Combine(RootDir(), safe + ".json");
        }

        private List<DispatchMessage> LoadFromDisk(string driverKey)
        {
            try
            {
                var path = FilePathFor(driverKey);
                if (!File.Exists(path)) return new List<DispatchMessage>();

                var json = File.ReadAllText(path);
                var list = JsonSerializer.Deserialize<List<DispatchMessage>>(json, _json);
                if (list == null) return new List<DispatchMessage>();

                foreach (var m in list)
                    m.DriverKey = NormalizeDriverKey(m.DriverKey);

                list.Sort((a, b) => b.SentUtc.CompareTo(a.SentUtc));
                return list;
            }
            catch
            {
                return new List<DispatchMessage>();
            }
        }

        private void SaveToDiskFor_NoLock(string driverKey, List<DispatchMessage> list)
        {
            try
            {
                var path = FilePathFor(driverKey);

                const int max = 200;
                if (list.Count > max)
                    list = list.Take(max).ToList();

                var json = JsonSerializer.Serialize(list, _json);
                File.WriteAllText(path, json);
            }
            catch { }
        }

        // Compatibility method name used in older code paths
        private void SaveToDiskFor(string driverKey)
        {
            lock (_gate)
            {
                driverKey = NormalizeDriverKey(driverKey);
                var list = GetOrLoadList_NoLock(driverKey);
                SaveToDiskFor_NoLock(driverKey, list);
            }
        }


// ---------------- ViewModel helpers ----------------

public IReadOnlyList<DispatchMessage> GetMessagesFor(string userKey)
{
    userKey = NormalizeDriverKey(userKey);
    if (string.IsNullOrWhiteSpace(userKey)) return Array.Empty<DispatchMessage>();

    lock (_gate)
    {
        var list = GetOrLoadList_NoLock(userKey);
        return list.ToList();
    }
}

public void Upsert(string userKey, DispatchMessage msg)
{
    if (msg == null) return;
    userKey = NormalizeDriverKey(userKey);
    if (string.IsNullOrWhiteSpace(userKey)) return;

    lock (_gate)
    {
        var list = GetOrLoadList_NoLock(userKey);

        var id = (msg.Id ?? "").Trim();
        if (string.IsNullOrWhiteSpace(id))
            id = Guid.NewGuid().ToString("N");

        msg.Id = id;
        msg.DriverKey = userKey;

        var idx = list.FindIndex(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) list[idx] = msg;
        else list.Insert(0, msg);

        SaveToDiskFor_NoLock(userKey, list);
    }

    Changed?.Invoke();
}
    }
}
