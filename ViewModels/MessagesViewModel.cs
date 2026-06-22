using OverWatchELD.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace OverWatchELD.ViewModels
{
    public sealed partial class MessagesViewModel : INotifyPropertyChanged
    {
        private readonly HttpClient _http = new HttpClient();

        // ✅ Hub contract can vary between releases. We probe once and cache the working URL patterns.
        private enum DispatchUrlMode { Unknown, DisplayName, User, Name }
        private enum ThreadUrlMode { Unknown, QueryLoadId, PathLoadId, QueryLoadIdAlt }
        private DispatchUrlMode _dispatchMode = DispatchUrlMode.Unknown;
        private ThreadUrlMode _threadMode = ThreadUrlMode.Unknown;
        private CancellationTokenSource? _cts;
        private Task? _loop;

        // ✅ Pending outgoing messages kept until server echoes them back
        private readonly ConcurrentDictionary<long, List<PendingOutgoing>> _pendingByLoadId = new();
        private long _localPendingSeq = 0;

        private sealed class PendingOutgoing
        {
            public long LocalId { get; init; }
            public long LoadId { get; init; }
            public string DisplayName { get; init; } = "User";
            public string Text { get; init; } = "";
            public DateTimeOffset CreatedUtc { get; init; }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private static void UI(Action action)
        {
            try
            {
                var d = Application.Current?.Dispatcher;
                if (d == null || d.CheckAccess()) { action(); return; }
                d.BeginInvoke(action);
            }
            catch { }
        }

        // ---------------- UI Bindings ----------------

        public ObservableCollection<ConversationVm> Conversations { get; } = new();

        private ConversationVm? _selectedConversation;
        public ConversationVm? SelectedConversation
        {
            get => _selectedConversation;
            set { _selectedConversation = value; OnPropertyChanged(); }
        }

        private string _recipientText = "";
        public string RecipientText
        {
            get => _recipientText;
            set { _recipientText = value; OnPropertyChanged(); }
        }

        private string _composerText = "";
        public string ComposerText
        {
            get => _composerText;
            set { _composerText = value; OnPropertyChanged(); }
        }

        private bool _showArchived;
        public bool ShowArchived
        {
            get => _showArchived;
            set { _showArchived = value; OnPropertyChanged(); }
        }

        private bool _discordInboundEnabled = true;
        public bool DiscordInboundEnabled
        {
            get => _discordInboundEnabled;
            set { _discordInboundEnabled = value; OnPropertyChanged(); }
        }

        private string _bridgeStatusText = "Hub: idle";
        public string BridgeStatusText
        {
            get => _bridgeStatusText;
            private set { _bridgeStatusText = value; OnPropertyChanged(); }
        }

        private string _lastPollAt = "";
        public string LastPollAt { get => _lastPollAt; private set { _lastPollAt = value; OnPropertyChanged(); } }

        private string _lastPollUrl = "";
        public string LastPollUrl { get => _lastPollUrl; private set { _lastPollUrl = value; OnPropertyChanged(); } }

        private string _lastPollJson = "";
        public string LastPollJson { get => _lastPollJson; private set { _lastPollJson = value; OnPropertyChanged(); } }

        private string _lastPollError = "";
        public string LastPollError { get => _lastPollError; private set { _lastPollError = value; OnPropertyChanged(); } }

        public ObservableCollection<string> DebugLines { get; } = new();

        // ---------------- Lifecycle ----------------

        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => PollLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            _cts = null;
            _loop = null;
        }

        private async Task PollLoopAsync(CancellationToken token)
        {
            UI(() =>
            {
                if (!Conversations.Any(c => c.LoadId == 0))
                {
                    var dispatch = new ConversationVm(0) { Title = "Dispatch" };
                    Conversations.Insert(0, dispatch);
                    SelectedConversation ??= dispatch;
                }
            });

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await PollOnceAsync(token);
                    UI(() => BridgeStatusText = "Hub: connected");
                }
                catch (Exception ex)
                {
                    UI(() =>
                    {
                        LastPollError = ex.Message;
                        BridgeStatusText = "Hub: error";
                        DebugLines.Insert(0, $"POLL error {DateTime.Now:HH:mm:ss}: {ex.Message}");
                    });
                }

                try { await Task.Delay(2000, token); } catch { }
            }
        }

        private async Task PollOnceAsync(CancellationToken token)
        {
            var hub = SettingsHubUrl();
            if (string.IsNullOrWhiteSpace(hub))
            {
                UI(() => BridgeStatusText = "Hub: set VtcServerUrl");
                return;
            }

            var hubBase = hub.TrimEnd('/');

            // Optional auth for Railway hub endpoints
            var botApiKey = GetBotApiKey();

            // ✅ Logged-in driver name used for privacy filtering + message identity
            var loggedDriverName = GetLoggedInDisplayName();

            // ✅ Snapshot selected load BEFORE we mutate Conversations / SelectedConversation
            var selectedLoadIdSnapshot = SelectedConversation?.LoadId ?? 0;
            // ---------- Conversations ----------
            // Public release: do NOT require /api/conversations to exist (many hubs don't ship it).
            // Keep a local "Dispatch" conversation plus whatever load thread the user selects.
            UI(() =>
            {
                // Ensure Dispatch exists & stays on top
                var dispatch = Conversations.FirstOrDefault(c => c.LoadId == 0);
                if (dispatch == null)
                {
                    dispatch = new ConversationVm(0) { Title = "Dispatch" };
                    Conversations.Insert(0, dispatch);
                }
                else
                {
                    var idx = Conversations.IndexOf(dispatch);
                    if (idx > 0) Conversations.Move(idx, 0);
                }

                // Preserve selection if possible
                var keep = Conversations.FirstOrDefault(c => c.LoadId == selectedLoadIdSnapshot);
                if (keep == null && selectedLoadIdSnapshot != 0)
                {
                    keep = new ConversationVm(selectedLoadIdSnapshot) { Title = $"Load #{selectedLoadIdSnapshot}" };
                    Conversations.Add(keep);
                }

                SelectedConversation ??= dispatch;
                if (keep != null)
                    SelectedConversation = keep;
            });

            var loadIdSnapshot = SelectedConversation?.LoadId ?? selectedLoadIdSnapshot;

            // ---------- 2) Messages ----------
            // Dispatch feed requires driverName; if we don't have it yet, don't hammer the hub (it will 400).
            if (loadIdSnapshot == 0 && string.IsNullOrWhiteSpace(loggedDriverName))
            {
                UI(() =>
                {
                    BridgeStatusText = "Hub: set driver name";
                    LastPollError = "Driver name is blank (cannot poll Dispatch).";
                });
                return;
            }

            // Hub contract varies across public releases. Some hubs use driverName, others use driver or name.
            // Some hubs expose load threads as /api/messages/load?loadId=###, others as /api/messages/load/###.
            // We probe on 400 and cache what works.
            var msgUrls = BuildCandidateMessageUrls(hubBase, loadIdSnapshot, loggedDriverName);

            string msgJson = "";
            string usedUrl = msgUrls.FirstOrDefault() ?? "";
            int lastStatus = 0;
            string lastBody = "";
            string lastErr = "";

            foreach (var candidate in msgUrls)
            {
                usedUrl = candidate;
                UI(() => LastPollUrl = usedUrl);

                var (ok, status, body, err) = await TryGetStringAsync(usedUrl, token, botApiKey);
                lastStatus = status;
                lastBody = body;
                lastErr = err;

                if (ok)
                {
                    msgJson = body;
                    CacheWorkingMessageUrlMode(hubBase, usedUrl, loadIdSnapshot);
                    break;
                }

                // If it's not a 400, don't keep probing (could be offline/timeout/etc.)
                if (status != 400)
                    break;
            }

            if (string.IsNullOrWhiteSpace(msgJson))
            {
                UI(() =>
                {
                    LastPollError = string.IsNullOrWhiteSpace(lastErr)
                        ? $"HTTP {lastStatus} ({(System.Net.HttpStatusCode)lastStatus})."
                        : lastErr;
                    BridgeStatusText = "Hub: disconnected";
                    if (!string.IsNullOrWhiteSpace(lastBody))
                        LastPollJson = lastBody;
                    DebugLines.Insert(0, $"POLL msg HTTP {lastStatus} {DateTime.Now:HH:mm:ss} url={usedUrl}");
                });
                return;
            }

            UI(() =>
            {
                LastPollJson = msgJson;
                LastPollAt = DateTime.Now.ToString("M/d h:mm:ss tt");
            });

            var msgs = ParseMessages(msgJson);

            // Build server message VMs
            var serverVms = new List<MessageVm>(msgs.Length);
            foreach (var m in msgs.OrderBy(x => x.SortKey))
            {
                var from = (m.From ?? "").Trim();

                var isMe = !string.IsNullOrWhiteSpace(from) &&
                           from.Equals(loggedDriverName, StringComparison.OrdinalIgnoreCase);

                var senderName = (m.SenderName ?? "").Trim();
                if (string.IsNullOrWhiteSpace(senderName))
                    senderName = string.IsNullOrWhiteSpace(from) ? "Dispatch" : from;

                serverVms.Add(new MessageVm
                {
                    SenderName = senderName,
                    SenderInitials = InitialsFromName(senderName),
                    MetaText = SafeTime(m.SentUtc),
                    Text = m.Text ?? "",
                    IsFromMe = isMe,

                    AvatarUrl = (m.AvatarUrl ?? "").Trim(),
                    IsDispatcher = m.IsDispatcher
                });
            }

            // ✅ Merge pending outgoing (keeps your sent bubble visible until hub echoes it back)
            var merged = MergePending(loadIdSnapshot, loggedDriverName, serverVms);

            UI(() =>
            {
                if (SelectedConversation == null) return;

                // If server returns nothing, still show pending/local messages
                SelectedConversation.Messages.Clear();
                foreach (var vm in merged)
                    SelectedConversation.Messages.Add(vm);

                // Update snippet/time from the newest item we’re showing
                var lastShown = merged.LastOrDefault();
                if (lastShown != null)
                {
                    SelectedConversation.LastSnippet = lastShown.Text ?? "";
                    SelectedConversation.LastTimeText = lastShown.MetaText ?? "";
                }

                DebugLines.Insert(0, $"POLL ok loadId={loadIdSnapshot} serverMsgs={msgs.Length} shown={merged.Count} {DateTime.Now:HH:mm:ss}");
            });
        }

        private List<MessageVm> MergePending(long loadId, string driverName, List<MessageVm> server)
        {
            // If no pending, quick exit
            if (!_pendingByLoadId.TryGetValue(loadId, out var pending) || pending.Count == 0)
                return server;

            var nowUtc = DateTimeOffset.UtcNow;

            // Match rule: if server contains same text from this driver (case-insensitive), consider delivered
            bool IsDelivered(PendingOutgoing p)
            {
                return server.Any(s =>
                    s.IsFromMe &&
                    string.Equals((s.Text ?? "").Trim(), (p.Text ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
            }

            // Remove delivered or stale pending (> 2 minutes old)
            lock (pending)
            {
                pending.RemoveAll(p =>
                    IsDelivered(p) ||
                    (nowUtc - p.CreatedUtc) > TimeSpan.FromMinutes(2));
            }

            if (pending.Count == 0)
                return server;

            // Append remaining pending to bottom (so it stays visible)
            var result = new List<MessageVm>(server.Count + pending.Count);
            result.AddRange(server);

            var displayName = GetLoggedInDisplayName();

            List<PendingOutgoing> pendingSnapshot;
            lock (pending) pendingSnapshot = pending.ToList();

            foreach (var p in pendingSnapshot.OrderBy(x => x.CreatedUtc))
            {
                result.Add(new MessageVm
                {
                    SenderName = displayName,
                    SenderInitials = InitialsFromName(displayName),
                    MetaText = p.CreatedUtc.ToLocalTime().ToString("h:mm tt"),
                    Text = p.Text,
                    IsFromMe = true
                });
            }

            return result;
        }

        // ---------------- Actions ----------------

        public void ApplyRecipient()
        {
            UI(() =>
            {
                var to = (RecipientText ?? "").Trim();
                if (string.IsNullOrWhiteSpace(to)) return;

                var existing = Conversations.FirstOrDefault(c =>
                    string.Equals(c.Title, to, StringComparison.OrdinalIgnoreCase) ||
                    c.Title.IndexOf(to, StringComparison.OrdinalIgnoreCase) >= 0);

                if (existing != null)
                {
                    SelectedConversation = existing;
                    return;
                }

                if (long.TryParse(to, out var asLoadId))
                {
                    var c = Conversations.FirstOrDefault(x => x.LoadId == asLoadId);
                    if (c != null) { SelectedConversation = c; return; }

                    var created = new ConversationVm(asLoadId) { Title = $"Load #{asLoadId}" };
                    Conversations.Add(created);
                    SelectedConversation = created;
                    return;
                }

                SelectedConversation = Conversations.FirstOrDefault(x => x.LoadId == 0);
            });
        }

        public async Task SendFromComposerAsync()
        {
            var text = (ComposerText ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text)) return;

            var hub = SettingsHubUrl();
            if (string.IsNullOrWhiteSpace(hub))
            {
                MessageBox.Show("Bot API URL is not set. Open VTC setup/settings and make sure BotApiBaseUrl is set.",
                    "OverWatch ELD", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var loadId = SelectedConversation?.LoadId ?? 0;
            var displayName = GetLoggedInDisplayName();

            // Add to pending FIRST so the bubble stays visible even while Railway/Discord catches up.
            var pending = _pendingByLoadId.GetOrAdd(loadId, _ => new List<PendingOutgoing>());
            var localId = Interlocked.Increment(ref _localPendingSeq);
            var createdUtc = DateTimeOffset.UtcNow;

            lock (pending)
            {
                pending.Add(new PendingOutgoing
                {
                    LocalId = localId,
                    LoadId = loadId,
                    DisplayName = displayName,
                    Text = text,
                    CreatedUtc = createdUtc
                });
            }

            UI(() =>
            {
                SelectedConversation?.Messages.Add(new MessageVm
                {
                    SenderName = displayName,
                    SenderInitials = InitialsFromName(displayName),
                    MetaText = createdUtc.ToLocalTime().ToString("h:mm tt"),
                    Text = text,
                    IsFromMe = true
                });

                ComposerText = "";
            });

            try
            {
                var (ok, status, body, usedUrl) = await TrySendDispatchMessageAsync(hub, loadId, displayName, text, CancellationToken.None);

                UI(() =>
                {
                    LastPollUrl = usedUrl;
                    LastPollJson = body ?? "";
                });

                if (!ok)
                {
                    UI(() =>
                    {
                        LastPollError = $"Send failed HTTP {status}";
                        DebugLines.Insert(0, $"SEND failed HTTP {status} loadId={loadId} {DateTime.Now:HH:mm:ss} url={usedUrl}");
                    });

                    MessageBox.Show($"Send failed ({status}).\n{body}",
                        "OverWatch ELD", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                UI(() => DebugLines.Insert(0, $"SEND ok loadId={loadId} {DateTime.Now:HH:mm:ss} url={usedUrl}"));
            }
            catch (Exception ex)
            {
                UI(() =>
                {
                    LastPollError = $"Send error: {ex.Message}";
                    DebugLines.Insert(0, $"SEND error loadId={loadId} {DateTime.Now:HH:mm:ss}: {ex.Message}");
                });
            }
        }

        private async Task<(bool ok, int status, string body, string usedUrl)> TrySendDispatchMessageAsync(
            string hub,
            long loadId,
            string displayName,
            string text,
            CancellationToken token)
        {
            var hubBase = (hub ?? "").Trim().TrimEnd('/');
            var guildId = GetConfiguredGuildId();
            var discordUserId = GetLinkedDiscordUserId();
            var discordUsername = GetLinkedDiscordUsername();
            var botApiKey = GetBotApiKey();

            // Send every common field name your Railway bot versions have accepted.
            // This prevents BadJson errors like "Expected Text, Body, Message, or Content".
            var payload = new
            {
                GuildId = guildId,
                Text = text,
                UserId = discordUserId,
                DiscordUserId = discordUserId,
                DriverDiscordUserId = discordUserId,
                UserName = displayName,
                DriverName = displayName,
                DisplayName = displayName,
                DiscordUsername = discordUsername,
                Route = "dispatch",
                Direction = "from_driver",
                Source = "eld",
                LoadId = loadId > 0 ? loadId.ToString(CultureInfo.InvariantCulture) : "",
                LoadNumber = loadId > 0 ? loadId.ToString(CultureInfo.InvariantCulture) : ""
            };

            var json = JsonSerializer.Serialize(payload);

            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(guildId))
            {
                candidates.Add($"{hubBase}/api/messages/send?guildId={Uri.EscapeDataString(guildId)}&route=dispatch&direction=from_driver");
                candidates.Add($"{hubBase}/api/dispatch/messages/send?guildId={Uri.EscapeDataString(guildId)}");
            }
            candidates.Add($"{hubBase}/api/messages/send");

            string lastBody = "";
            int lastStatus = 0;
            string lastUrl = candidates.FirstOrDefault() ?? "";

            foreach (var url in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                lastUrl = url;
                try
                {
                    using var req = OverWatchELD.Services.VtcHubClient.Create(HttpMethod.Post, url);
                    req.Content = new StringContent(json, Encoding.UTF8, "application/json");

                    if (!string.IsNullOrWhiteSpace(botApiKey))
                        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", botApiKey);

                    using var resp = await _http.SendAsync(req, token).ConfigureAwait(false);
                    lastStatus = (int)resp.StatusCode;
                    lastBody = await resp.Content.ReadAsStringAsync(token).ConfigureAwait(false);

                    if (resp.IsSuccessStatusCode)
                        return (true, lastStatus, lastBody, url);

                    // Try fallbacks for the common broken-route responses.
                    if (lastStatus == 400 || lastStatus == 401 || lastStatus == 404 || lastStatus == 405)
                        continue;

                    break;
                }
                catch (Exception ex)
                {
                    lastStatus = 0;
                    lastBody = ex.Message;
                }
            }

            return (false, lastStatus, lastBody, lastUrl);
        }

        private static string GetLoggedInDisplayName()
        {
            try
            {
                // ✅ Prefer linked Discord identity if present
                try
                {
                    var ident = OverWatchELD.Services.DiscordIdentityStore.Load();
                    var dname = (ident.DiscordUsername ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(dname) && !dname.Equals("User", StringComparison.OrdinalIgnoreCase))
                        return dname;
                }
                catch { }

                if (Application.Current is OverWatchELD.App app)
                {
                    var dn = (app.Session?.DriverName ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(dn)
                        && !dn.Equals("User", StringComparison.OrdinalIgnoreCase)
                        && !dn.Equals("Driver", StringComparison.OrdinalIgnoreCase))
                        return dn;
                }

                try
                {
                    var dn2 = (UserSession.Instance.DisplayName ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(dn2) && !dn2.Equals("User", StringComparison.OrdinalIgnoreCase))
                        return dn2;
                }
                catch { }

                var uname = (EldDriverIdentityResolver.DriverName() ?? "").Trim();
                return string.IsNullOrWhiteSpace(uname) ? "User" : uname;
            }
            catch
            {
                return "User";
            }
        }

        // ---------------- Robust JSON parsing ----------------

        private sealed class ConversationParsed
        {
            public long LoadId;
            public string? Title;
            public string? Status;
            public string? LastUtc;
        }

        private sealed class MessageParsed
        {
            public long SortKey;
            public string? From;
            public string? SenderName;
            public string? AvatarUrl;
            public bool IsDispatcher;
            public string? Text;
            public string? SentUtc;
        }

        private static ConversationParsed[] ParseConversations(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<ConversationParsed>();

                return doc.RootElement.EnumerateArray()
                    .Select(el => new ConversationParsed
                    {
                        LoadId = GetLong(el, "loadId", "LoadId"),
                        Title = GetString(el, "title", "Title"),
                        Status = GetString(el, "status", "Status"),
                        LastUtc = GetString(el, "lastUtc", "LastUtc")
                    })
                    .Where(x => x.LoadId >= 0)
                    .ToArray();
            }
            catch { return Array.Empty<ConversationParsed>(); }
        }

        private static MessageParsed[] ParseMessages(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);

                JsonElement arr;
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    arr = doc.RootElement;
                }
                else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    // Railway/bot versions have returned { items: [] }, { messages: [] }, or { data: [] }.
                    if (doc.RootElement.TryGetProperty("items", out arr) && arr.ValueKind == JsonValueKind.Array) { }
                    else if (doc.RootElement.TryGetProperty("messages", out arr) && arr.ValueKind == JsonValueKind.Array) { }
                    else if (doc.RootElement.TryGetProperty("data", out arr) && arr.ValueKind == JsonValueKind.Array) { }
                    else return Array.Empty<MessageParsed>();
                }
                else
                {
                    return Array.Empty<MessageParsed>();
                }

                long idx = 0;
                return arr.EnumerateArray()
                    .Select(el =>
                    {
                        idx++;
                        var text = GetString(el,
                            "text", "Text",
                            "body", "Body",
                            "message", "Message",
                            "content", "Content");

                        var sender = GetString(el,
                            "from", "From",
                            "driverName", "DriverName",
                            "displayName", "DisplayName",
                            "senderName", "SenderName",
                            "discordUsername", "DiscordUsername");

                        var direction = GetString(el, "direction", "Direction", "source", "Source") ?? "";
                        var isDispatcher = GetBool(el, "isDispatcher", "IsDispatcher") ||
                                           direction.Equals("dispatch", StringComparison.OrdinalIgnoreCase) ||
                                           direction.Equals("from_dispatch", StringComparison.OrdinalIgnoreCase) ||
                                           direction.Equals("dispatcher", StringComparison.OrdinalIgnoreCase);

                        return new MessageParsed
                        {
                            SortKey = GetLong(el, "sortKey", "SortKey", "createdMs", "CreatedMs", "id", "Id") == 0 ? idx : GetLong(el, "sortKey", "SortKey", "createdMs", "CreatedMs", "id", "Id"),
                            From = sender,
                            SenderName = sender,
                            AvatarUrl = GetString(el, "avatarUrl", "AvatarUrl", "discordAvatarUrl", "DiscordAvatarUrl"),
                            IsDispatcher = isDispatcher,
                            Text = text,
                            SentUtc = GetString(el, "sentUtc", "SentUtc", "createdUtc", "CreatedUtc", "dateUtc", "DateUtc", "timestamp", "Timestamp")
                        };
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                    .ToArray();
            }
            catch { return Array.Empty<MessageParsed>(); }
        }

        private static string? GetString(JsonElement el, params string[] names)
        {
            foreach (var n in names)
            {
                if (el.TryGetProperty(n, out var p))
                {
                    if (p.ValueKind == JsonValueKind.String) return p.GetString();
                    if (p.ValueKind == JsonValueKind.Number) return p.ToString();
                    if (p.ValueKind == JsonValueKind.True) return "true";
                    if (p.ValueKind == JsonValueKind.False) return "false";
                }
            }
            return null;
        }

        private static bool GetBool(JsonElement el, params string[] names)
        {
            foreach (var n in names)
            {
                if (el.TryGetProperty(n, out var p))
                {
                    if (p.ValueKind == JsonValueKind.True) return true;
                    if (p.ValueKind == JsonValueKind.False) return false;
                    if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var i)) return i != 0;
                    if (p.ValueKind == JsonValueKind.String && bool.TryParse(p.GetString(), out var b)) return b;
                }
            }
            return false;
        }

        private static long GetLong(JsonElement el, params string[] names)
        {
            foreach (var n in names)
            {
                if (el.TryGetProperty(n, out var p))
                {
                    if (p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var v)) return v;
                    if (p.ValueKind == JsonValueKind.String && long.TryParse(p.GetString(), out var s)) return s;
                }
            }
            return 0;
        }

        // ---------------- Helpers ----------------

        private static string SafeTime(string? isoUtc)
        {
            if (string.IsNullOrWhiteSpace(isoUtc)) return "";
            if (DateTimeOffset.TryParse(isoUtc, out var dto))
                return dto.ToLocalTime().ToString("h:mm tt");
            return isoUtc!;
        }

        private string SettingsHubUrl()
        {
            // ✅ Railway-only: hub URL comes from vtc.config.json (BotApiBaseUrl)
            try
            {
                var cfg = OverWatchELD.Services.VtcConfigService.Get();
                var hub = (cfg?.BotApiBaseUrl ?? "").Trim();
                return hub;
            }
            catch { }

            // Fallback (legacy): LocalAppData settings.json
            try
            {
                var pathA = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OverWatchELD", "settings.json");

                var pathB = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ATS_ELD", "settings.json");

                string? json = null;
                if (System.IO.File.Exists(pathA)) json = System.IO.File.ReadAllText(pathA);
                else if (System.IO.File.Exists(pathB)) json = System.IO.File.ReadAllText(pathB);

                if (string.IsNullOrWhiteSpace(json))
                    return "";

                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("VtcServerUrl", out var p))
                    return (p.GetString() ?? "").Trim();

                return "";
            }
            catch { return ""; }
        }

        private static string GetBotApiKey()
        {
            try
            {
                var cfg = OverWatchELD.Services.VtcConfigService.Get();
                // SaaS-ready: prefer per-install DeviceToken, fallback to legacy BotApiKey.
                var tok = (cfg?.DeviceToken ?? "").Trim();
                if (string.IsNullOrWhiteSpace(tok)) tok = (cfg?.BotApiKey ?? "").Trim();
                return tok;
            }
            catch { return ""; }
        }

        private IEnumerable<string> BuildCandidateMessageUrls(string hubBase, long loadId, string driverName)
        {
            hubBase = (hubBase ?? "").TrimEnd('/');
            var guildId = GetConfiguredGuildId();
            var discordUserId = GetLinkedDiscordUserId();
            var dn = Uri.EscapeDataString((driverName ?? "").Trim());
            var gid = Uri.EscapeDataString(guildId ?? "");
            var did = Uri.EscapeDataString(discordUserId ?? "");

            // Load thread polling. Keep legacy route support, but prefer the current hub route first.
            if (loadId > 0)
            {
                if (!string.IsNullOrWhiteSpace(guildId))
                {
                    yield return $"{hubBase}/api/hub/messages?guildId={gid}&loadId={loadId}";
                    yield return $"{hubBase}/api/messages?guildId={gid}&loadId={loadId}";
                }

                yield return $"{hubBase}/api/hub/messages?loadId={loadId}";
                yield return $"{hubBase}/api/messages?loadId={loadId}";
                yield return $"{hubBase}/api/messages/load?loadId={loadId}";
                yield return $"{hubBase}/api/messages/load/{loadId}";
                yield break;
            }

            // Dispatch polling. Current working route is /api/hub/messages and it identifies the linked driver.
            if (!string.IsNullOrWhiteSpace(guildId) && !string.IsNullOrWhiteSpace(discordUserId))
                yield return $"{hubBase}/api/hub/messages?guildId={gid}&driverDiscordUserId={did}";

            if (!string.IsNullOrWhiteSpace(guildId))
            {
                yield return $"{hubBase}/api/hub/messages?guildId={gid}&driverName={dn}";
                yield return $"{hubBase}/api/messages?guildId={gid}&driverName={dn}";
                yield return $"{hubBase}/api/messages?guildId={gid}&driver={dn}";
                yield return $"{hubBase}/api/messages?guildId={gid}&name={dn}";
            }

            // Legacy fallbacks.
            yield return $"{hubBase}/api/hub/messages?driverName={dn}";
            yield return $"{hubBase}/api/messages?driverName={dn}";
            yield return $"{hubBase}/api/messages?driver={dn}";
            yield return $"{hubBase}/api/messages?name={dn}";
        }

        private static string GetConfiguredGuildId()
        {
            try
            {
                var cfg = OverWatchELD.Services.VtcConfigService.Get();
                var gid = (cfg?.Discord?.GuildId ?? "").Trim();
                if (string.IsNullOrWhiteSpace(gid)) gid = (cfg?.GuildId ?? "").Trim();
                return gid;
            }
            catch { }

            try
            {
                var ident = OverWatchELD.Services.DiscordIdentityStore.Load();
                return (ident.GuildId ?? "").Trim();
            }
            catch { return ""; }
        }

        private static string GetLinkedDiscordUserId()
        {
            try
            {
                var ident = OverWatchELD.Services.DiscordIdentityStore.Load();
                return (ident.DiscordUserId ?? "").Trim();
            }
            catch { return ""; }
        }

        private static string GetLinkedDiscordUsername()
        {
            try
            {
                var ident = OverWatchELD.Services.DiscordIdentityStore.Load();
                return (ident.DiscordUsername ?? "").Trim();
            }
            catch { return ""; }
        }

        private void CacheWorkingMessageUrlMode(string hubBase, string usedUrl, long loadId)
        {
            try
            {
                // Cache dispatch query param
                if (loadId == 0)
                {
                    if (usedUrl.Contains("?driver=", StringComparison.OrdinalIgnoreCase)) _dispatchMode = DispatchUrlMode.User;
                    else if (usedUrl.Contains("?name=", StringComparison.OrdinalIgnoreCase)) _dispatchMode = DispatchUrlMode.Name;
                    else _dispatchMode = DispatchUrlMode.DisplayName;
                }
                else
                {
                    // Cache thread style
                    if (usedUrl.IndexOf("/api/messages/load/", StringComparison.OrdinalIgnoreCase) >= 0) _threadMode = ThreadUrlMode.PathLoadId;
                    else if (usedUrl.Contains("/api/messages?", StringComparison.OrdinalIgnoreCase) && usedUrl.Contains("loadId=", StringComparison.OrdinalIgnoreCase)) _threadMode = ThreadUrlMode.QueryLoadIdAlt;
                    else _threadMode = ThreadUrlMode.QueryLoadId;
                }
            }
            catch { }
        }

        private async Task<(bool ok, int status, string body, string err)> TryGetStringAsync(string url, CancellationToken token, string? bearer)
        {
            try
            {
                using var req = OverWatchELD.Services.VtcHubClient.Create(HttpMethod.Get, url);
                // Legacy override (kept for safety). If caller passes a bearer explicitly, honor it.
                if (!string.IsNullOrWhiteSpace(bearer))
                    req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);

                using var resp = await _http.SendAsync(req, token);
                var body = await resp.Content.ReadAsStringAsync(token);
                if (resp.IsSuccessStatusCode)
                    return (true, (int)resp.StatusCode, body ?? "", "");

                return (false, (int)resp.StatusCode, body ?? "", "");
            }
            catch (Exception ex)
            {
                return (false, 0, "", ex.Message);
            }
        }

        private static string InitialsFromName(string name)
        {
            var t = (name ?? "").Trim();
            if (t.Length == 0) return "?";
            var parts = t.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) return parts[0].Substring(0, 1).ToUpperInvariant();
            return (parts[0].Substring(0, 1) + parts[1].Substring(0, 1)).ToUpperInvariant();
        }

        // ---------------- Nested VMs ----------------

        public sealed class ConversationVm : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string? name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

            public long LoadId { get; }
            public ConversationVm(long loadId) { LoadId = loadId; }

            private string _title = "Messages";
            public string Title { get => _title; set { _title = value; OnPropertyChanged(); OnPropertyChanged(nameof(Initials)); } }

            private string _lastSnippet = "";
            public string LastSnippet { get => _lastSnippet; set { _lastSnippet = value; OnPropertyChanged(); } }

            private string _lastTimeText = "";
            public string LastTimeText { get => _lastTimeText; set { _lastTimeText = value; OnPropertyChanged(); } }

            public string Initials => InitialsFromName(Title);

            public ObservableCollection<MessageVm> Messages { get; } = new();
        }

        public sealed class MessageVm
        {
            public string SenderName { get; set; } = "Dispatch";
            public string SenderInitials { get; set; } = "DP";
            public string MetaText { get; set; } = "";
            public string Text { get; set; } = "";
            public bool IsFromMe { get; set; }

            public string? AvatarUrl { get; set; } = null;
            public bool IsDispatcher { get; set; }

            public bool ShowAvatar { get; set; } = true;
            public bool ShowHeader { get; set; } = true;

            public HorizontalAlignment BubbleAlign
                => IsFromMe ? HorizontalAlignment.Right : HorizontalAlignment.Left;

            public System.Windows.Media.Brush BubbleBackground
                => IsFromMe
                    ? (System.Windows.Media.Brush)new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#DBEAFE"))
                    : (System.Windows.Media.Brush)new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFFFFF"));

            public bool HasImage => false;
            public string? ImagePath => null;
        }
    }

    internal static class HttpClientExt
    {
        public static Task<string> GetStringAsync(this HttpClient http, string url, CancellationToken token)
            => GetStringAsync(http, url, token, bearerToken: "");

        public static async Task<string> GetStringAsync(this HttpClient http, string url, CancellationToken token, string? bearerToken)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(bearerToken))
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
            using var resp = await http.SendAsync(req, token);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync(token);
        }
    }
}