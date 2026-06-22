using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using OverWatchELD.Services;
using OverWatchELD.Services.Fleet;
using OverWatchELD.Models.Fleet;

namespace OverWatchELD.ViewModels
{
    /// <summary>
    /// Phase 2.5: Adds mileage leaderboards under the Discord announcements area.
    /// Uses the existing local OverWatchELD.db tables created in Phase 2.
    /// </summary>
    public sealed class VtcDashboardPanelViewModel : INotifyPropertyChanged
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public ICommand RefreshCommand { get; }

        private string _modeText = "VTC Mode";
        public string ModeText { get => _modeText; set { _modeText = value; OnPropertyChanged(); } }

        private string _vtcNameText = "VTC: —";
        public string VtcNameText { get => _vtcNameText; set { _vtcNameText = value; OnPropertyChanged(); } }

        private string _botApiText = "Bot API: —";
        public string BotApiText { get => _botApiText; set { _botApiText = value; OnPropertyChanged(); } }

        private string _linkText = "Link: —";
        public string LinkText { get => _linkText; set { _linkText = value; OnPropertyChanged(); } }

        private string _lastUpdatedText = "Last Updated: —";
        public string LastUpdatedText { get => _lastUpdatedText; set { _lastUpdatedText = value; OnPropertyChanged(); } }

        public ObservableCollection<LeaderboardRow> Weekly { get; } = new();
        public ObservableCollection<LeaderboardRow> Monthly { get; } = new();
        public ObservableCollection<FleetAnalyticsTruckRow> FleetTrucks { get; } = new();

        public sealed class LeaderboardRow
        {
            public int Rank { get; set; }
            public string Driver { get; set; } = "";
            public string Miles { get; set; } = "";
        }

        public VtcDashboardPanelViewModel()
        {
            RefreshCommand = new RelayCommand(async _ => await RefreshAsync());
            _ = RefreshAsync();
        }

        public async Task RefreshAsync()
        {
            try
            {
                await Task.Delay(10);

                var cfg = VtcConfigService.Load();
                var link = VtcLinkService.GetLink();
                var isLinked = link != null && link.Linked && !string.IsNullOrWhiteSpace(link.DiscordUserId);

                var vtcEnabled = cfg != null && cfg.Enabled;

                // Public release UX:
                // - Show VTC name from config even if the user hasn't linked their Discord yet.
                // - Linking is optional per-driver.
                var cfgVtcName = (cfg?.VtcName ?? "").Trim();
                var cfgVtcShort = (cfg?.VtcShort ?? "").Trim();

                if (!vtcEnabled)
                    ModeText = "Standalone (Not Linked)";
                else
                    ModeText = isLinked ? "VTC Mode (Linked)" : "VTC Mode (Not Linked)";

                var vtcNameToShow = !string.IsNullOrWhiteSpace(cfgVtcShort) ? cfgVtcShort : cfgVtcName;
                if (string.IsNullOrWhiteSpace(vtcNameToShow))
                    vtcNameToShow = !string.IsNullOrWhiteSpace(link?.VtcName) ? link!.VtcName : "—";

                VtcNameText = "VTC: " + vtcNameToShow;

                // Bot API status (best-effort; doesn't require auth)
                var baseUrl = (cfg?.BotApiBaseUrl ?? "").Trim().TrimEnd('/');
                var online = await IsBotApiOnlineAsync(baseUrl);
                BotApiText = "Bot API: " + (online ? "Online" : "Offline");

                LinkText = isLinked ? $"Linked as {link!.DiscordUserName} ({link.DiscordUserId})" : "Not linked";
                LastUpdatedText = "Last Updated: " + DateTime.Now.ToString("MMM d, yyyy h:mm tt");

                LoadLeaderboard();
                LoadFleetTrucks();
            }
            catch (Exception ex)
            {
                ModeText = "Error";
                LinkText = ex.Message;
            }
        }


        private void LoadFleetTrucks()
        {
            FleetTrucks.Clear();

            foreach (var truck in FleetTruckApprovalService.BuildFleetTruckRows())
            {
                FleetTrucks.Add(truck);
            }
        }

        private static async Task<bool> IsBotApiOnlineAsync(string baseUrl)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(baseUrl)) return false;

                // Try a few common health endpoints without assuming a specific hub build.
                var urls = new[]
                {
                    baseUrl + "/api/health",
                    baseUrl + "/health",
                    baseUrl + "/api/ping",
                    baseUrl
                };

                foreach (var u in urls)
                {
                    try
                    {
                        using var resp = await _http.GetAsync(u);
                        if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode < 500)
                            return true;
                    }
                    catch { }
                }
            }
            catch { }

            return false;
        }

        private void LoadLeaderboard()
        {
            Weekly.Clear();
            Monthly.Clear();

            var w = DatabaseService.GetMileageLeaderboard(7);
            var m = DatabaseService.GetMileageLeaderboard(30);

            int r = 1;
            foreach (var row in w)
                Weekly.Add(new LeaderboardRow { Rank = r++, Driver = row.driverName, Miles = $"{row.miles:0.0}" });

            r = 1;
            foreach (var row in m)
                Monthly.Add(new LeaderboardRow { Rank = r++, Driver = row.driverName, Miles = $"{row.miles:0.0}" });
        }
    }
}
