// MainWindow.xaml.cs ✅ FULL COPY/REPLACE
// ✅ Keeps current handlers / companion / VTC / profile / export behavior
// ✅ X button fully exits
// ✅ Minimize goes to system tray
// ✅ Tray icon supports Open / Exit
// ✅ Premium update indicator: clickable, auto-checks, green update available light
// ✅ Does NOT change locked clocks/graphs/log rendering behavior

using OverWatchELD.Services.Fleet;
using OverWatchELD.ViewModels;
using OverWatchELD.Views;
using OverWatchELD.Services;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Input;
using IOPath = System.IO.Path;
using System.Net;
using System.Net.Sockets;
using Forms = System.Windows.Forms;
using System.Media;
using OverWatchELD.Views.Fleet;

namespace OverWatchELD
{
    public partial class MainWindow : Window
    {
        // ===== Portal Integration =====
        private const string PortalUrl = "https://overwatcheld.up.railway.app/portal.html";
        private const string ManageUrl = "https://overwatcheld.up.railway.app/manage.html";
        private const string LatestUpdateApiUrl = "https://overwatcheld.up.railway.app/api/updates/latest";
        private const string DefaultDownloadUrl = "https://overwatcheld.up.railway.app/downloads.html";

        private static readonly HttpClient _vtcHttp = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(4)
        };
        private static readonly HttpClient _updateHttp = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        private static readonly HttpClient _companionHttp = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(2)
        };
        private DispatcherTimer? _checkEngineFlashTimer;
        private int _checkEngineFlashTicks;
        private static readonly JsonSerializerOptions _vtcJson = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private bool IsDeveloperUser()
        {
            try
            {
                var identity = DiscordIdentityStore.Load();

                var currentId = "";

                try
                {
                    var prop = identity?.GetType().GetProperty("DiscordUserId");

                    currentId =
                        (prop?.GetValue(identity)?.ToString() ?? "")
                        .Trim();
                }
                catch
                {
                    currentId = "";
                }

                var developerId =
                    (Environment.GetEnvironmentVariable("OVERWATCH_DEV_DISCORD_ID") ?? "")
                    .Trim();

                if (string.IsNullOrWhiteSpace(currentId))
                    return false;

                if (string.IsNullOrWhiteSpace(developerId))
                    return false;

                return string.Equals(
                    currentId,
                    developerId,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private void RequestMaintenance_Click(object sender, RoutedEventArgs e)
        {
            var win = new MaintenanceRequestWindow
            {
                Owner = this
            };

            win.ShowDialog();
        }

        private static string BotBase =>
            (Services.VtcConfigService.Load().BotApiBaseUrl ?? "").Trim().TrimEnd('/');

        private readonly DispatcherTimer _vtcPollTimer = new DispatcherTimer();
        private readonly DispatcherTimer _companionStatusTimer = new DispatcherTimer();
        private readonly DispatcherTimer _updateCheckTimer = new DispatcherTimer();

        private volatile bool _vtcUiEnabled = false;
        private volatile bool _vtcPollInFlight = false;
        private volatile bool _updateCheckInFlight = false;

        private string _latestUpdateUrl = DefaultDownloadUrl;
        private string _latestUpdateVersion = "";
        private bool _updateAvailable = false;
        private bool _updateDismissedThisSession = false;

        // ✅ Tray support
        private Forms.NotifyIcon? _trayIcon;
        private bool _allowRealClose;

        private static FleetMaintenanceService? _fleetSvc;
        private static FleetMaintenanceService FleetSvc
        {
            get
            {
                try
                {
                    var app = Application.Current as App;
                    if (app != null)
                    {
                        var prop = app.GetType().GetProperty("Fleet");
                        if (prop != null)
                        {
                            var v = prop.GetValue(app) as FleetMaintenanceService;
                            if (v != null) return v;
                        }
                    }
                }
                catch { }

                _fleetSvc ??= new FleetMaintenanceService(new FleetStore(), new FleetRules());
                return _fleetSvc;
            }
        }
        private void OnMaintenanceMalfunctionRaised(MaintenanceMalfunctionAlert alert)
        {
            Dispatcher.Invoke(() =>
            {
                try { SystemSounds.Exclamation.Play(); } catch { }

                try
                {
                    if (CheckEngineAlertText != null)
                        CheckEngineAlertText.Text = $"{alert.UnitNumber} {alert.TruckName}: {alert.Issue}";

                    if (CheckEngineAlertBorder != null)
                    {
                        CheckEngineAlertBorder.Visibility = Visibility.Visible;
                        CheckEngineAlertBorder.Opacity = 1;
                    }

                    _checkEngineFlashTicks = 0;

                    _checkEngineFlashTimer?.Stop();
                    _checkEngineFlashTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(350)
                    };

                    _checkEngineFlashTimer.Tick += (_, __) =>
                    {
                        _checkEngineFlashTicks++;

                        if (CheckEngineAlertBorder != null)
                        {
                            CheckEngineAlertBorder.Background =
                                _checkEngineFlashTicks % 2 == 0
                                    ? new SolidColorBrush(Color.FromRgb(153, 27, 27))
                                    : new SolidColorBrush(Color.FromRgb(239, 68, 68));
                        }

                        if (_checkEngineFlashTicks >= 24)
                        {
                            _checkEngineFlashTimer?.Stop();

                            if (CheckEngineAlertBorder != null)
                            {
                                CheckEngineAlertBorder.Visibility = Visibility.Visible;
                                CheckEngineAlertBorder.Background = new SolidColorBrush(Color.FromRgb(127, 29, 29));
                            }
                        }
                    };

                    _checkEngineFlashTimer.Start();
                }
                catch { }
            });
        }

        private void ClearCheckEngineAlert_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _checkEngineFlashTimer?.Stop();

                if (CheckEngineAlertBorder != null)
                    CheckEngineAlertBorder.Visibility = Visibility.Collapsed;

                if (CheckEngineAlertText != null)
                    CheckEngineAlertText.Text = "";
            }
            catch
            {
            }
        }

        private sealed class VtcBrandingConfig
        {
            public string BannerImagePath { get; set; } = "";
            public string IconImagePath { get; set; } = "";
        }

        public MainWindow()
        {
            InitializeComponent();

            try
            {
                var online = OnlinePresenceService.Load();

                if (OnlineToggle != null)
                    OnlineToggle.IsChecked = online;

                if (Application.Current is App app)
                {
                    app.EnsureSession();
                    app.Session.IsOnline = online;
                }
            }
            catch { }

            try
            {
                FleetAlertHub.OnAlert += a =>
                {
                    try
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                var msg = string.IsNullOrWhiteSpace(a.Plate) ? a.Message : $"[{a.Plate}] {a.Message}";
                                new OverWatchELD.Views.Fleet.FleetToastWindow(msg).Show();
                            }
                            catch { }
                        }));
                    }
                    catch { }
                };
            }
            catch { }

            MaintenanceMalfunctionAlertService.MalfunctionRaised += OnMaintenanceMalfunctionRaised;

            _vtcPollTimer.Interval = TimeSpan.FromSeconds(12);
            _vtcPollTimer.Tick += async (_, __) => await PollVtcAsync();

            _companionStatusTimer.Interval = TimeSpan.FromSeconds(5);
            _companionStatusTimer.Tick += async (_, __) => await RefreshCompanionStatusAsync();

            _updateCheckTimer.Interval = TimeSpan.FromMinutes(10);
            _updateCheckTimer.Tick += async (_, __) => await CheckForUpdateLightAsync();

            PreviewKeyDown += MainWindow_PreviewKeyDown;

            // ✅ Tray setup
            InitializeTrayIcon();
            StateChanged += MainWindow_StateChanged;
        }


        private async void OnlineToggle_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                var isOnline = OnlineToggle?.IsChecked == true;

                OnlinePresenceService.Save(isOnline);

                if (Application.Current is App app)
                {
                    app.EnsureSession();
                    app.Session.IsOnline = isOnline;
                }

                await VtcDriverPresenceSyncService.SyncAsync(isOnline);
            }
            catch { }
        }

        private void AllowCompanionFirewallAccess_Click(object sender, RoutedEventArgs e)
        {
            AllowCompanionFirewallAccess();
        }

        private void EnsureCompanionAccessPin_Click(object sender, RoutedEventArgs e)
        {
            EnsureCompanionAccessPin();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Keep post-login fast: show the dashboard first, then let online checks run in the background.
            // Before this patch, startup waited for VTC polling, Companion health, and update checks before
            // navigating to the dashboard. A slow Railway/API/local health check could make login feel stuck.
            try { NavigateTo("dashboard"); } catch { }

            try { RefreshProfileButtonImage(); } catch { }
            try { RefreshCompanionUrlUi(); } catch { }

            try { _vtcPollTimer.Start(); } catch { }
            try { _companionStatusTimer.Start(); } catch { }
            try { _updateCheckTimer.Start(); } catch { }

            // Start Companion after the UI has rendered so the dashboard is usable immediately.
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { StartCompanionAfterLogin(); } catch { }
                }), DispatcherPriority.Background);
            }
            catch { }

            // Fire-and-forget background refreshes. These methods already protect themselves with in-flight flags.
            _ = PollVtcAsync();
            _ = RefreshCompanionStatusAsync();
            _ = CheckForUpdateLightAsync();
        }

        private void OpenMediaPlayer_Click(object sender, RoutedEventArgs e)
        {
            var win = new OverWatchELD.Views.Media.MediaPlayerWindow
            {
                Owner = Window.GetWindow(this)
            };

            win.Show();
        }

        private void StartCompanionAfterLogin()
        {
            try
            {
                if (OverWatchELD.Services.CompanionApiHostSafe.IsRunning)
                {
                    Debug.WriteLine("[COMPANION] Already running on http://127.0.0.1:" + OverWatchELD.Services.CompanionApiHostSafe.Port + "/");
                    return;
                }

                var app = Application.Current as App;

                try { app?.EnsureSession(); } catch { }

                // Wire app services into Companion before starting it.
                // These are nullable on purpose so Companion still starts even if a service
                // is unavailable in standalone mode.
                OverWatchELD.Services.CompanionApiHostSafe.Configure(
                    telemetryService: app?.Telemetry,
                    dutyService: app?.DutyMachine,
                    inspectionService: null,
                    dispatchInboxService: null,
                    vtcService: null,
                    mainWindowOrShell: this);

                Debug.WriteLine("[COMPANION] Starting local web host after login on http://127.0.0.1:5234/");
                OverWatchELD.Services.CompanionBootstrapSafe.Start();

                Debug.WriteLine(OverWatchELD.Services.CompanionApiHostSafe.IsRunning
                    ? "[COMPANION] Started successfully."
                    : "[COMPANION] Start was requested but host did not report running.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[COMPANION] Failed to start after login: " + ex);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            try { _vtcPollTimer.Stop(); } catch { }
            try { _companionStatusTimer.Stop(); } catch { }
            try { _updateCheckTimer.Stop(); } catch { }

            try
            {
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                    _trayIcon = null;
                }
            }
            catch { }
            try { MaintenanceMalfunctionAlertService.MalfunctionRaised -= OnMaintenanceMalfunctionRaised; } catch { }
            try { _checkEngineFlashTimer?.Stop(); } catch { }
            base.OnClosed(e);
        }
        // =========================================================
        // VTC Branding Logo
        // =========================================================

        public void RefreshVtcBranding()
        {
            try
            {
                bool linked = IsVtcActuallyLinked();

                string name = "Standalone";

                if (linked)
                {
                    try
                    {
                        var cfg = Services.VtcConfigService.Load(true);
                        name = (cfg.VtcName ?? "").Trim();
                    }
                    catch { }

                    if (string.IsNullOrWhiteSpace(name))
                        name = "VTC";
                }

                RefreshVtcBadge(linked ? name : "", linked);
            }
            catch { }
        }

        // =========================================================
        // Check For Updates
        // =========================================================

        private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = await GetLatestUpdateInfoAsync();

                if (!result.Ok)
                {
                    MessageBox.Show(
                        "Could not read the latest update version.",
                        "OverWatch ELD Updates",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );

                    await CheckForUpdateLightAsync();
                    return;
                }

                if (result.UpdateAvailable)
                {
                    var open = MessageBox.Show(
                        $"A new OverWatch ELD update is available.\n\nCurrent: {result.CurrentVersion}\nLatest: {result.LatestVersion}\n\nOpen download page?",
                        "Update Available",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information
                    );

                    if (open == MessageBoxResult.Yes)
                    {
                        OpenUrl(result.DownloadUrl);
                    }
                }
                else
                {
                    MessageBox.Show(
                        $"You are up to date.\n\nVersion: {result.CurrentVersion}",
                        "OverWatch ELD Updates",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                }

                await CheckForUpdateLightAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to check for updates right now.\n\n" + ex.Message,
                    "Update Check Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );

                await CheckForUpdateLightAsync();
            }
        }

        private async Task CheckForUpdateLightAsync()
        {
            if (_updateCheckInFlight)
                return;

            _updateCheckInFlight = true;

            try
            {
                SetUpdateLightChecking();

                var result = await GetLatestUpdateInfoAsync();

                if (!result.Ok)
                {
                    _updateAvailable = false;
                    SetUpdateLightFailed();
                    return;
                }

                _latestUpdateVersion = result.LatestVersion;
                _latestUpdateUrl = string.IsNullOrWhiteSpace(result.DownloadUrl)
                    ? DefaultDownloadUrl
                    : result.DownloadUrl;

                _updateAvailable = result.UpdateAvailable;

                if (_updateAvailable)
                    SetUpdateLightAvailable(result.LatestVersion);
                else
                    SetUpdateLightUpToDate();
            }
            catch
            {
                _updateAvailable = false;
                SetUpdateLightFailed();
            }
            finally
            {
                _updateCheckInFlight = false;
            }
        }

        private async Task<UpdateCheckResult> GetLatestUpdateInfoAsync()
        {
            try
            {
                var currentVersion = System.Reflection.Assembly
                    .GetExecutingAssembly()
                    .GetName()
                    .Version?
                    .ToString() ?? "0.0.0";

                var json = await _updateHttp.GetStringAsync(LatestUpdateApiUrl);

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var latestVersion = root.TryGetProperty("version", out var v)
                    ? v.GetString()
                    : "";

                var downloadUrl = root.TryGetProperty("url", out var u)
                    ? u.GetString()
                    : DefaultDownloadUrl;

                if (string.IsNullOrWhiteSpace(latestVersion))
                {
                    return new UpdateCheckResult
                    {
                        Ok = false,
                        CurrentVersion = currentVersion,
                        LatestVersion = "",
                        DownloadUrl = DefaultDownloadUrl,
                        UpdateAvailable = false
                    };
                }

                var updateAvailable = IsNewerOrDifferentVersion(currentVersion, latestVersion);

                return new UpdateCheckResult
                {
                    Ok = true,
                    CurrentVersion = currentVersion,
                    LatestVersion = latestVersion.Trim(),
                    DownloadUrl = string.IsNullOrWhiteSpace(downloadUrl) ? DefaultDownloadUrl : downloadUrl.Trim(),
                    UpdateAvailable = updateAvailable
                };
            }
            catch
            {
                return new UpdateCheckResult
                {
                    Ok = false,
                    CurrentVersion = "0.0.0",
                    LatestVersion = "",
                    DownloadUrl = DefaultDownloadUrl,
                    UpdateAvailable = false
                };
            }
        }

        private static bool IsNewerOrDifferentVersion(string currentVersion, string latestVersion)
        {
            currentVersion = (currentVersion ?? "").Trim();
            latestVersion = (latestVersion ?? "").Trim();

            if (string.IsNullOrWhiteSpace(currentVersion) || string.IsNullOrWhiteSpace(latestVersion))
                return false;

            if (Version.TryParse(currentVersion, out var current) &&
                Version.TryParse(latestVersion, out var latest))
            {
                return latest > current;
            }

            return !string.Equals(currentVersion, latestVersion, StringComparison.OrdinalIgnoreCase);
        }

        private void SetUpdateLightChecking()
        {
            try
            {
                if (UpdateStatusBox != null && !_updateDismissedThisSession)
                    UpdateStatusBox.Visibility = Visibility.Visible;

                if (CloseUpdateStatusButton != null)
                    CloseUpdateStatusButton.Visibility = Visibility.Collapsed;

                if (UpdateAvailableLight != null)
                {
                    UpdateAvailableLight.Fill = new SolidColorBrush(Color.FromRgb(51, 65, 85));
                    UpdateAvailableLight.Stroke = new SolidColorBrush(Color.FromRgb(100, 116, 139));
                    UpdateAvailableLight.Opacity = 1.0;
                }

                if (UpdateAvailableText != null)
                {
                    UpdateAvailableText.Text = "Updates: Checking...";
                    UpdateAvailableText.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                }

                if (UpdateStatusBox != null)
                {
                    UpdateStatusBox.BorderBrush = new SolidColorBrush(Color.FromRgb(34, 49, 74));
                    UpdateStatusBox.Background = new SolidColorBrush(Color.FromRgb(17, 24, 39));
                    UpdateStatusBox.ToolTip = "Checking for OverWatch ELD updates...";
                }
            }
            catch { }
        }


        private void SetUpdateLightAvailable(string latestVersion)
        {
            try
            {
                if (_updateDismissedThisSession)
                {
                    if (UpdateStatusBox != null)
                        UpdateStatusBox.Visibility = Visibility.Collapsed;

                    return;
                }

                if (UpdateStatusBox != null)
                    UpdateStatusBox.Visibility = Visibility.Visible;

                if (CloseUpdateStatusButton != null)
                    CloseUpdateStatusButton.Visibility = Visibility.Visible;

                if (UpdateAvailableLight != null)
                {
                    UpdateAvailableLight.Fill = new SolidColorBrush(Color.FromRgb(34, 197, 94));
                    UpdateAvailableLight.Stroke = new SolidColorBrush(Color.FromRgb(74, 222, 128));
                    UpdateAvailableLight.Opacity = 1.0;
                }

                if (UpdateAvailableText != null)
                {
                    UpdateAvailableText.Text = $"Update available: {latestVersion}";
                    UpdateAvailableText.Foreground = new SolidColorBrush(Color.FromRgb(134, 239, 172));
                }

                if (UpdateStatusBox != null)
                {
                    UpdateStatusBox.BorderBrush = new SolidColorBrush(Color.FromRgb(34, 197, 94));
                    UpdateStatusBox.Background = new SolidColorBrush(Color.FromRgb(5, 46, 22));
                    UpdateStatusBox.ToolTip = "Click to open the OverWatch ELD download page";
                }
            }
            catch { }
        }


        private void SetUpdateLightUpToDate()
        {
            try
            {
                if (UpdateStatusBox != null && !_updateDismissedThisSession)
                    UpdateStatusBox.Visibility = Visibility.Visible;

                if (CloseUpdateStatusButton != null)
                    CloseUpdateStatusButton.Visibility = Visibility.Collapsed;

                if (UpdateAvailableLight != null)
                {
                    UpdateAvailableLight.Fill = new SolidColorBrush(Color.FromRgb(51, 65, 85));
                    UpdateAvailableLight.Stroke = new SolidColorBrush(Color.FromRgb(100, 116, 139));
                    UpdateAvailableLight.Opacity = 1.0;
                }

                if (UpdateAvailableText != null)
                {
                    UpdateAvailableText.Text = "Updates: Up to date";
                    UpdateAvailableText.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                }

                if (UpdateStatusBox != null)
                {
                    UpdateStatusBox.BorderBrush = new SolidColorBrush(Color.FromRgb(34, 49, 74));
                    UpdateStatusBox.Background = new SolidColorBrush(Color.FromRgb(17, 24, 39));
                    UpdateStatusBox.ToolTip = "OverWatch ELD is up to date";
                }
            }
            catch { }
        }


        private void SetUpdateLightFailed()
        {
            try
            {
                if (UpdateStatusBox != null && !_updateDismissedThisSession)
                    UpdateStatusBox.Visibility = Visibility.Visible;

                if (CloseUpdateStatusButton != null)
                    CloseUpdateStatusButton.Visibility = Visibility.Collapsed;

                if (UpdateAvailableLight != null)
                {
                    UpdateAvailableLight.Fill = new SolidColorBrush(Color.FromRgb(51, 65, 85));
                    UpdateAvailableLight.Stroke = new SolidColorBrush(Color.FromRgb(100, 116, 139));
                    UpdateAvailableLight.Opacity = 1.0;
                }

                if (UpdateAvailableText != null)
                {
                    UpdateAvailableText.Text = "Updates: Unable to check";
                    UpdateAvailableText.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                }

                if (UpdateStatusBox != null)
                {
                    UpdateStatusBox.BorderBrush = new SolidColorBrush(Color.FromRgb(34, 49, 74));
                    UpdateStatusBox.Background = new SolidColorBrush(Color.FromRgb(17, 24, 39));
                    UpdateStatusBox.ToolTip = "Could not check for updates";
                }
            }
            catch { }
        }


        private void CloseUpdateStatusButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                e.Handled = true;
                _updateDismissedThisSession = true;

                if (UpdateStatusBox != null)
                    UpdateStatusBox.Visibility = Visibility.Collapsed;
            }
            catch { }
        }

        private void UpdateStatusBox_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (!_updateAvailable)
                {
                    MessageBox.Show(
                        "OverWatch ELD is up to date.",
                        "Updates",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                    return;
                }

                var result = MessageBox.Show(
                    $"Update {_latestUpdateVersion} is available.\n\nOpen download page?",
                    "Update Available",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information
                );

                if (result == MessageBoxResult.Yes)
                {
                    OpenUrl(string.IsNullOrWhiteSpace(_latestUpdateUrl) ? DefaultDownloadUrl : _latestUpdateUrl);
                }
            }
            catch { }
        }

        private sealed class UpdateCheckResult
        {
            public bool Ok { get; set; }
            public string CurrentVersion { get; set; } = "";
            public string LatestVersion { get; set; } = "";
            public string DownloadUrl { get; set; } = DefaultDownloadUrl;
            public bool UpdateAvailable { get; set; }
        }

        // =========================================================
        // Tray support
        // =========================================================

        private void InitializeTrayIcon()
        {
            try
            {
                if (_trayIcon != null)
                    return;

                _trayIcon = new Forms.NotifyIcon
                {
                    Text = "OverWatch ELD",
                    Visible = false
                };

                try
                {
                    var iconPath = IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
                    if (File.Exists(iconPath))
                        _trayIcon.Icon = new System.Drawing.Icon(iconPath);
                    else
                        _trayIcon.Icon = System.Drawing.SystemIcons.Application;
                }
                catch
                {
                    _trayIcon.Icon = System.Drawing.SystemIcons.Application;
                }

                var menu = new Forms.ContextMenuStrip();

                var openItem = new Forms.ToolStripMenuItem("Open OverWatch ELD");
                openItem.Click += (_, __) => RestoreFromTray();

                var exitItem = new Forms.ToolStripMenuItem("Exit");
                exitItem.Click += (_, __) =>
                {
                    try
                    {
                        _allowRealClose = true;

                        if (_trayIcon != null)
                            _trayIcon.Visible = false;

                        Close();
                    }
                    catch
                    {
                        Environment.Exit(0);
                    }
                };

                menu.Items.Add(openItem);
                menu.Items.Add(new Forms.ToolStripSeparator());
                menu.Items.Add(exitItem);

                _trayIcon.ContextMenuStrip = menu;
                _trayIcon.DoubleClick += (_, __) => RestoreFromTray();
            }
            catch
            {
            }
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            try
            {
                if (WindowState == WindowState.Minimized)
                    MinimizeToTray();
            }
            catch
            {
            }
        }

        private void MinimizeToTray()
        {
            try
            {
                InitializeTrayIcon();

                ShowInTaskbar = false;

                if (_trayIcon != null)
                {
                    _trayIcon.Visible = true;
                    _trayIcon.BalloonTipTitle = "OverWatch ELD";
                    _trayIcon.BalloonTipText = "Running in system tray.";
                    _trayIcon.ShowBalloonTip(1500);
                }

                Hide();
            }
            catch
            {
            }
        }

        private void RestoreFromTray()
        {
            try
            {
                Show();
                ShowInTaskbar = true;
                WindowState = WindowState.Normal;
                Activate();

                if (_trayIcon != null)
                    _trayIcon.Visible = false;
            }
            catch
            {
            }
        }

        // =========================================================
        // Companion URL / QR / Copy / Status
        // =========================================================

        private string GetCompanionUrl()
        {
            try
            {
                var urls = OverWatchELD.Services.CompanionApiHostSafe.GetReachableUrls();

                var bestLanUrl = urls
                    .FirstOrDefault(x =>
                        x.StartsWith("http://192.168.", StringComparison.OrdinalIgnoreCase) ||
                        x.StartsWith("http://10.", StringComparison.OrdinalIgnoreCase) ||
                        x.StartsWith("http://172.", StringComparison.OrdinalIgnoreCase))
                    ?? urls.FirstOrDefault(x => !x.Contains("127.0.0.1") && !x.Contains("localhost"))
                    ?? $"http://127.0.0.1:{OverWatchELD.Services.CompanionApiHostSafe.Port}/";

                return bestLanUrl.TrimEnd('/');
            }
            catch
            {
                try
                {
                    var ip = GetLanIpOrLocalhost();
                    return $"http://{ip}:{OverWatchELD.Services.CompanionApiHostSafe.Port}";
                }
                catch
                {
                    return "http://127.0.0.1:5234";
                }
            }
        }

        private static string GetLanIpOrLocalhost()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());

                var ip = host.AddressList
                    .FirstOrDefault(a =>
                        a.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(a));

                return ip?.ToString() ?? "127.0.0.1";
            }
            catch
            {
                return "127.0.0.1";
            }
        }

        private void RefreshCompanionUrlUi()
        {
            try
            {
                if (CompanionUrlText != null)
                    CompanionUrlText.Text = GetCompanionUrl();
            }
            catch { }
        }

        private void RefreshCompanionStatusUi()
        {
            try
            {
                var isOnline = IsCompanionOnline();

                if (CompanionStatusText != null)
                {
                    CompanionStatusText.Text = isOnline ? "🟢 Companion Online" : "🔴 Companion Offline";
                    CompanionStatusText.Foreground = isOnline
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#86EFAC"))
                        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FCA5A5"));
                }

                RefreshCompanionUrlUi();
            }
            catch
            {
                try
                {
                    if (CompanionStatusText != null)
                    {
                        CompanionStatusText.Text = "🔴 Companion Offline";
                        CompanionStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FCA5A5"));
                    }
                }
                catch { }
            }
        }

        private bool IsCompanionOnline()
        {
            try
            {
                using var client = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(2)
                };

                var url = $"http://127.0.0.1:{OverWatchELD.Services.CompanionApiHostSafe.Port}/health";
                var response = client.GetAsync(url).GetAwaiter().GetResult();
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private async Task RefreshCompanionStatusAsync()
        {
            try
            {
                var urls = OverWatchELD.Services.CompanionApiHostSafe.GetReachableUrls();
                var localhostHealth = $"http://127.0.0.1:{OverWatchELD.Services.CompanionApiHostSafe.Port}/health";

                using var resp = await _companionHttp.GetAsync(localhostHealth);
                var ok = resp.IsSuccessStatusCode;

                string bestLanUrl = urls
                    .FirstOrDefault(x =>
                        x.StartsWith("http://192.168.", StringComparison.OrdinalIgnoreCase) ||
                        x.StartsWith("http://10.", StringComparison.OrdinalIgnoreCase) ||
                        x.StartsWith("http://172.", StringComparison.OrdinalIgnoreCase))
                    ?? urls.FirstOrDefault(x => !x.Contains("127.0.0.1") && !x.Contains("localhost"))
                    ?? localhostHealth.Replace("/health", "/");

                if (ok)
                {
                    if (CompanionStatusText != null)
                        CompanionStatusText.Text = "🟢 Companion Online";

                    if (CompanionStatusText != null)
                        CompanionStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#86EFAC"));

                    if (CompanionUrlText != null)
                        CompanionUrlText.Text = bestLanUrl.TrimEnd('/');
                }
                else
                {
                    if (CompanionStatusText != null)
                        CompanionStatusText.Text = "🔴 Companion Offline";

                    if (CompanionStatusText != null)
                        CompanionStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FCA5A5"));

                    if (CompanionUrlText != null)
                        CompanionUrlText.Text = bestLanUrl.TrimEnd('/');
                }
            }
            catch
            {
                try
                {
                    var urls = OverWatchELD.Services.CompanionApiHostSafe.GetReachableUrls();
                    string bestLanUrl = urls
                        .FirstOrDefault(x =>
                            x.StartsWith("http://192.168.", StringComparison.OrdinalIgnoreCase) ||
                            x.StartsWith("http://10.", StringComparison.OrdinalIgnoreCase) ||
                            x.StartsWith("http://172.", StringComparison.OrdinalIgnoreCase))
                        ?? $"http://127.0.0.1:{OverWatchELD.Services.CompanionApiHostSafe.Port}/";

                    if (CompanionStatusText != null)
                        CompanionStatusText.Text = "🔴 Companion Offline";

                    if (CompanionStatusText != null)
                        CompanionStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FCA5A5"));

                    if (CompanionUrlText != null)
                        CompanionUrlText.Text = bestLanUrl.TrimEnd('/');
                }
                catch
                {
                }
            }
        }

        private void ShowCompanionQr_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var url = GetCompanionUrl();
                var win = new OverWatchELD.Views.CompanionQrWindow(url)
                {
                    Owner = this
                };
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to open Companion QR.\n\n" + ex.Message,
                    "OverWatch ELD",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void CompanionUrlText_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            CopyCompanionUrlToClipboard();
        }

        private void CopyCompanionUrl_Click(object sender, RoutedEventArgs e)
        {
            CopyCompanionUrlToClipboard();
        }

        private void CopyCompanionUrlToClipboard()
        {
            try
            {
                var url = GetCompanionUrl();
                Clipboard.SetText(url);

                MessageBox.Show(
                    "Companion link copied:\n\n" + url,
                    "OverWatch ELD",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Failed to copy companion link.\n\n" + ex.Message,
                    "OverWatch ELD",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        // =========================================================
        // Companion firewall + basic security upgrade
        // =========================================================

        private void AllowCompanionFirewallAccess()
        {
            try
            {
                var port = OverWatchELD.Services.CompanionApiHostSafe.Port;
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"advfirewall firewall add rule name=\"OverWatch ELD Companion\" dir=in action=allow protocol=TCP localport={port}",
                    Verb = "runas",
                    CreateNoWindow = true,
                    UseShellExecute = true
                };

                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Could not add firewall rule automatically.\n\n" + ex.Message,
                    "OverWatch ELD",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private string GetCompanionSecurityFilePath()
        {
            try
            {
                var dir = IOPath.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OverWatchELD");
                Directory.CreateDirectory(dir);
                return IOPath.Combine(dir, "companion.security.json");
            }
            catch
            {
                return IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "companion.security.json");
            }
        }

        private void EnsureCompanionAccessPin()
        {
            try
            {
                var path = GetCompanionSecurityFilePath();
                if (File.Exists(path))
                    return;

                var pin = new Random().Next(100000, 999999).ToString();
                var json = JsonSerializer.Serialize(new { pin }, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);

                MessageBox.Show(
                    "Companion security PIN created.\n\nPIN: " + pin + "\n\nSave this for phone access.",
                    "OverWatch ELD",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch { }
        }

        // =========================================================
        // Hotkeys
        // =========================================================

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

                if (!ctrl || !shift)
                    return;

                switch (e.Key)
                {
                    case Key.M:
                        WindowState = WindowState.Minimized;
                        e.Handled = true;
                        return;

                    case Key.X:
                        _allowRealClose = true;
                        Close();
                        e.Handled = true;
                        return;

                    case Key.D:
                        NavigateTo("dashboard");
                        e.Handled = true;
                        return;

                    case Key.G:
                        NavigateTo("dispatch");
                        e.Handled = true;
                        return;

                    case Key.V:
                        if (_vtcUiEnabled || IsVtcActuallyLinked())
                            NavigateTo("vtc");
                        e.Handled = true;
                        return;

                    case Key.F:
                        NavigateTo("fleet");
                        e.Handled = true;
                        return;

                    case Key.P:
                        NavigateTo("performance");
                        e.Handled = true;
                        return;

                    case Key.L:
                        NavigateTo("logs");
                        e.Handled = true;
                        return;

                    case Key.I:
                        NavigateTo("inspections");
                        e.Handled = true;
                        return;

                    case Key.C:
                        NavigateTo("compliance");
                        e.Handled = true;
                        return;

                    case Key.S:
                        NavSettings_Click(this, new RoutedEventArgs());
                        e.Handled = true;
                        return;

                    case Key.U:
                        NavigateTo("support");
                        e.Handled = true;
                        return;

                    case Key.O:
                        NavigateTo("documents");
                        e.Handled = true;
                        return;
                }
            }
            catch { }
        }

        // =========================================================
        // Profile
        // =========================================================

        private void ProfileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var pairing = Services.VtcPairingStore.Load();

                var driverName = FirstNonEmpty(
                    GetProp(pairing, "DriverName"),
                    GetProp(pairing, "DisplayName"),
                    GetProp(pairing, "UserName"),
                    GetProp(pairing, "DiscordUsername"));

                var discordName = FirstNonEmpty(
                    GetProp(pairing, "DiscordUsername"),
                    GetProp(pairing, "UserName"),
                    GetProp(pairing, "DriverName"));

                var discordId = FirstNonEmpty(
                    GetProp(pairing, "DiscordUserId"),
                    GetProp(pairing, "UserId"));

                var truckNumber = FirstNonEmpty(
                    GetProp(pairing, "TruckNumber"),
                    GetProp(pairing, "TruckId"));

                var win =
                    CreateProfileWindowWithStrings(discordId, driverName, discordName, truckNumber)
                    ?? CreateProfileWindowParameterless();

                if (win == null)
                {
                    MessageBox.Show(
                        "Could not open the profile window because the existing DriverProfileView constructor did not match the current call pattern.",
                        "Profile",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                win.Owner = this;
                win.ShowDialog();

                RefreshProfileButtonImage();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Profile", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshProfileButtonImage()
        {
            try
            {
                var pairing = Services.VtcPairingStore.Load();

                var discordId = FirstNonEmpty(
                    GetProp(pairing, "DiscordUserId"),
                    GetProp(pairing, "UserId"));

                var path = GetProfileImagePath(discordId);

                var hasImage = !string.IsNullOrWhiteSpace(path) && File.Exists(path);

                if (ProfileButtonImageEllipse != null)
                    ProfileButtonImageEllipse.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;

                if (ProfileButtonFallbackText != null)
                    ProfileButtonFallbackText.Visibility = hasImage ? Visibility.Collapsed : Visibility.Visible;

                if (!hasImage)
                {
                    if (ProfileButtonImageEllipse != null)
                        ProfileButtonImageEllipse.Fill = null;
                    return;
                }

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path!, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();

                var brush = new ImageBrush(bmp)
                {
                    Stretch = Stretch.UniformToFill
                };

                if (ProfileButtonImageEllipse != null)
                    ProfileButtonImageEllipse.Fill = brush;
            }
            catch
            {
                try
                {
                    if (ProfileButtonImageEllipse != null)
                    {
                        ProfileButtonImageEllipse.Fill = null;
                        ProfileButtonImageEllipse.Visibility = Visibility.Collapsed;
                    }

                    if (ProfileButtonFallbackText != null)
                        ProfileButtonFallbackText.Visibility = Visibility.Visible;
                }
                catch { }
            }
        }

        private static string GetProfileImagePath(string driverId)
        {
            try
            {
                var safeId = Sanitize(driverId);

                var profilePath = IOPath.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Config",
                    "DriverProfiles",
                    $"{safeId}.json");

                if (!File.Exists(profilePath))
                    return "";

                var json = File.ReadAllText(profilePath);
                var data = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, string>>(json, _vtcJson);

                if (data == null)
                    return "";

                if (data.TryGetValue("ProfileImagePath", out var img))
                    return (img ?? "").Trim();

                return "";
            }
            catch
            {
                return "";
            }
        }

        private static Window? CreateProfileWindowWithStrings(string driverId, string driverName, string discordName, string truckNumber)
        {
            try
            {
                var t = typeof(OverWatchELD.Views.DriverProfileView);
                var ctor = t.GetConstructor(new[]
                {
                    typeof(string),
                    typeof(string),
                    typeof(string),
                    typeof(string)
                });

                if (ctor == null)
                    return null;

                return ctor.Invoke(new object[]
                {
                    driverId ?? "",
                    driverName ?? "",
                    discordName ?? "",
                    truckNumber ?? ""
                }) as Window;
            }
            catch
            {
                return null;
            }
        }

        private static Window? CreateProfileWindowParameterless()
        {
            try
            {
                return new OverWatchELD.Views.DriverProfileView();
            }
            catch
            {
                return null;
            }
        }

        private static string GetProp(object? obj, string name)
        {
            try
            {
                var p = obj?.GetType().GetProperty(name);
                return (p?.GetValue(obj)?.ToString() ?? "").Trim();
            }
            catch
            {
                return "";
            }
        }

        private static string FirstNonEmpty(params string[] values)
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
            foreach (var c in System.IO.Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');
            value = value.Trim();
            return string.IsNullOrWhiteSpace(value) ? "default" : value;
        }

        // =========================================================
        // VTC
        // =========================================================

        private async Task PollVtcAsync()
        {
            if (_vtcPollInFlight) return;
            _vtcPollInFlight = true;

            try
            {
                bool linked = IsVtcActuallyLinked();
                string name = "";

                if (linked)
                {
                    try
                    {
                        name = await TryGetVtcNameFromBotAsync();
                    }
                    catch
                    {
                        name = "";
                    }

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        try
                        {
                            var pairing = Services.VtcPairingStore.Load();
                            name = (pairing?.VtcName ?? "").Trim();
                        }
                        catch { }

                        if (string.IsNullOrWhiteSpace(name))
                        {
                            try
                            {
                                var cfg = Services.VtcConfigService.Load();
                                name = (cfg.VtcName ?? "").Trim();
                            }
                            catch { }
                        }
                    }
                }

                _vtcUiEnabled = linked;

                RefreshVtcBadge(linked ? name : "", linked);
                ApplyVtcUiState(linked);
            }
            catch
            {
                bool linked = false;

                try
                {
                    linked = IsVtcActuallyLinked();
                }
                catch { }

                _vtcUiEnabled = linked;

                try
                {
                    string fallbackName = "";

                    if (linked)
                    {
                        try
                        {
                            var pairing = Services.VtcPairingStore.Load();
                            fallbackName = (pairing?.VtcName ?? "").Trim();
                        }
                        catch { }

                        if (string.IsNullOrWhiteSpace(fallbackName))
                        {
                            try
                            {
                                var cfg = Services.VtcConfigService.Load();
                                fallbackName = (cfg.VtcName ?? "").Trim();
                            }
                            catch { }
                        }
                    }

                    RefreshVtcBadge(linked ? fallbackName : "", linked);
                    ApplyVtcUiState(linked);
                }
                catch { }
            }
            finally
            {
                _vtcPollInFlight = false;
            }
        }

        private void RefreshVtcBadge(string? vtcName, bool connected)
        {
            try
            {
                var label = connected && !string.IsNullOrWhiteSpace(vtcName)
                    ? vtcName!.Trim()
                    : "Standalone";

                if (VtcBadgeTextBlock != null)
                    VtcBadgeTextBlock.Text = label;

                if (VtcBadgeFallbackTextBlock != null)
                    VtcBadgeFallbackTextBlock.Text = GetBadgeInitials(label);

                ApplyVtcBadgeIcon(connected ? GetVtcIconPath() : "");
            }
            catch
            {
                try
                {
                    if (VtcBadgeTextBlock != null)
                        VtcBadgeTextBlock.Text = connected && !string.IsNullOrWhiteSpace(vtcName) ? vtcName!.Trim() : "Standalone";

                    ApplyVtcBadgeIcon("");
                }
                catch { }
            }
        }

        private void ApplyVtcBadgeIcon(string? iconPath)
        {
            try
            {
                var hasIcon = !string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath);

                if (VtcBadgeIconImage != null)
                    VtcBadgeIconImage.Visibility = hasIcon ? Visibility.Visible : Visibility.Collapsed;

                if (VtcBadgeFallbackTextBlock != null)
                    VtcBadgeFallbackTextBlock.Visibility = hasIcon ? Visibility.Collapsed : Visibility.Visible;

                if (VtcBadgeFallbackBorder != null)
                    VtcBadgeFallbackBorder.Visibility = hasIcon ? Visibility.Collapsed : Visibility.Visible;

                if (!hasIcon)
                {
                    if (VtcBadgeIconImage != null)
                        VtcBadgeIconImage.Source = null;

                    return;
                }

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(iconPath!, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();

                if (VtcBadgeIconImage != null)
                {
                    VtcBadgeIconImage.Source = bmp;
                    VtcBadgeIconImage.Clip = new EllipseGeometry(new Rect(0, 0, 26, 26));
                }
            }
            catch
            {
                try
                {
                    if (VtcBadgeIconImage != null)
                    {
                        VtcBadgeIconImage.Source = null;
                        VtcBadgeIconImage.Visibility = Visibility.Collapsed;
                    }

                    if (VtcBadgeFallbackTextBlock != null)
                        VtcBadgeFallbackTextBlock.Visibility = Visibility.Visible;

                    if (VtcBadgeFallbackBorder != null)
                        VtcBadgeFallbackBorder.Visibility = Visibility.Visible;
                }
                catch { }
            }
        }

        private static string GetBadgeInitials(string? text)
        {
            var value = (text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value))
                return "VT";

            var parts = value
                .Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(p => !string.Equals(p, "VTC", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (parts.Length == 0)
                return value.Length >= 2 ? value[..2].ToUpperInvariant() : value.ToUpperInvariant();

            if (parts.Length == 1)
                return parts[0].Length >= 2 ? parts[0][..2].ToUpperInvariant() : parts[0].ToUpperInvariant();

            return string.Concat(parts[0][0], parts[1][0]).ToUpperInvariant();
        }

        private static string GetVtcIconPath()
        {
            try
            {
                var branding = LoadBrandingConfig();
                var iconPath = (branding.IconImagePath ?? "").Trim();
                return File.Exists(iconPath) ? iconPath : "";
            }
            catch
            {
                return "";
            }
        }

        private static string GetBrandingRootDirectory()
        {
            var cfg = Services.VtcConfigService.Load(forceReload: true);
            var pairing = Services.VtcPairingStore.Load();
            var guildId = (pairing?.GuildId ?? cfg.Discord?.GuildId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(guildId))
                guildId = "default";

            var dir = IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "VtcBranding", guildId);
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string GetBrandingConfigPath()
        {
            return IOPath.Combine(GetBrandingRootDirectory(), "branding.json");
        }

        private static VtcBrandingConfig LoadBrandingConfig()
        {
            try
            {
                var path = GetBrandingConfigPath();
                if (!File.Exists(path))
                    return new VtcBrandingConfig();

                var json = File.ReadAllText(path);
                var data = JsonSerializer.Deserialize<VtcBrandingConfig>(json, _vtcJson);
                return data ?? new VtcBrandingConfig();
            }
            catch
            {
                return new VtcBrandingConfig();
            }
        }

        private void ApplyVtcUiState(bool showVtc)
        {
            try
            {
                if (NavVtcBtn != null)
                    NavVtcBtn.Visibility = showVtc ? Visibility.Visible : Visibility.Collapsed;

                SetVtcUiVisibility(showVtc);

                if (!showVtc)
                {
                    try
                    {
                        var typeName = MainContent?.Content?.GetType()?.FullName ?? "";
                        if (typeName.Contains("Vtc", StringComparison.OrdinalIgnoreCase))
                            NavigateTo("dashboard");
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void SetVtcUiVisibility(bool connected)
        {
            var vis = connected ? Visibility.Visible : Visibility.Collapsed;

            string[] names =
            {
                "NavVtcBtn",
                "VtcTabItem",
                "TabVtc",
                "VtcTab",
                "VtcMenuItem",
                "NavVtc",
                "BtnVtc",
                "MenuVtc"
            };

            foreach (var n in names)
            {
                try
                {
                    if (FindName(n) is UIElement el)
                        el.Visibility = vis;
                }
                catch { }
            }
        }

        private static bool IsVtcActuallyLinked()
        {
            try
            {
                var cfg = OverWatchELD.Services.VtcConfigService.Load();
                var baseUrl = (cfg.BotApiBaseUrl ?? "").Trim().TrimEnd('/');
                var gid = (cfg.Discord?.GuildId ?? "").Trim();
                var pairing = Services.VtcPairingStore.Load();
                var pairGid = (pairing?.GuildId ?? "").Trim();

                if (string.IsNullOrWhiteSpace(baseUrl))
                    return false;

                if (!string.IsNullOrWhiteSpace(pairGid))
                    return true;

                return !string.IsNullOrWhiteSpace(gid);
            }
            catch
            {
                return false;
            }
        }

        private static async Task<string> TryGetVtcNameFromBotAsync()
        {
            var cfg = OverWatchELD.Services.VtcConfigService.Load();
            var pairing = Services.VtcPairingStore.Load();
            var gid = (pairing?.GuildId ?? cfg.Discord?.GuildId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(BotBase) || string.IsNullOrWhiteSpace(gid))
                return "";

            var url = BotBase.TrimEnd('/') + "/api/vtc/name?guildId=" + Uri.EscapeDataString(gid);

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Accept.Clear();
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var resp = await _vtcHttp.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return "";

            var txt = (await resp.Content.ReadAsStringAsync())?.Trim() ?? "";
            return ExtractNameFromBotNameResponse(txt);
        }

        private static string ExtractNameFromBotNameResponse(string txt)
        {
            if (string.IsNullOrWhiteSpace(txt)) return "";

            var s = txt.Trim();
            if (!s.StartsWith("{")) return s;

            try
            {
                using var doc = JsonDocument.Parse(s);
                var root = doc.RootElement;

                string GetString(params string[] keys)
                {
                    foreach (var k in keys)
                    {
                        if (root.TryGetProperty(k, out var p) && p.ValueKind == JsonValueKind.String)
                            return p.GetString() ?? "";
                    }
                    return "";
                }

                var name = GetString("vtcShort", "shortName", "short", "abbr");
                if (string.IsNullOrWhiteSpace(name))
                    name = GetString("vtcName", "name", "serverName", "displayName", "text", "label", "title", "value");

                return (name ?? "").Trim();
            }
            catch
            {
                return "";
            }
        }

        // =========================================================
        // Navigation / click handlers required by XAML
        // =========================================================

        public void NavigateTo(string route)
        {
            route = (route ?? "").Trim().ToLowerInvariant();

            switch (route)
            {
                case "dashboard":
                    SetMainContentByType("OverWatchELD.Views.DashboardView");
                    break;

                case "logs":
                    ShowLogsWindow();
                    break;

                case "messages":
                    SetMainContentByType("OverWatchELD.Views.MessagesView");
                    break;

                case "performance":
                    SetMainContentByType("OverWatchELD.Views.DriverPerformanceView");
                    break;

                case "dispatch":
                    SetMainContentByType("OverWatchELD.Views.DispatchInboxTabView");
                    break;

                case "inspections":
                    SetMainContentByType("OverWatchELD.Views.InspectionsView");
                    break;

                case "compliance":
                    SetMainContentByType("OverWatchELD.Views.ComplianceView");
                    break;

                case "maintenance":
                    SetMainContentByType("OverWatchELD.Views.MaintenanceHubView");
                    break;

                case "vtc":
                    SetMainContentByType("OverWatchELD.Views.VtcHomeView");
                    break;

                case "fleet":
                case "vtc-fleet":
                    try
                    {
                        var view = new OverWatchELD.Views.Fleet.FleetView();
                        view.DataContext = new FleetCommandCenterViewModel();
                        MainContent.Content = view;
                    }
                    catch (Exception ex)
                    {
                        SetContentFallback($"Failed to load Fleet.\n{ex.GetType().Name}: {ex.Message}");
                    }
                    break;

                case "fleet-command":
                case "fleet-command-center":
                    try
                    {
                        var win = new OverWatchELD.Views.FleetCommandCenterWindow
                        {
                            Owner = this
                        };
                        win.ShowDialog();
                    }
                    catch (Exception ex)
                    {
                        SetContentFallback($"Failed to load Fleet Command Center.\n{ex.GetType().Name}: {ex.Message}");
                    }
                    break;

                case "vtc-roster":
                    MainContent.Content = new Views.VtcRosterView();
                    break;

                case "vtc-performance":
                    MainContent.Content = new Views.VtcHomeView();
                    break;

                case "support":
                    SetMainContentByType("OverWatchELD.Views.SupportView");
                    break;

                case "documents":
                    SetMainContentByType("OverWatchELD.Views.DocumentsView");
                    break;

                default:
                    SetContentFallback($"Unknown route: {route}");
                    break;
            }
        }


        private void AddTruck_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new OverWatchELD.Views.Fleet.FleetTruckTelemetryImportWindow
                {
                    Owner = this
                };

                if (win.ShowDialog() == true)
                {
                    MessageBox.Show(
                        "Truck submitted with a Pending badge. It must be approved by an admin before it becomes a fleet truck.",
                        "Add Truck",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to open Add Truck.\n\n" + ex.Message,
                    "Add Truck",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        public void NavDashboard_Click(object sender, RoutedEventArgs e) => NavigateTo("dashboard");
        public void NavLogs_Click(object sender, RoutedEventArgs e) => NavigateTo("logs");
        public void NavPerformance_Click(object sender, RoutedEventArgs e) => NavigateTo("performance");
        public void NavMessage_Click(object sender, RoutedEventArgs e) => NavigateTo("messages");
        public void NavInspections_Click(object sender, RoutedEventArgs e) => NavigateTo("inspections");
        public void NavCompliance_Click(object sender, RoutedEventArgs e) => NavigateTo("compliance");
        public void NavMaintenance_Click(object sender, RoutedEventArgs e) => NavigateTo("maintenance");
        public void NavVtc_Click(object sender, RoutedEventArgs e) => NavigateTo("vtc");
        public void NavSupport_Click(object sender, RoutedEventArgs e) => NavigateTo("support");
        public void NavDocuments_Click(object sender, RoutedEventArgs e) => NavigateTo("documents");

        private void NavDispatch_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MainContent.Content = new OverWatchELD.Views.DispatchView();
            }
            catch
            {
                NavigateTo("dispatch");
            }
        }

        public void NavSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new Views.SettingsWindow();
                win.Owner = this;
                win.ShowDialog();

                RefreshProfileButtonImage();
                RefreshCompanionUrlUi();
                RefreshCompanionStatusUi();
            }
            catch { }
        }

        private void ExportLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ShowDailyLogsExportWindow();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.GetBaseException().Message, "Export Logs", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportInspections_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new OverWatchELD.Views.InspectionHistoryWindow
                {
                    Owner = this
                };

                win.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to open inspection history.\n\n" + ex.Message,
                    "Inspections",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        public void ShowLogsWindow()
        {
            try
            {
                SetMainContentByType("OverWatchELD.Views.LogsView");
            }
            catch { }
        }

        public void ShowMaintenanceWindow()
        {
            try
            {
                foreach (Window w in Application.Current.Windows)
                {
                    if (w is OverWatchELD.Views.MaintenanceWindow existing)
                    {
                        if (!existing.IsVisible) existing.Show();
                        existing.Activate();
                        return;
                    }
                }

                var win = new OverWatchELD.Views.MaintenanceWindow { Owner = this };
                win.Show();
                win.Activate();
            }
            catch { }
        }

        public void ShowDailyLogsExportWindow()
        {
            try
            {
                foreach (Window w in Application.Current.Windows)
                {
                    if (w is OverWatchELD.Views.DailyLogsExportWindow existing)
                    {
                        if (!existing.IsVisible) existing.Show();
                        existing.Activate();
                        return;
                    }
                }

                var win = new OverWatchELD.Views.DailyLogsExportWindow { Owner = this };
                win.Show();
                win.Activate();
            }
            catch { }
        }

        public void SetMainContentByType(string fullTypeName)
        {
            SetMainContentByTypeInternal(fullTypeName);
        }

        private void SetMainContentByTypeInternal(string fullTypeName)
        {
            try
            {
                var asmName = typeof(MainWindow).Assembly.FullName;
                var t =
                    Type.GetType($"{fullTypeName}, {asmName}", throwOnError: false)
                    ?? Type.GetType(fullTypeName, throwOnError: false);

                if (t == null)
                {
                    SetContentFallback($"Missing view: {fullTypeName}");
                    return;
                }

                var instance = Activator.CreateInstance(t);
                MainContent.Content = instance;
            }
            catch (Exception ex)
            {
                SetContentFallback($"Failed to load view.\n{ex.GetType().Name}: {ex.Message}");
            }
        }

        // ✅ X button fully exits. Minimize goes to tray.
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                if (!_allowRealClose)
                {
                    try
                    {
                        if (_trayIcon != null)
                            _trayIcon.Visible = false;
                    }
                    catch { }

                    base.OnClosing(e);
                    System.Windows.Application.Current.Shutdown();
                    Environment.Exit(0);
                    return;
                }

                try
                {
                    if (_trayIcon != null)
                    {
                        _trayIcon.Visible = false;
                        _trayIcon.Dispose();
                        _trayIcon = null;
                    }
                }
                catch { }
            }
            catch { }

            base.OnClosing(e);
            System.Windows.Application.Current.Shutdown();
            Environment.Exit(0);
        }

        // =========================================================
        // Portal / Manage Buttons
        // =========================================================

        private void OpenPortal_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cfg = OverWatchELD.Services.VtcConfigService.Load(true);
                var guildId = GetCurrentGuildId(cfg);
                OpenUrl(BuildUrlWithGuild(PortalUrl, guildId));
            }
            catch
            {
                OpenUrl(PortalUrl);
            }
        }

        private void OpenManage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cfg = OverWatchELD.Services.VtcConfigService.Load(true);
                var guildId = GetCurrentGuildId(cfg);

                if (string.IsNullOrWhiteSpace(guildId))
                {
                    MessageBox.Show(
                        "No linked VTC was found. Link this ELD to a VTC before opening Manage VTC.",
                        "OverWatch ELD",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    OpenUrl(PortalUrl);
                    return;
                }

                // Do not block locally. The web app is the source of truth for admin/manager access
                // through /api/auth/vtcs, so open manage.html with the exact guildId and let the
                // webpage allow Admin / Manager / Owner users and deny regular drivers.
                OpenUrl(BuildUrlWithGuild(ManageUrl, guildId));
            }
            catch
            {
                OpenUrl(ManageUrl);
            }
        }

        private static string BuildUrlWithGuild(string baseUrl, string? guildId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(guildId))
                    return baseUrl;

                var separator = baseUrl.Contains("?", StringComparison.Ordinal) ? "&" : "?";
                return baseUrl + separator + "guildId=" + Uri.EscapeDataString(guildId.Trim());
            }
            catch
            {
                return baseUrl;
            }
        }

        private static string GetCurrentGuildId(object? cfg)
        {
            try
            {
                var fromCfg = GetConfigStringProperty(cfg, "GuildId");
                if (!string.IsNullOrWhiteSpace(fromCfg))
                    return fromCfg;

                var discord = cfg?.GetType().GetProperty("Discord")?.GetValue(cfg);
                var fromDiscord = GetConfigStringProperty(discord, "GuildId");
                if (!string.IsNullOrWhiteSpace(fromDiscord))
                    return fromDiscord;

                var identity = new OverWatchELD.Services.DiscordIdentityService().LoadOrDefault();
                var fromIdentity = GetConfigStringProperty(identity, "GuildId");
                if (!string.IsNullOrWhiteSpace(fromIdentity))
                    return fromIdentity;

                if (Application.Current is App app)
                {
                    var fromSession = Convert.ToString(app.Session?.GuildId) ?? "";
                    if (!string.IsNullOrWhiteSpace(fromSession))
                        return fromSession.Trim();
                }
            }
            catch { }

            return "";
        }

        private static string GetCurrentVtcRole(object? cfg)
        {
            try
            {
                var role = GetConfigStringProperty(cfg, "LinkedUserRole", "UserRole", "Role", "VtcRole", "DiscordRole");
                if (!string.IsNullOrWhiteSpace(role))
                    return role;

                var identity = new OverWatchELD.Services.DiscordIdentityService().LoadOrDefault();
                role = GetConfigStringProperty(identity, "LinkedUserRole", "UserRole", "Role", "VtcRole", "DiscordRole");
                if (!string.IsNullOrWhiteSpace(role))
                    return role;

                if (Application.Current is App app)
                {
                    role = Convert.ToString(app.Session?.LinkedUserRole) ?? "";
                    if (!string.IsNullOrWhiteSpace(role))
                        return role.Trim();

                    role = Convert.ToString(app.Session?.DriverRole) ?? "";
                    if (!string.IsNullOrWhiteSpace(role))
                        return role.Trim();
                }
            }
            catch { }

            return "";
        }

        private static bool IsVtcAdminRole(string? role)
        {
            role = (role ?? "").Trim().ToLowerInvariant();

            return role == "owner"
                || role == "admin"
                || role == "administrator"
                || role == "manager"
                || role == "management"
                || role == "founder"
                || role.Contains("owner")
                || role.Contains("admin")
                || role.Contains("manager")
                || role.Contains("management");
        }

        private static string GetConfigStringProperty(object? obj, params string[] names)
        {
            try
            {
                if (obj == null)
                    return "";

                foreach (var name in names)
                {
                    var prop = obj.GetType().GetProperty(name);
                    var value = prop?.GetValue(obj)?.ToString();

                    if (!string.IsNullOrWhiteSpace(value))
                        return value.Trim();
                }
            }
            catch { }

            return "";
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void SetContentFallback(string msg)
        {
            MainContent.Content = new Border
            {
                Padding = new Thickness(18),
                Child = new TextBlock
                {
                    Text = msg,
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap
                }
            };
        }
    }
}