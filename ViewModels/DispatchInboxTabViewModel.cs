using OverWatchELD.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace OverWatchELD.ViewModels
{
    public partial class DispatchInboxTabViewModel : INotifyPropertyChanged
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

        public ICommand? ReplyCommand { get; set; }

        public ObservableCollection<ThreadRow> Threads { get; } = new();
        public ObservableCollection<ThreadRow> FilteredThreads { get; } = new();
        public ObservableCollection<MessageRow> SelectedMessages { get; } = new();

        private readonly HashSet<string> _locallyDeletedThreadKeys = new(StringComparer.OrdinalIgnoreCase);

        private string _searchText = "";
        private ThreadRow? _selectedThread;
        private string _headerSubtitle = "Dispatch messages and replies sync with the VTC hub.";
        private string _unreadSummary = "Unread: 0";

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText == value) return;
                _searchText = value;
                OnPropertyChanged();
                ApplyThreadFilter();
            }
        }

        public ThreadRow? SelectedThread
        {
            get => _selectedThread;
            set
            {
                if (_selectedThread == value) return;

                _selectedThread = value;

                if (_selectedThread != null && _selectedThread.ThreadKey != "empty")
                {
                    _selectedThread.UnreadCount = 0;
                    _selectedThread.StatusBrush = Brushes.Gray;

                    foreach (var msg in _selectedThread.SourceMessages)
                        msg.IsRead = true;
                }

                UpdateUnreadSummary();

                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedThreadTitle));
                OnPropertyChanged(nameof(SelectedThreadSubtitle));
                OnPropertyChanged(nameof(ComposeHint));
                OnPropertyChanged(nameof(CanReplyToSelectedThread));
            }
        }

        public string HeaderSubtitle
        {
            get => _headerSubtitle;
            set
            {
                if (_headerSubtitle == value) return;
                _headerSubtitle = value;
                OnPropertyChanged();
            }
        }

        public string UnreadSummary
        {
            get => _unreadSummary;
            set
            {
                if (_unreadSummary == value) return;
                _unreadSummary = value;
                OnPropertyChanged();
            }
        }

        public string SelectedThreadTitle =>
            SelectedThread?.DisplayName ?? "No conversation selected";

        public string SelectedThreadSubtitle =>
            SelectedThread == null
                ? "Pick a conversation from the left."
                : SelectedThread.RoleLine;

        public string ComposeHint
        {
            get
            {
                if (SelectedThread == null || SelectedThread.ThreadKey == "empty")
                    return "Start a new message to Dispatch or select an existing conversation.";

                return $"Replying to {SelectedThread.DisplayName}";
            }
        }

        public bool CanStartNewConversation => true;
        public Visibility CanStartNewConversationVisibility => Visibility.Visible;

        public bool CanReplyToSelectedThread =>
            SelectedThread != null &&
            SelectedThread.ThreadKey != "empty";

        public Visibility DispatchOnlyVisibility => Visibility.Collapsed;

        public bool CurrentUserIsDispatchLike => false;

        private string CurrentGuildId
        {
            get
            {
                try { return (VtcConfigService.Load(forceReload: true).Discord?.GuildId ?? "").Trim(); }
                catch { return ""; }
            }
        }

        private string CurrentBaseUrl
        {
            get
            {
                try { return (VtcConfigService.Load(forceReload: true).BotApiBaseUrl ?? "").Trim().TrimEnd('/'); }
                catch { return ""; }
            }
        }

        private string CurrentDiscordUserId
        {
            get
            {
                try { return (DiscordIdentityStore.Load()?.DiscordUserId ?? "").Trim(); }
                catch { return ""; }
            }
        }

        private string CurrentDiscordUsername
        {
            get
            {
                try { return (DiscordIdentityStore.Load()?.DiscordUsername ?? "").Trim(); }
                catch { return ""; }
            }
        }

        public Task InitializeAsync()
        {
            HeaderSubtitle = "Dispatch messages and replies sync with the VTC hub.";
            return Task.CompletedTask;
        }

        public async Task RefreshAsync(bool keepSelection = false)
        {
            string? keepKey = keepSelection ? SelectedThread?.ThreadKey : null;

            try
            {
                var baseUrl = CurrentBaseUrl;
                var guildId = CurrentGuildId;
                var myDiscordUserId = CurrentDiscordUserId;

                if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(guildId))
                {
                    if (!Threads.Any())
                        AddEmptyThread("Pair or select a VTC first.");
                    return;
                }

                var url = $"{baseUrl}/api/messages?guildId={Uri.EscapeDataString(guildId)}";
                var raw = (await Http.GetStringAsync(url))?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(raw))
                    return;

                var allMessages = NormalizeAllMessages(raw);

                if (allMessages.Count == 0)
                    return;

                var convoRows = BuildConversationRows(allMessages, myDiscordUserId)
                    .Where(x => !_locallyDeletedThreadKeys.Contains(x.ThreadKey))
                    .GroupBy(x => x.ThreadKey, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderByDescending(x => x.LastMessageUtc).First())
                    .OrderByDescending(x => x.LastMessageUtc)
                    .ToList();

                Threads.Clear();

                foreach (var row in convoRows)
                {
                    row.SourceMessages = row.SourceMessages
                        .GroupBy(x => x.StableKey, StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.OrderByDescending(x => x.TimestampUtc).First())
                        .OrderBy(x => x.TimestampUtc)
                        .ToList();

                    Threads.Add(row);
                }

                ApplyThreadFilter();

                if (!string.IsNullOrWhiteSpace(keepKey))
                {
                    SelectedThread =
                        Threads.FirstOrDefault(x => string.Equals(x.ThreadKey, keepKey, StringComparison.OrdinalIgnoreCase))
                        ?? Threads.FirstOrDefault();
                }
                else if (SelectedThread == null || _locallyDeletedThreadKeys.Contains(SelectedThread.ThreadKey))
                {
                    SelectedThread = Threads.FirstOrDefault();
                }

                UpdateUnreadSummary();
                await OpenSelectedThreadAsync();
            }
            catch (Exception ex)
            {
                if (!Threads.Any())
                    AddEmptyThread("Messages unavailable: " + ex.Message);
            }
        }

        public void ApplyThreadFilter()
        {
            var q = (SearchText ?? "").Trim();

            var rows = string.IsNullOrWhiteSpace(q)
                ? Threads.ToList()
                : Threads.Where(x =>
                        Contains(x.DisplayName, q) ||
                        Contains(x.DiscordName, q) ||
                        Contains(x.Role, q) ||
                        Contains(x.LastMessagePreview, q))
                    .ToList();

            FilteredThreads.Clear();

            foreach (var row in rows)
                FilteredThreads.Add(row);
        }

        public async Task OpenSelectedThreadAsync()
        {
            await Task.CompletedTask;

            SelectedMessages.Clear();

            var selected = SelectedThread;

            if (selected == null || string.Equals(selected.ThreadKey, "empty", StringComparison.OrdinalIgnoreCase))
            {
                NotifySelectedComputed();
                return;
            }

            selected.SourceMessages = selected.SourceMessages
                .GroupBy(x => x.StableKey, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.TimestampUtc).First())
                .OrderBy(x => x.TimestampUtc)
                .ToList();

            foreach (var item in selected.SourceMessages)
            {
                item.IsRead = true;

                SelectedMessages.Add(new MessageRow
                {
                    MessageId = item.MessageId ?? "",
                    ThreadKey = selected.ThreadKey ?? "",
                    SenderName = item.IsMine ? "You" : (item.FromName ?? "Unknown"),
                    Body = item.Body ?? "",
                    TimestampUtc = item.TimestampUtc,
                    IsMine = item.IsMine,
                    IsSystem = item.IsSystem,
                    IsRead = true
                });
            }

            selected.UnreadCount = 0;
            selected.StatusBrush = Brushes.Gray;
            selected.NotifyComputed();

            UpdateUnreadSummary();
            NotifySelectedComputed();
        }

        public async Task<bool> SendAsync(string body)
        {
            try
            {
                body = (body ?? "").Trim();

                if (string.IsNullOrWhiteSpace(body))
                    return false;

                var baseUrl = CurrentBaseUrl;
                var guildId = CurrentGuildId;
                var myDiscordUserId = (CurrentDiscordUserId ?? "").Trim();
                var myDiscordUsername = (CurrentDiscordUsername ?? "").Trim();

                if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(guildId))
                {
                    MessageBox.Show("Bot API or GuildId is missing.", "Messaging Hub", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                if (string.IsNullOrWhiteSpace(myDiscordUserId))
                {
                    MessageBox.Show("Your Discord account is not paired yet.", "Messaging Hub", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                baseUrl = baseUrl.Trim().TrimEnd('/');
                var displayName = string.IsNullOrWhiteSpace(myDiscordUsername) ? "Driver" : myDiscordUsername;

                var url =
                    $"{baseUrl}/api/messages/send" +
                    $"?guildId={Uri.EscapeDataString(guildId)}" +
                    $"&route=dispatch" +
                    $"&direction=from_driver" +
                    $"&text={Uri.EscapeDataString(body)}" +
                    $"&driverName={Uri.EscapeDataString(displayName)}" +
                    $"&discordUserId={Uri.EscapeDataString(myDiscordUserId)}" +
                    $"&userId={Uri.EscapeDataString(myDiscordUserId)}";

                var payload = new
                {
                    GuildId = guildId,
                    Text = body,
                    DriverDiscordUserId = myDiscordUserId,
                    DiscordUserId = myDiscordUserId,
                    UserId = myDiscordUserId,
                    DriverName = displayName,
                    DisplayName = displayName,
                    DiscordUsername = myDiscordUsername,
                    Route = "dispatch",
                    Direction = "from_driver",
                    Source = "eld"
                };

                var json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                var resp = await Http.PostAsync(url, content);
                var respBody = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                    MessageBox.Show(
                        $"Send failed.\n\nStatus: {(int)resp.StatusCode}\n\nResponse:\n{respBody}",
                        "Messaging Hub",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return false;
                }

                await RefreshAsync(keepSelection: true);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to send message: " + ex.Message, "Messaging Hub", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public async Task<bool> SendAsync(string body, string attachmentPath, string attachmentFileName)
        {
            if (!string.IsNullOrWhiteSpace(attachmentFileName))
            {
                body = string.IsNullOrWhiteSpace(body)
                    ? $"Attachment: {attachmentFileName}"
                    : $"{body}\n\nAttachment: {attachmentFileName}";
            }

            return await SendAsync(body);
        }

        public async Task StartNewConversationAsync(string driverName, string discordUserId, string firstMessage)
        {
            firstMessage = (firstMessage ?? "").Trim();

            if (string.IsNullOrWhiteSpace(firstMessage))
                return;

            var myDiscordUserId = CurrentDiscordUserId;
            var myDiscordUsername = CurrentDiscordUsername;

            if (string.IsNullOrWhiteSpace(myDiscordUserId))
            {
                MessageBox.Show("Your Discord account is not paired yet.", "Messaging Hub", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dispatchThreadKey = myDiscordUserId;

            _locallyDeletedThreadKeys.Remove(dispatchThreadKey);

            SelectedThread = Threads.FirstOrDefault(x =>
                string.Equals(x.ThreadKey, dispatchThreadKey, StringComparison.OrdinalIgnoreCase));

            if (SelectedThread == null)
            {
                SelectedThread = new ThreadRow
                {
                    ThreadKey = dispatchThreadKey,
                    DisplayName = "Dispatch",
                    DiscordName = string.IsNullOrWhiteSpace(myDiscordUsername) ? "" : myDiscordUsername,
                    DiscordUserId = myDiscordUserId,
                    Role = "Driver",
                    LastMessagePreview = "",
                    LastMessageUtc = DateTimeOffset.UtcNow,
                    UnreadCount = 0,
                    SourceMessages = new List<NormalizedMessage>()
                };

                Threads.Insert(0, SelectedThread);
                ApplyThreadFilter();
            }

            await SendAsync(firstMessage);
        }

        public async Task StartNewConversationAsync(
            string driverName,
            string discordUserId,
            string firstMessage,
            string attachmentPath,
            string attachmentFileName)
        {
            if (!string.IsNullOrWhiteSpace(attachmentFileName))
            {
                firstMessage =
                    string.IsNullOrWhiteSpace(firstMessage)
                        ? $"Attachment: {attachmentFileName}"
                        : $"{firstMessage}\n\nAttachment: {attachmentFileName}";
            }

            await StartNewConversationAsync(driverName, discordUserId, firstMessage);
        }

        public async Task MarkSelectedThreadReadAsync(bool autoOnly = false)
        {
            await Task.CompletedTask;

            if (SelectedThread == null || SelectedThread.ThreadKey == "empty")
                return;

            SelectedThread.UnreadCount = 0;
            SelectedThread.StatusBrush = Brushes.Gray;

            foreach (var msg in SelectedThread.SourceMessages)
                msg.IsRead = true;

            foreach (var row in SelectedMessages)
                row.IsRead = true;

            SelectedThread.NotifyComputed();
            UpdateUnreadSummary();
        }

        public async Task ClearSelectedThreadAsync()
        {
            await Task.CompletedTask;

            if (SelectedThread == null || SelectedThread.ThreadKey == "empty")
                return;

            var confirm = MessageBox.Show(
                $"Delete conversation with {SelectedThread.DisplayName}?",
                "Delete Conversation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            var key = SelectedThread.ThreadKey;
            _locallyDeletedThreadKeys.Add(key);

            var existing = Threads.FirstOrDefault(x =>
                string.Equals(x.ThreadKey, key, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
                Threads.Remove(existing);

            var filtered = FilteredThreads.FirstOrDefault(x =>
                string.Equals(x.ThreadKey, key, StringComparison.OrdinalIgnoreCase));

            if (filtered != null)
                FilteredThreads.Remove(filtered);

            SelectedMessages.Clear();
            SelectedThread = Threads.FirstOrDefault();

            UpdateUnreadSummary();

            if (SelectedThread != null)
                await OpenSelectedThreadAsync();
        }

        public List<OverWatchELD.Views.NewDispatchMessageWindow.DriverPickItem> GetDriverSelections()
        {
            var list = new List<OverWatchELD.Views.NewDispatchMessageWindow.DriverPickItem>();

            try
            {
                var myDiscordUserId = (CurrentDiscordUserId ?? "").Trim();
                var myDiscordUsername = (CurrentDiscordUsername ?? "").Trim();

                if (!string.IsNullOrWhiteSpace(myDiscordUserId) || !string.IsNullOrWhiteSpace(myDiscordUsername))
                {
                    list.Add(new OverWatchELD.Views.NewDispatchMessageWindow.DriverPickItem
                    {
                        DriverName = string.IsNullOrWhiteSpace(myDiscordUsername) ? "Dispatch" : myDiscordUsername,
                        DiscordUserId = myDiscordUserId,
                        Role = "Driver"
                    });
                }

                foreach (var t in Threads)
                {
                    if (t == null || string.IsNullOrWhiteSpace(t.DisplayName) || t.ThreadKey == "empty")
                        continue;

                    list.Add(new OverWatchELD.Views.NewDispatchMessageWindow.DriverPickItem
                    {
                        DriverName = t.DisplayName,
                        DiscordUserId = t.DiscordUserId ?? "",
                        Role = string.IsNullOrWhiteSpace(t.Role) ? "Driver" : t.Role
                    });
                }
            }
            catch
            {
            }

            return list
                .Where(x => !string.IsNullOrWhiteSpace(x.DriverName))
                .GroupBy(x => $"{x.DriverName}|{x.DiscordUserId}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(x => x.DriverName)
                .ToList();
        }

        private void UpdateUnreadSummary()
        {
            var unread = Threads.Where(x => x.ThreadKey != "empty").Sum(x => x.UnreadCount);
            UnreadSummary = $"Unread: {unread:N0}";
        }

        private void AddEmptyThread(string text)
        {
            Threads.Clear();
            FilteredThreads.Clear();
            SelectedMessages.Clear();

            var row = new ThreadRow
            {
                ThreadKey = "empty",
                DisplayName = text,
                LastMessageUtc = DateTimeOffset.UtcNow,
                SourceMessages = new List<NormalizedMessage>()
            };

            Threads.Add(row);
            FilteredThreads.Add(row);
            SelectedThread = row;

            UpdateUnreadSummary();
            NotifySelectedComputed();
        }

        private List<ThreadRow> BuildConversationRows(List<NormalizedMessage> allMessages, string myDiscordUserId)
        {
            var grouped = allMessages
                .Where(x => !string.IsNullOrWhiteSpace(x.DiscordUserId))
                .GroupBy(x => x.DiscordUserId ?? "", StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var ordered = g
                        .GroupBy(x => x.StableKey, StringComparer.OrdinalIgnoreCase)
                        .Select(x => x.OrderByDescending(m => m.TimestampUtc).First())
                        .OrderBy(x => x.TimestampUtc)
                        .ToList();

                    var last = ordered.Last();

                    var displayName = g.Select(x => x.DriverName)
                        .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

                    if (string.IsNullOrWhiteSpace(displayName))
                        displayName = "Dispatch";

                    var unread = ordered.Count(x => !x.IsMine && !x.IsRead);

                    return new ThreadRow
                    {
                        ThreadKey = g.Key,
                        DisplayName = displayName,
                        DiscordUserId = g.Key,
                        Role = "Driver",
                        LastMessagePreview = string.IsNullOrWhiteSpace(last.Body) ? "(empty)" : last.Body,
                        LastMessageUtc = last.TimestampUtc,
                        UnreadCount = unread,
                        StatusBrush = unread > 0 ? Brushes.MediumPurple : Brushes.Gray,
                        SourceMessages = ordered
                    };
                })
                .OrderByDescending(x => x.LastMessageUtc)
                .ToList();

            if (!string.IsNullOrWhiteSpace(myDiscordUserId) &&
                grouped.All(x => !string.Equals(x.DiscordUserId, myDiscordUserId, StringComparison.OrdinalIgnoreCase)))
            {
                grouped.Insert(0, new ThreadRow
                {
                    ThreadKey = myDiscordUserId,
                    DisplayName = "Dispatch",
                    DiscordUserId = myDiscordUserId,
                    Role = "Driver",
                    LastMessageUtc = DateTimeOffset.UtcNow,
                    UnreadCount = 0,
                    StatusBrush = Brushes.Gray,
                    SourceMessages = new List<NormalizedMessage>()
                });
            }

            return grouped;
        }

        private List<NormalizedMessage> NormalizeAllMessages(string raw)
        {
            var results = new List<NormalizedMessage>();

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            JsonElement arr = default;

            if (root.ValueKind == JsonValueKind.Array)
                arr = root;
            else if (root.ValueKind == JsonValueKind.Object)
            {
                if (!(root.TryGetProperty("rows", out arr) ||
                      root.TryGetProperty("items", out arr) ||
                      root.TryGetProperty("messages", out arr) ||
                      root.TryGetProperty("data", out arr)))
                    return results;
            }
            else
                return results;

            if (arr.ValueKind != JsonValueKind.Array)
                return results;

            var myDiscordUserId = CurrentDiscordUserId;

            foreach (var el in arr.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object)
                    continue;

                var id = GetString(el, "id", "messageId");
                var body = GetString(el, "text", "message", "body", "content");
                var ts = ParseDate(GetString(el, "createdUtc", "timestampUtc", "sentUtc", "serverTsUtc", "createdAt"));
                var driverName = GetString(el, "driverName", "displayName", "name");
                var driverDiscordUserId = GetString(el, "driverDiscordUserId", "discordUserId", "userId", "driverId", "threadUserId");
                var fromDiscordUserId = GetString(el, "fromDiscordUserId", "senderDiscordUserId", "authorDiscordUserId");
                var toDiscordUserId = GetString(el, "toDiscordUserId", "recipientDiscordUserId");
                var fromName = GetString(el, "fromName", "senderName", "authorName", "author");
                var isSystem = GetBool(el, "isSystem", "system");

                var isMine =
                    !string.IsNullOrWhiteSpace(fromDiscordUserId) &&
                    !string.IsNullOrWhiteSpace(myDiscordUserId) &&
                    string.Equals(fromDiscordUserId, myDiscordUserId, StringComparison.OrdinalIgnoreCase);

                if (string.IsNullOrWhiteSpace(fromName))
                    fromName = isMine ? "You" : "Dispatch";

                var fallbackStableId = $"{ts:O}|{body}|{fromDiscordUserId}|{toDiscordUserId}|{driverDiscordUserId}";

                results.Add(new NormalizedMessage
                {
                    MessageId = string.IsNullOrWhiteSpace(id) ? fallbackStableId : id,
                    Body = string.IsNullOrWhiteSpace(body) ? "(empty)" : body,
                    TimestampUtc = ts,
                    FromName = fromName,
                    ToName = isMine ? "Dispatch" : "You",
                    DriverName = string.IsNullOrWhiteSpace(driverName) ? "Dispatch" : driverName,
                    DiscordUserId = driverDiscordUserId,
                    FromDiscordUserId = fromDiscordUserId,
                    ToDiscordUserId = toDiscordUserId,
                    Role = "Driver",
                    IsRead = isMine,
                    IsMine = isMine,
                    IsSystem = isSystem
                });
            }

            return results
                .GroupBy(x => x.StableKey, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.TimestampUtc).First())
                .OrderBy(x => x.TimestampUtc)
                .ToList();
        }

        private void NotifySelectedComputed()
        {
            OnPropertyChanged(nameof(SelectedThreadTitle));
            OnPropertyChanged(nameof(SelectedThreadSubtitle));
            OnPropertyChanged(nameof(ComposeHint));
            OnPropertyChanged(nameof(CanReplyToSelectedThread));
        }

        private static string GetString(JsonElement el, params string[] names)
        {
            foreach (var name in names)
            {
                if (el.TryGetProperty(name, out var p))
                {
                    if (p.ValueKind == JsonValueKind.String)
                        return (p.GetString() ?? "").Trim();

                    if (p.ValueKind == JsonValueKind.Number ||
                        p.ValueKind == JsonValueKind.True ||
                        p.ValueKind == JsonValueKind.False)
                        return p.ToString().Trim();
                }
            }

            return "";
        }

        private static bool GetBool(JsonElement el, params string[] names)
        {
            foreach (var name in names)
            {
                if (el.TryGetProperty(name, out var p))
                {
                    if (p.ValueKind == JsonValueKind.True) return true;
                    if (p.ValueKind == JsonValueKind.False) return false;

                    if (p.ValueKind == JsonValueKind.String && bool.TryParse(p.GetString(), out var parsed))
                        return parsed;
                }
            }

            return false;
        }

        private static DateTimeOffset ParseDate(string raw)
        {
            if (DateTimeOffset.TryParse(raw, out var dt))
                return dt;

            return DateTimeOffset.UtcNow;
        }

        private static bool Contains(string? a, string b)
        {
            return (a ?? "").IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public sealed class ThreadRow : INotifyPropertyChanged
        {
            private int _unreadCount;

            public string ThreadKey { get; set; } = "";
            public string DisplayName { get; set; } = "";
            public string DiscordName { get; set; } = "";
            public string DiscordUserId { get; set; } = "";
            public string Role { get; set; } = "";
            public string LastMessagePreview { get; set; } = "";
            public DateTimeOffset LastMessageUtc { get; set; }
            public Brush StatusBrush { get; set; } = Brushes.Gray;
            public List<NormalizedMessage> SourceMessages { get; set; } = new();

            public int UnreadCount
            {
                get => _unreadCount;
                set
                {
                    if (_unreadCount == value) return;
                    _unreadCount = value;
                    OnPropertyChanged();
                    NotifyComputed();
                }
            }

            public string UnreadBadgeText => UnreadCount <= 0 ? "Read" : $"Unread {UnreadCount}";
            public Visibility UnreadVisibility => Visibility.Visible;
            public string DiscordNameDisplay => string.IsNullOrWhiteSpace(DiscordName) ? "" : DiscordName;
            public string RoleDisplay => string.IsNullOrWhiteSpace(Role) ? "Driver" : Role;
            public string RoleLine => string.IsNullOrWhiteSpace(DiscordNameDisplay) ? RoleDisplay : $"{RoleDisplay} • {DiscordNameDisplay}";
            public string LastMessageTimeDisplay => LastMessageUtc.LocalDateTime.ToString("g", CultureInfo.InvariantCulture);

            public void NotifyComputed()
            {
                OnPropertyChanged(nameof(UnreadBadgeText));
                OnPropertyChanged(nameof(UnreadVisibility));
                OnPropertyChanged(nameof(DiscordNameDisplay));
                OnPropertyChanged(nameof(RoleDisplay));
                OnPropertyChanged(nameof(RoleLine));
                OnPropertyChanged(nameof(LastMessageTimeDisplay));
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            private void OnPropertyChanged([CallerMemberName] string? name = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }

        public sealed class MessageRow : INotifyPropertyChanged
        {
            private bool _isRead;

            public string MessageId { get; set; } = "";
            public string ThreadKey { get; set; } = "";
            public string SenderName { get; set; } = "";
            public string Body { get; set; } = "";
            public DateTimeOffset TimestampUtc { get; set; }
            public bool IsMine { get; set; }
            public bool IsSystem { get; set; }

            public bool IsRead
            {
                get => _isRead;
                set
                {
                    if (_isRead == value) return;
                    _isRead = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ReadText));
                }
            }

            public string ReadText => IsRead ? "Read" : "Unread";

            public HorizontalAlignment BubbleAlignment =>
                IsMine ? HorizontalAlignment.Right : HorizontalAlignment.Left;

            public Brush BubbleBrush
            {
                get
                {
                    if (IsSystem) return new SolidColorBrush(Color.FromRgb(70, 70, 70));

                    return IsMine
                        ? new SolidColorBrush(Color.FromRgb(36, 71, 109))
                        : new SolidColorBrush(Color.FromRgb(40, 40, 40));
                }
            }

            public Brush SenderBrush =>
                IsMine ? Brushes.LightBlue : Brushes.Plum;

            public string SenderLine =>
                IsSystem ? "System" : SenderName;

            public string TimeDisplay =>
                TimestampUtc.LocalDateTime.ToString("g", CultureInfo.InvariantCulture);

            public event PropertyChangedEventHandler? PropertyChanged;

            private void OnPropertyChanged([CallerMemberName] string? name = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }

        public sealed class NormalizedMessage
        {
            public string MessageId { get; set; } = "";
            public string Body { get; set; } = "";
            public DateTimeOffset TimestampUtc { get; set; }
            public string FromName { get; set; } = "";
            public string ToName { get; set; } = "";
            public string DriverName { get; set; } = "";
            public string DiscordUserId { get; set; } = "";
            public string FromDiscordUserId { get; set; } = "";
            public string ToDiscordUserId { get; set; } = "";
            public string Role { get; set; } = "";
            public bool IsRead { get; set; }
            public bool IsMine { get; set; }
            public bool IsSystem { get; set; }

            public string StableKey =>
                !string.IsNullOrWhiteSpace(MessageId)
                    ? MessageId
                    : $"{TimestampUtc:O}|{Body}|{FromDiscordUserId}|{ToDiscordUserId}|{DiscordUserId}";
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}