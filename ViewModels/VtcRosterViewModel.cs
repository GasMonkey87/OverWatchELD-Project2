using OverWatchELD.Services;
using OverWatchELD.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace OverWatchELD.ViewModels
{
    public sealed class VtcRosterViewModel : INotifyPropertyChanged
    {
        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        private static readonly JsonSerializerOptions JsonReadOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly JsonSerializerOptions ProfileJson = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public ObservableCollection<RosterDriverRow> Drivers { get; } = new();
        public ObservableCollection<RosterDriverRow> FilteredDrivers { get; } = new();
        public ObservableCollection<DriverHistoryEntry> SelectedDriverHistory { get; } = new();

        private readonly RelayCommand _openSelectedDriverScoreCommand;

        private string _searchText = "";
        private string _resolvedCurrentUserRole = "";
        private bool _currentUserCanManageRoster;
        private bool _currentUserCanConfigureVtc;

        private RosterDriverRow? _selectedDriver;
        private DriverPerformanceStore.DriverPerf? _selectedDriverPerf;

        private RosterDriverRow? _activeProfileDriver;
        private DriverPerformanceStore.DriverPerf? _activeProfilePerf;

        public VtcRosterViewModel()
        {
            _openSelectedDriverScoreCommand = new RelayCommand(
                _ => OpenSelectedDriverScore(),
                _ => ActiveProfileDriver != null);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText == value) return;
                _searchText = value;
                OnPropertyChanged();
            }
        }

        // Row selection only; does NOT auto-open the right-side profile panel.
        public RosterDriverRow? SelectedDriver
        {
            get => _selectedDriver;
            set
            {
                if (ReferenceEquals(_selectedDriver, value)) return;
                _selectedDriver = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanOpenSelectedDriverScore));
                _openSelectedDriverScoreCommand.RaiseCanExecuteChanged();
            }
        }

        // Legacy/selection-based perf state kept for compatibility.
        public DriverPerformanceStore.DriverPerf? SelectedDriverPerf
        {
            get => _selectedDriverPerf;
            private set
            {
                _selectedDriverPerf = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedDriverScoreDisplay));
                OnPropertyChanged(nameof(SelectedDriverPerformanceSummary));
            }
        }

        public bool CanOpenSelectedDriverScore => SelectedDriver != null;

        public string SelectedDriverScoreDisplay =>
            SelectedDriverPerf == null ? "Score: --" : $"Score: {SelectedDriverPerf.Score:N0}";

        public string SelectedDriverPerformanceSummary
        {
            get
            {
                if (SelectedDriverPerf == null)
                    return "Miles: --   •   Loads: --   •   Hard Brakes: --   •   Overspeed: --";

                return
                    $"Miles Week: {SelectedDriverPerf.MilesWeek:N1}   •   Loads Week: {SelectedDriverPerf.LoadsWeek}   •   " +
                    $"Hard Brakes: {SelectedDriverPerf.HardBrakes}   •   Overspeed: {SelectedDriverPerf.OverspeedEvents}";
            }
        }

        public string SelectedDriverRecentHistoryTitle =>
            SelectedDriver == null ? "Recent History" : $"Recent History • {SelectedDriver.Driver}";

        // New right-side opened profile state.
        public RosterDriverRow? ActiveProfileDriver
        {
            get => _activeProfileDriver;
            private set
            {
                if (ReferenceEquals(_activeProfileDriver, value)) return;
                _activeProfileDriver = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActiveProfileScoreDisplay));
                OnPropertyChanged(nameof(ActiveProfilePerformanceSummary));
                OnPropertyChanged(nameof(ActiveProfileRecentHistoryTitle));
                OnPropertyChanged(nameof(CanOpenActiveProfileScore));
            }
        }

        public DriverPerformanceStore.DriverPerf? ActiveProfilePerf
        {
            get => _activeProfilePerf;
            private set
            {
                _activeProfilePerf = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActiveProfileScoreDisplay));
                OnPropertyChanged(nameof(ActiveProfilePerformanceSummary));
            }
        }

        public bool CanOpenActiveProfileScore => ActiveProfileDriver != null;

        public string ActiveProfileScoreDisplay =>
            ActiveProfilePerf == null ? "Score: --" : $"Score: {ActiveProfilePerf.Score:N0}";

        public string ActiveProfilePerformanceSummary
        {
            get
            {
                if (ActiveProfilePerf == null)
                    return "Miles: --   •   Loads: --   •   Hard Brakes: --   •   Overspeed: --";

                return
                    $"Miles Week: {ActiveProfilePerf.MilesWeek:N1}   •   Loads Week: {ActiveProfilePerf.LoadsWeek}   •   " +
                    $"Hard Brakes: {ActiveProfilePerf.HardBrakes}   •   Overspeed: {ActiveProfilePerf.OverspeedEvents}";
            }
        }

        public string ActiveProfileRecentHistoryTitle =>
            ActiveProfileDriver == null ? "Recent History" : $"Recent History • {ActiveProfileDriver.Driver}";

        public ICommand OpenSelectedDriverScoreCommand => _openSelectedDriverScoreCommand;

        public bool CanCurrentUserEditRow(RosterDriverRow? row) => CanEditProfile(row);

        public bool CurrentUserCanManageRoster
        {
            get => _currentUserCanManageRoster;
            private set
            {
                if (_currentUserCanManageRoster == value) return;
                _currentUserCanManageRoster = value;
                OnPropertyChanged();
            }
        }

        public bool CurrentUserCanConfigureVtc
        {
            get => _currentUserCanConfigureVtc;
            private set
            {
                if (_currentUserCanConfigureVtc == value) return;
                _currentUserCanConfigureVtc = value;
                OnPropertyChanged();
            }
        }

        public string CurrentUserRole
        {
            get => _resolvedCurrentUserRole;
            private set
            {
                if (string.Equals(_resolvedCurrentUserRole, value, StringComparison.Ordinal)) return;
                _resolvedCurrentUserRole = value ?? "";
                OnPropertyChanged();
            }
        }

        public async Task RefreshAsync()
        {
            try
            {
                var cfg = VtcConfigService.Load(forceReload: true);
                var baseUrl = (cfg.BotApiBaseUrl ?? "").Trim().TrimEnd('/');
                var guildId = (cfg.Discord?.GuildId ?? "").Trim();

                Drivers.Clear();
                FilteredDrivers.Clear();
                SelectedDriverHistory.Clear();
                SelectedDriverPerf = null;
                SelectedDriver = null;
                ActiveProfileDriver = null;
                ActiveProfilePerf = null;

                CurrentUserRole = "";
                CurrentUserCanManageRoster = false;
                CurrentUserCanConfigureVtc = false;

                if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(guildId))
                {
                    Drivers.Add(new RosterDriverRow
                    {
                        Driver = "Pair or select a Discord server first.",
                        DiscordName = "",
                        Role = "",
                        Status = "",
                        Truck = "",
                        Location = "",
                        LastSeen = "",
                        TotalDistanceMiles = 0,
                        TotalMassLbs = 0,
                        AchievementsSummary = "None"
                    });

                    ApplyFilter();
                    return;
                }

                await RefreshCurrentUserPermissionsAsync(baseUrl, guildId);

                var url = $"{baseUrl}/api/vtc/roster?guildId={Uri.EscapeDataString(guildId)}";
                var raw = (await Http.GetStringAsync(url))?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(raw))
                {
                    AddEmptyState("No roster data returned.");
                    return;
                }

                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;

                JsonElement arr;
                if (!(root.TryGetProperty("drivers", out arr) ||
                      root.TryGetProperty("members", out arr) ||
                      root.TryGetProperty("items", out arr) ||
                      root.TryGetProperty("data", out arr)))
                {
                    arr = root.ValueKind == JsonValueKind.Array ? root : default;
                }

                if (arr.ValueKind != JsonValueKind.Array)
                {
                    AddEmptyState("Roster data format was not recognized.");
                    return;
                }

                foreach (var el in arr.EnumerateArray())
                {
                    var driver = GetString(el, "driver", "driverName", "name", "displayName", "username");
                    var discordName = GetString(el, "discordName", "discordUsername", "discordTag", "tag");
                    var discordUserId = GetString(el, "discordUserId", "userId", "id");
                    var role = GetString(el, "role", "rank", "vtcRole");
                    var status = GetString(el, "status", "presence", "dutyStatus");
                    var truck = GetString(el, "truck", "truckNumber", "truckName", "assignedTruck");
                    var location = GetString(el, "location", "city", "currentLocation");
                    var lastSeen = GetString(el, "lastSeen", "lastSeenUtc", "serverTsUtc");

                    var totalDistanceMiles = GetDouble(el, "totalDistanceMiles", "miles", "distanceMiles", "totalMiles");
                    var totalMassLbs = GetDouble(el, "totalMassLbs", "massLbs", "totalMass", "cargoMass");

                    var achievementsSummary = GetAchievementsSummary(el);

                    if (string.IsNullOrWhiteSpace(driver))
                        driver = "Unknown Driver";

                    var row = new RosterDriverRow
                    {
                        Driver = driver,
                        DiscordName = discordName,
                        DiscordUserId = discordUserId,
                        Role = string.IsNullOrWhiteSpace(role) ? "Driver" : role,
                        Status = status,
                        Truck = truck,
                        Location = location,
                        LastSeen = NormalizeLastSeen(lastSeen),
                        TotalDistanceMiles = totalDistanceMiles,
                        TotalMassLbs = totalMassLbs,
                        AchievementsSummary = achievementsSummary
                    };

                    ApplyLocalProfileOverlay(row);
                    ApplyPerformanceOverlay(row);
                    Drivers.Add(row);

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var awards = await GetDriverAwardsAsync(baseUrl, guildId, row.DiscordUserId);

                            if (Application.Current == null)
                                return;

                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                row.AwardEmojis.Clear();

                                foreach (var a in awards)
                                {
                                    var emoji = a.Award?.IconEmoji;
                                    if (!string.IsNullOrWhiteSpace(emoji))
                                        row.AwardEmojis.Add(emoji.Trim());
                                }

                                row.NotifyAll();
                            });
                        }
                        catch
                        {
                        }
                    });
                }

                if (Drivers.Count == 0)
                {
                    AddEmptyState("No roster members found.");
                    return;
                }

                SortDrivers();
                ApplyFilter();

                // Keep first row selected for keyboard/navigation convenience,
                // but do not auto-open the profile panel.
                SelectedDriver = FilteredDrivers.FirstOrDefault();
            }
            catch (Exception ex)
            {
                Drivers.Clear();
                FilteredDrivers.Clear();

                Drivers.Add(new RosterDriverRow
                {
                    Driver = "Roster unavailable",
                    DiscordName = ex.Message,
                    Role = "",
                    Status = "",
                    Truck = "",
                    Location = "",
                    LastSeen = "",
                    TotalDistanceMiles = 0,
                    TotalMassLbs = 0,
                    AchievementsSummary = "None"
                });

                ApplyFilter();
            }
        }

        private async Task RefreshCurrentUserPermissionsAsync(string baseUrl, string guildId)
        {
            try
            {
                var myDiscordId = (DiscordIdentityStore.Load()?.DiscordUserId ?? "").Trim();
                if (string.IsNullOrWhiteSpace(myDiscordId))
                    return;

                var url =
                    $"{baseUrl}/api/vtc/me?guildId={Uri.EscapeDataString(guildId)}&discordUserId={Uri.EscapeDataString(myDiscordId)}";

                var raw = (await Http.GetStringAsync(url))?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(raw))
                    return;

                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;

                if (root.TryGetProperty("resolvedRole", out var roleEl))
                    CurrentUserRole = (roleEl.GetString() ?? "").Trim();

                if (root.TryGetProperty("canManageRoster", out var manageEl))
                    CurrentUserCanManageRoster = manageEl.ValueKind == JsonValueKind.True;

                if (root.TryGetProperty("canConfigureVtc", out var cfgEl))
                    CurrentUserCanConfigureVtc = cfgEl.ValueKind == JsonValueKind.True;

                if (string.IsNullOrWhiteSpace(CurrentUserRole))
                    CurrentUserRole = "Driver";
            }
            catch
            {
                CurrentUserRole = "";
                CurrentUserCanManageRoster = false;
                CurrentUserCanConfigureVtc = false;
            }
        }

        public void ApplyFilter()
        {
            var q = (SearchText ?? "").Trim();

            var filtered = string.IsNullOrWhiteSpace(q)
                ? Drivers.ToList()
                : Drivers.Where(x =>
                       Contains(x.Driver, q) ||
                       Contains(x.DiscordName, q) ||
                       Contains(x.Role, q) ||
                       Contains(x.Status, q) ||
                       Contains(x.Truck, q) ||
                       Contains(x.Location, q) ||
                       Contains(x.AchievementsSummary, q) ||
                       Contains(x.AwardCountDisplay, q) ||
                       Contains(x.PerformanceDisplay, q))
                    .ToList();

            FilteredDrivers.Clear();
            foreach (var item in filtered)
                FilteredDrivers.Add(item);
        }

        private bool CanEditProfile(RosterDriverRow? row)
        {
            try
            {
                if (row == null)
                    return false;

                var myId = (DiscordIdentityStore.Load()?.DiscordUserId ?? "").Trim();
                var targetId = (row.DiscordUserId ?? "").Trim();

                bool isAdmin = CurrentUserCanManageRoster;

                bool isSelf =
                    !string.IsNullOrWhiteSpace(myId) &&
                    !string.IsNullOrWhiteSpace(targetId) &&
                    string.Equals(myId, targetId, StringComparison.OrdinalIgnoreCase);

                return isAdmin || isSelf;
            }
            catch
            {
                return false;
            }
        }

        public void OpenSelectedDriverProfile()
        {
            try
            {
                if (SelectedDriver == null)
                    return;

                ActiveProfileDriver = SelectedDriver;

                SelectedDriverHistory.Clear();
                ActiveProfilePerf = null;

                var id = (ActiveProfileDriver.DiscordUserId ?? "").Trim();
                if (string.IsNullOrWhiteSpace(id))
                    return;

                ActiveProfilePerf = DriverPerformanceStore.Get(id);

                // Keep legacy selected-driver fields in sync too.
                SelectedDriverPerf = ActiveProfilePerf;

                foreach (var item in DriverHistoryStore.GetRecent(id, 25))
                    SelectedDriverHistory.Add(item);

                _openSelectedDriverScoreCommand.RaiseCanExecuteChanged();
            }
            catch
            {
            }
        }

        public async Task ChangeNameAsync(RosterDriverRow row, string newName)
        {
            try
            {
                if (!CanEditProfile(row))
                {
                    MessageBox.Show("You can only edit your own profile unless you are Owner/Admin/Manager.",
                        "Roster",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                newName = (newName ?? "").Trim();
                if (string.IsNullOrWhiteSpace(newName))
                    return;

                var cfg = VtcConfigService.Load(forceReload: true);
                var baseUrl = (cfg.BotApiBaseUrl ?? "").Trim().TrimEnd('/');
                var guildId = (cfg.Discord?.GuildId ?? "").Trim();

                if (!string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(guildId))
                {
                    try
                    {
                        var payload = new
                        {
                            guildId,
                            discordUserId = row.DiscordUserId,
                            driverId = row.DiscordUserId,
                            newName
                        };

                        var json = JsonSerializer.Serialize(payload);
                        using var content = new StringContent(json, Encoding.UTF8, "application/json");
                        await Http.PostAsync($"{baseUrl}/api/vtc/roster/rename", content);
                    }
                    catch
                    {
                    }
                }

                UpdateLocalProfile(row.DiscordUserId ?? "", p =>
                {
                    p["DriverName"] = newName;
                });

                row.Driver = newName;
                row.NotifyAll();

                SortDrivers();
                ApplyFilter();

                if (ReferenceEquals(ActiveProfileDriver, row) || ReferenceEquals(SelectedDriver, row))
                    OpenSelectedDriverProfile();

                MessageBox.Show("Roster name updated.", "Roster", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to change name: " + ex.Message,
                    "Roster",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        public async Task ChangeTruckAsync(RosterDriverRow row, string newTruck)
        {
            try
            {
                if (!CanEditProfile(row))
                {
                    MessageBox.Show("You can only edit your own truck unless you are Owner/Admin/Manager.",
                        "Roster",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                newTruck = (newTruck ?? "").Trim();

                var cfg = VtcConfigService.Load(forceReload: true);
                var baseUrl = (cfg.BotApiBaseUrl ?? "").Trim().TrimEnd('/');
                var guildId = (cfg.Discord?.GuildId ?? "").Trim();

                if (!string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(guildId))
                {
                    try
                    {
                        var payload = new
                        {
                            driverId = (row.DiscordUserId ?? "").Trim(),
                            name = (row.Driver ?? "").Trim(),
                            discordUserId = (row.DiscordUserId ?? "").Trim(),
                            discordUsername = "",
                            truckNumber = newTruck,
                            role = (row.Role ?? "").Trim(),
                            status = (row.Status ?? "").Trim(),
                            notes = ""
                        };

                        var json = JsonSerializer.Serialize(payload);
                        using var content = new StringContent(json, Encoding.UTF8, "application/json");
                        await Http.PostAsync($"{baseUrl}/api/vtc/roster/update?guildId={Uri.EscapeDataString(guildId)}", content);
                    }
                    catch
                    {
                    }
                }

                UpdateLocalProfile(row.DiscordUserId ?? "", p =>
                {
                    p["TruckNumber"] = newTruck;
                    p["TruckName"] = newTruck;
                });

                row.Truck = newTruck;
                row.NotifyAll();

                SortDrivers();
                ApplyFilter();

                if (ReferenceEquals(ActiveProfileDriver, row) || ReferenceEquals(SelectedDriver, row))
                    OpenSelectedDriverProfile();

                MessageBox.Show("Roster truck updated.", "Roster", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to change truck: " + ex.Message,
                    "Roster",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        public async Task KickAsync(RosterDriverRow row)
        {
            try
            {
                if (!CurrentUserCanManageRoster)
                {
                    MessageBox.Show("Only Owner/Admin/Manager can kick roster members.",
                        "Roster",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var cfg = VtcConfigService.Load(forceReload: true);
                var baseUrl = (cfg.BotApiBaseUrl ?? "").Trim().TrimEnd('/');
                var guildId = (cfg.Discord?.GuildId ?? "").Trim();

                if (!string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(guildId))
                {
                    try
                    {
                        var payload = new
                        {
                            guildId,
                            discordUserId = row.DiscordUserId,
                            driverId = row.DiscordUserId
                        };

                        var json = JsonSerializer.Serialize(payload);
                        using var content = new StringContent(json, Encoding.UTF8, "application/json");
                        await Http.PostAsync($"{baseUrl}/api/vtc/roster/kick", content);
                    }
                    catch
                    {
                    }
                }

                Drivers.Remove(row);
                FilteredDrivers.Remove(row);

                if (ReferenceEquals(SelectedDriver, row))
                    SelectedDriver = FilteredDrivers.FirstOrDefault();

                if (ReferenceEquals(ActiveProfileDriver, row))
                {
                    ActiveProfileDriver = null;
                    ActiveProfilePerf = null;
                    SelectedDriverHistory.Clear();
                    SelectedDriverPerf = null;
                }

                MessageBox.Show("Roster member removed.", "Roster", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to kick member: " + ex.Message,
                    "Roster",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OpenSelectedDriverScore()
        {
            try
            {
                if (ActiveProfileDriver == null)
                    return;

                var vm = new DriverScoreViewModel(
                    ActiveProfileDriver.DiscordUserId ?? "",
                    ActiveProfileDriver.Driver ?? "Driver");

                var win = new DriverScoreWindow
                {
                    Owner = Application.Current?.MainWindow,
                    DataContext = vm
                };

                win.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to open driver score: " + ex.Message,
                    "Score",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // Kept for compatibility; does not auto-open profile anymore.
        private void LoadSelectedDriverDetails()
        {
            SelectedDriverHistory.Clear();
            SelectedDriverPerf = null;

            OnPropertyChanged(nameof(SelectedDriverRecentHistoryTitle));

            var id = (SelectedDriver?.DiscordUserId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(id))
                return;

            SelectedDriverPerf = DriverPerformanceStore.Get(id);

            foreach (var item in DriverHistoryStore.GetRecent(id, 25))
                SelectedDriverHistory.Add(item);
        }

        private void ApplyPerformanceOverlay(RosterDriverRow row)
        {
            try
            {
                var id = (row.DiscordUserId ?? "").Trim();
                if (string.IsNullOrWhiteSpace(id))
                    return;

                var perf = DriverPerformanceStore.Get(id);

                row.MilesWeek = perf.MilesWeek;
                row.LoadsWeek = perf.LoadsWeek;
                row.HardBrakes = perf.HardBrakes;
                row.OverspeedEvents = perf.OverspeedEvents;
                row.Score = perf.Score;
                row.NotifyAll();
            }
            catch
            {
            }
        }

        private void SortDrivers()
        {
            var ordered = Drivers
                .OrderByDescending(x => IsRolePriority(x.Role))
                .ThenByDescending(x => LooksOnline(x.Status))
                .ThenByDescending(x => x.Score)
                .ThenBy(x => x.Driver)
                .ToList();

            Drivers.Clear();
            foreach (var item in ordered)
                Drivers.Add(item);
        }

        private static bool IsRolePriority(string? role)
        {
            var r = (role ?? "").Trim();
            return r.Equals("Owner", StringComparison.OrdinalIgnoreCase) ||
                   r.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                   r.Equals("Manager", StringComparison.OrdinalIgnoreCase);
        }

        private void AddEmptyState(string message)
        {
            Drivers.Clear();
            FilteredDrivers.Clear();

            Drivers.Add(new RosterDriverRow
            {
                Driver = message,
                DiscordName = "",
                Role = "",
                Status = "",
                Truck = "",
                Location = "",
                LastSeen = "",
                TotalDistanceMiles = 0,
                TotalMassLbs = 0,
                AchievementsSummary = "None"
            });

            ApplyFilter();
        }

        private static bool Contains(string? source, string value)
        {
            return (source ?? "").IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool LooksOnline(string? status)
        {
            if (string.IsNullOrWhiteSpace(status)) return false;
            var s = status.Trim();

            return s.Equals("online", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("active", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("driving", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("on duty", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("onduty", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeLastSeen(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "-";

            if (DateTimeOffset.TryParse(raw, out var dt))
                return dt.LocalDateTime.ToString("g", CultureInfo.InvariantCulture);

            return raw.Trim();
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

        private static double GetDouble(JsonElement el, params string[] names)
        {
            foreach (var name in names)
            {
                if (el.TryGetProperty(name, out var p))
                {
                    if (p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var d))
                        return d;

                    if (p.ValueKind == JsonValueKind.String &&
                        double.TryParse(p.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                        return parsed;
                }
            }

            return 0;
        }

        private static string GetAchievementsSummary(JsonElement el)
        {
            try
            {
                if (el.TryGetProperty("achievements", out var a) && a.ValueKind == JsonValueKind.Array)
                {
                    var parts = a.EnumerateArray()
                        .Select(x => x.ValueKind == JsonValueKind.String ? (x.GetString() ?? "").Trim() : x.ToString().Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToList();

                    return parts.Count == 0 ? "None" : string.Join(" • ", parts);
                }

                if (el.TryGetProperty("achievementCount", out var countEl))
                {
                    var count = countEl.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(count))
                        return $"{count} unlocked";
                }
            }
            catch
            {
            }

            return "None";
        }

        private static void ApplyLocalProfileOverlay(RosterDriverRow row)
        {
            try
            {
                var profile = LoadLocalProfile(row.DiscordUserId ?? "", row.Driver ?? "");
                if (profile.Count == 0)
                    return;

                var localDriver = GetProfileValue(profile, "DriverName");
                var localDiscord = GetProfileValue(profile, "DiscordName");
                var localTruck = FirstNonEmpty(
                    GetProfileValue(profile, "TruckNumber"),
                    GetProfileValue(profile, "TruckName"));

                if (!string.IsNullOrWhiteSpace(localDriver))
                    row.Driver = localDriver;

                if (!string.IsNullOrWhiteSpace(localDiscord))
                    row.DiscordName = localDiscord;

                if (!string.IsNullOrWhiteSpace(localTruck))
                    row.Truck = localTruck;

                if (int.TryParse(GetProfileValue(profile, "HardBrakes"), out var hb))
                    row.HardBrakes = hb;

                if (int.TryParse(GetProfileValue(profile, "OverspeedEvents"), out var os))
                    row.OverspeedEvents = os;
            }
            catch
            {
            }
        }

        private static void UpdateLocalProfile(string driverId, Action<Dictionary<string, string>> update)
        {
            try
            {
                var safeId = Sanitize(FirstNonEmpty(driverId, "default"));

                var path = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Config",
                    "DriverProfiles",
                    $"{safeId}.json");

                Dictionary<string, string> data;

                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    data = JsonSerializer.Deserialize<Dictionary<string, string>>(json, ProfileJson)
                           ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

                update(data);
                data["UpdatedUtc"] = DateTime.UtcNow.ToString("o");

                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                var outJson = JsonSerializer.Serialize(data, ProfileJson);
                File.WriteAllText(path, outJson);
            }
            catch
            {
            }
        }

        private static Dictionary<string, string> LoadLocalProfile(string driverId, string driverNameFallback)
        {
            try
            {
                var safeId = Sanitize(FirstNonEmpty(driverId, driverNameFallback, "default"));

                var path = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Config",
                    "DriverProfiles",
                    $"{safeId}.json");

                if (!File.Exists(path))
                    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json, ProfileJson)
                       ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static string GetProfileValue(Dictionary<string, string> data, string key)
        {
            if (data.TryGetValue(key, out var value))
                return (value ?? "").Trim();

            return "";
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var v in values)
            {
                var s = (v ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(s))
                    return s;
            }

            return "";
        }

        private static string Sanitize(string value)
        {
            value ??= "default";
            foreach (var c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');

            value = value.Trim();
            return string.IsNullOrWhiteSpace(value) ? "default" : value;
        }

        private async Task<List<DriverAwardDto>> GetDriverAwardsAsync(string baseUrl, string guildId, string driverId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(baseUrl) ||
                    string.IsNullOrWhiteSpace(guildId) ||
                    string.IsNullOrWhiteSpace(driverId))
                {
                    return new List<DriverAwardDto>();
                }

                var url =
                    $"{baseUrl}/api/vtc/awards/driver?guildId={Uri.EscapeDataString(guildId)}&driverId={Uri.EscapeDataString(driverId)}";

                var data = await Http.GetFromJsonAsync<DriverAwardsResponse>(url, JsonReadOpts);
                return data?.Awards ?? new List<DriverAwardDto>();
            }
            catch
            {
                return new List<DriverAwardDto>();
            }
        }

        private sealed class DriverAwardsResponse
        {
            public bool Ok { get; set; }
            public List<DriverAwardDto>? Awards { get; set; }
        }

        private sealed class DriverAwardDto
        {
            public string DriverId { get; set; } = "";
            public string DriverName { get; set; } = "";
            public string AwardId { get; set; } = "";
            public DateTime AwardedUtc { get; set; }
            public string AwardedByUsername { get; set; } = "";
            public string Note { get; set; } = "";
            public AwardDto? Award { get; set; }
        }

        private sealed class AwardDto
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string Description { get; set; } = "";
            public string IconEmoji { get; set; } = "🏆";
        }

        public sealed class RosterDriverRow : INotifyPropertyChanged
        {
            private string _driver = "";
            private string _discordName = "";
            private string _discordUserId = "";
            private string _role = "";
            private string _status = "";
            private string _truck = "";
            private string _location = "";
            private string _lastSeen = "";
            private double _totalDistanceMiles;
            private double _totalMassLbs;
            private string _achievementsSummary = "None";
            private double _milesWeek;
            private int _loadsWeek;
            private int _hardBrakes;
            private int _overspeedEvents;
            private double _score;

            public List<string> AwardEmojis { get; } = new();

            public int AwardCount => AwardEmojis.Count;

            public string AwardEmojiSummary
            {
                get
                {
                    if (AwardEmojis.Count == 0)
                        return "";

                    return string.Join(" ", AwardEmojis
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Take(5));
                }
            }

            public string AwardCountDisplay
            {
                get
                {
                    var clean = AwardEmojis
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToList();

                    if (clean.Count == 0)
                        return "";

                    if (clean.Count <= 5)
                        return string.Join(" ", clean);

                    return $"{string.Join(" ", clean.Take(3))}  +{clean.Count - 3}";
                }
            }

            public string Driver
            {
                get => _driver;
                set { _driver = value; OnPropertyChanged(); OnComputedChanged(); }
            }

            public string DiscordName
            {
                get => _discordName;
                set { _discordName = value; OnPropertyChanged(); OnComputedChanged(); }
            }

            public string DiscordUserId
            {
                get => _discordUserId;
                set { _discordUserId = value; OnPropertyChanged(); }
            }

            public string Role
            {
                get => _role;
                set { _role = value; OnPropertyChanged(); OnComputedChanged(); }
            }

            public string Status
            {
                get => _status;
                set { _status = value; OnPropertyChanged(); OnComputedChanged(); }
            }

            public string Truck
            {
                get => _truck;
                set { _truck = value; OnPropertyChanged(); OnComputedChanged(); }
            }

            public string Location
            {
                get => _location;
                set { _location = value; OnPropertyChanged(); OnComputedChanged(); }
            }

            public string LastSeen
            {
                get => _lastSeen;
                set { _lastSeen = value; OnPropertyChanged(); }
            }

            public double TotalDistanceMiles
            {
                get => _totalDistanceMiles;
                set { _totalDistanceMiles = value; OnPropertyChanged(); OnComputedChanged(); }
            }

            public double TotalMassLbs
            {
                get => _totalMassLbs;
                set { _totalMassLbs = value; OnPropertyChanged(); OnComputedChanged(); }
            }

            public string AchievementsSummary
            {
                get => _achievementsSummary;
                set { _achievementsSummary = value; OnPropertyChanged(); OnComputedChanged(); }
            }

            public double MilesWeek
            {
                get => _milesWeek;
                set { _milesWeek = value; OnPropertyChanged(); OnComputedChanged(); }
            }

            public int LoadsWeek
            {
                get => _loadsWeek;
                set { _loadsWeek = value; OnPropertyChanged(); OnComputedChanged(); }
            }

            public int HardBrakes
            {
                get => _hardBrakes;
                set { _hardBrakes = value; OnPropertyChanged(); OnComputedChanged(); }
            }

            public int OverspeedEvents
            {
                get => _overspeedEvents;
                set { _overspeedEvents = value; OnPropertyChanged(); OnComputedChanged(); }
            }

            public double Score
            {
                get => _score;
                set { _score = value; OnPropertyChanged(); OnComputedChanged(); }
            }

            public string Initials
            {
                get
                {
                    var value = (Driver ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(value)) return "?";

                    var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 1) return parts[0].Substring(0, 1).ToUpperInvariant();

                    return $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant();
                }
            }

            public string DiscordNameDisplay =>
                string.IsNullOrWhiteSpace(DiscordName) ? "Discord: --" : $"Discord: {DiscordName.Trim()}";

            public string RoleDisplay =>
                string.IsNullOrWhiteSpace(Role) ? "Driver" : Role.Trim();

            public string StatusDisplay =>
                string.IsNullOrWhiteSpace(Status) ? "Offline" : Status.Trim();

            public string TotalDistanceDisplay =>
                $"{TotalDistanceMiles:N0} mi";

            public string TotalMassDisplay =>
                $"{TotalMassLbs:N0} lbs";

            public string TruckLocationDisplay
            {
                get
                {
                    var truck = string.IsNullOrWhiteSpace(Truck) ? "--" : Truck.Trim();
                    var location = string.IsNullOrWhiteSpace(Location) ? "--" : Location.Trim();
                    return $"Truck: {truck}   •   Location: {location}";
                }
            }

            public string PerformanceDisplay =>
                $"Score: {Score:N0}   •   Week: {MilesWeek:N1} mi   •   Loads: {LoadsWeek}   •   HB: {HardBrakes}   •   OS: {OverspeedEvents}";

            public Brush StatusBrush
            {
                get
                {
                    var s = (Status ?? "").Trim();

                    if (s.Equals("online", StringComparison.OrdinalIgnoreCase) ||
                        s.Equals("active", StringComparison.OrdinalIgnoreCase) ||
                        s.Equals("driving", StringComparison.OrdinalIgnoreCase) ||
                        s.Equals("on duty", StringComparison.OrdinalIgnoreCase) ||
                        s.Equals("onduty", StringComparison.OrdinalIgnoreCase))
                        return Brushes.LimeGreen;

                    if (s.Equals("idle", StringComparison.OrdinalIgnoreCase) ||
                        s.Equals("parked", StringComparison.OrdinalIgnoreCase) ||
                        s.Equals("break", StringComparison.OrdinalIgnoreCase))
                        return Brushes.Goldenrod;

                    return Brushes.IndianRed;
                }
            }

            public Brush RoleBadgeBrush
            {
                get
                {
                    var r = (Role ?? "").Trim();

                    if (r.Equals("Owner", StringComparison.OrdinalIgnoreCase))
                        return new SolidColorBrush(Color.FromRgb(160, 120, 20));

                    if (r.Equals("Admin", StringComparison.OrdinalIgnoreCase))
                        return new SolidColorBrush(Color.FromRgb(140, 45, 45));

                    if (r.Equals("Manager", StringComparison.OrdinalIgnoreCase))
                        return new SolidColorBrush(Color.FromRgb(45, 95, 145));

                    return new SolidColorBrush(Color.FromRgb(70, 70, 70));
                }
            }

            public void NotifyAll()
            {
                OnPropertyChanged(nameof(Driver));
                OnPropertyChanged(nameof(DiscordName));
                OnPropertyChanged(nameof(Role));
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(Truck));
                OnPropertyChanged(nameof(Location));
                OnPropertyChanged(nameof(LastSeen));
                OnPropertyChanged(nameof(TotalDistanceMiles));
                OnPropertyChanged(nameof(TotalMassLbs));
                OnPropertyChanged(nameof(AchievementsSummary));
                OnPropertyChanged(nameof(MilesWeek));
                OnPropertyChanged(nameof(LoadsWeek));
                OnPropertyChanged(nameof(HardBrakes));
                OnPropertyChanged(nameof(OverspeedEvents));
                OnPropertyChanged(nameof(Score));
                OnPropertyChanged(nameof(AwardCount));
                OnPropertyChanged(nameof(AwardEmojiSummary));
                OnPropertyChanged(nameof(AwardCountDisplay));
                OnComputedChanged();
            }

            private void OnComputedChanged()
            {
                OnPropertyChanged(nameof(Initials));
                OnPropertyChanged(nameof(DiscordNameDisplay));
                OnPropertyChanged(nameof(RoleDisplay));
                OnPropertyChanged(nameof(StatusDisplay));
                OnPropertyChanged(nameof(TotalDistanceDisplay));
                OnPropertyChanged(nameof(TotalMassDisplay));
                OnPropertyChanged(nameof(TruckLocationDisplay));
                OnPropertyChanged(nameof(PerformanceDisplay));
                OnPropertyChanged(nameof(StatusBrush));
                OnPropertyChanged(nameof(RoleBadgeBrush));
                OnPropertyChanged(nameof(AwardCount));
                OnPropertyChanged(nameof(AwardEmojiSummary));
                OnPropertyChanged(nameof(AwardCountDisplay));
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            private void OnPropertyChanged([CallerMemberName] string? name = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }

        private sealed class RelayCommand : ICommand
        {
            private readonly Action<object?> _execute;
            private readonly Func<object?, bool>? _canExecute;

            public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
            {
                _execute = execute;
                _canExecute = canExecute;
            }

            public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
            public void Execute(object? parameter) => _execute(parameter);

            public event EventHandler? CanExecuteChanged;
            public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}