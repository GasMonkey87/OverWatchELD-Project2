using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using OverWatchELD.Services;

namespace OverWatchELD.Views
{
    public partial class LiveMapWindow : Window
    {
        private string _botBaseUrl = "";
        private string _guildId = "0";
        private bool _initialized;

        public LiveMapWindow()
        {
            InitializeComponent();
            Loaded += LiveMapWindow_Loaded;
        }

        private async void LiveMapWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                await LoadConfigAsync();

                if (string.IsNullOrWhiteSpace(_botBaseUrl))
                {
                    ShowWarning(
                        "VTC Bot URL is not set yet.\n\nGo to Settings → VTC and set your Bot API Base URL (Railway).");
                    return;
                }

                var liveMapRoot = GetLiveMapRoot();
                var htmlPath = Path.Combine(liveMapRoot, "index.html");

                if (!File.Exists(htmlPath))
                {
                    ShowError(
                        "Missing LiveMap/index.html in output.\n\nMake sure the LiveMap folder is copied to the build output.");
                    return;
                }

                await EnsureWebViewReadyAsync();

                MapWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "appassets.overwatcheld",
                    liveMapRoot,
                    CoreWebView2HostResourceAccessKind.Allow);

                var assets = LoadAssetState(liveMapRoot);

                ConfigureWebViewSettings();

                MapWebView.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
                MapWebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;

                _pendingBootstrap = assets;
                MapWebView.Source = new Uri("https://appassets.overwatcheld/index.html");
            }
            catch (Exception ex)
            {
                ShowError("Live map failed to start:\n" + ex.Message);
            }
        }

        private AssetBootstrapState? _pendingBootstrap;

        private async void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess || _pendingBootstrap == null)
                return;

            try
            {
                var cfg = new
                {
                    botBaseUrl = _botBaseUrl?.Trim(),
                    guildId = _guildId?.Trim(),
                    hasAtsPmtiles = _pendingBootstrap.HasAtsPmtiles,
                    atsPmtilesUrl = _pendingBootstrap.AtsPmtilesUrl,
                    atsSpritesUrl = _pendingBootstrap.AtsSpritesUrl,
                    atsSprites2xUrl = _pendingBootstrap.AtsSprites2xUrl
                };

                var cfgJson = JsonSerializer.Serialize(cfg);

                await MapWebView.ExecuteScriptAsync($"window.__OWELD = {cfgJson};");
                await MapWebView.ExecuteScriptAsync($"window.__ATS_POIS = {_pendingBootstrap.AtsPoisJson};");
                await MapWebView.ExecuteScriptAsync($"window.__ATS_ROADS = {_pendingBootstrap.AtsRoadsJson};");
                await MapWebView.ExecuteScriptAsync($"window.__OWELD_EXPANSION_DEPOTS = {_pendingBootstrap.ExpansionDepotMarkersJson};");
                await MapWebView.ExecuteScriptAsync($"window.__OWELD_REGION_OVERLAYS = {_pendingBootstrap.RegionOverlaysJson};");
                await MapWebView.ExecuteScriptAsync($"window.__OWELD_INITIAL_DRIVER_TRAILS = {_pendingBootstrap.DriverTrailsJson};");
                await MapWebView.ExecuteScriptAsync($"window.__OWELD_ILLINOIS_MAP = {_pendingBootstrap.IllinoisMapJson};");

                await MapWebView.ExecuteScriptAsync(@"
                    if (window.initializeOverwatchELD) {
                        window.initializeOverwatchELD(window.__OWELD || {});
                    }
                ");
            }
            catch (Exception ex)
            {
                ShowError("Live map loaded, but setup failed:\n" + ex.Message);
            }
        }

        private async Task LoadConfigAsync()
        {
            await Task.Yield();

            try
            {
                var cfgPath = VtcConfigService.ConfigPath;

                if (File.Exists(cfgPath))
                {
                    var json = File.ReadAllText(cfgPath);

                    using var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.TryGetProperty("BotApiBaseUrl", out var botApi))
                        _botBaseUrl = (botApi.GetString() ?? "").Trim();

                    if (string.IsNullOrWhiteSpace(_botBaseUrl) &&
                        doc.RootElement.TryGetProperty("VtcServerUrl", out var vtcServer))
                    {
                        _botBaseUrl = (vtcServer.GetString() ?? "").Trim();
                    }

                    // PRIMARY GUILD SOURCE
                    if (doc.RootElement.TryGetProperty("GuildId", out var guildProp))
                    {
                        var g = (guildProp.GetString() ?? "").Trim();

                        if (!string.IsNullOrWhiteSpace(g))
                            _guildId = g;
                    }
                }

                // FALLBACK TO PAIRING STORE
                if (string.IsNullOrWhiteSpace(_guildId) || _guildId == "0")
                {
                    var pairing = VtcPairingStore.Load();

                    var pairingGuild = (pairing?.GuildId ?? "").Trim();

                    if (!string.IsNullOrWhiteSpace(pairingGuild))
                        _guildId = pairingGuild;
                }

                // FINAL SAFETY
                if (string.IsNullOrWhiteSpace(_guildId))
                    _guildId = "0";
            }
            catch
            {
                _botBaseUrl = "";
                _guildId = "0";
            }
        }

        private async Task EnsureWebViewReadyAsync()
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OverWatchELD",
                "WebView2");

            Directory.CreateDirectory(userDataFolder);

            var env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder);

            await MapWebView.EnsureCoreWebView2Async(env);
        }

        private void ConfigureWebViewSettings()
        {
            if (MapWebView.CoreWebView2 == null)
                return;

            MapWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            MapWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
            MapWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            MapWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            MapWebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            MapWebView.CoreWebView2.Settings.IsZoomControlEnabled = true;
        }

        private static string GetLiveMapRoot()
        {
            return Path.Combine(AppContext.BaseDirectory, "LiveMap");
        }

        private static AssetBootstrapState LoadAssetState(string liveMapRoot)
        {
            var state = new AssetBootstrapState();

            var pmtilesPath = Path.Combine(liveMapRoot, "assets", "ats.pmtiles");
            var spritesJsonPath = Path.Combine(liveMapRoot, "assets", "sprites.json");
            var spritesPngPath = Path.Combine(liveMapRoot, "assets", "sprites.png");
            var sprites2xJsonPath = Path.Combine(liveMapRoot, "assets", "sprites@2x.json");
            var sprites2xPngPath = Path.Combine(liveMapRoot, "assets", "sprites@2x.png");

            state.HasAtsPmtiles =
                File.Exists(pmtilesPath) &&
                File.Exists(spritesJsonPath) &&
                File.Exists(spritesPngPath);

            if (state.HasAtsPmtiles)
            {
                state.AtsPmtilesUrl = "https://appassets.overwatcheld/assets/ats.pmtiles";
                state.AtsSpritesUrl = "https://appassets.overwatcheld/assets/sprites";

                if (File.Exists(sprites2xJsonPath) && File.Exists(sprites2xPngPath))
                    state.AtsSprites2xUrl = "https://appassets.overwatcheld/assets/sprites@2x";
            }

            try
            {
                var poisPath = Path.Combine(liveMapRoot, "data", "ats_pois.json");
                if (File.Exists(poisPath))
                    state.AtsPoisJson = File.ReadAllText(poisPath);
            }
            catch
            {
                state.AtsPoisJson = "[]";
            }

            try
            {
                var roadsPath = Path.Combine(liveMapRoot, "data", "ats_roads.json");
                if (File.Exists(roadsPath))
                    state.AtsRoadsJson = File.ReadAllText(roadsPath);
            }
            catch
            {
                state.AtsRoadsJson = "[]";
            }

            try
            {
                state.ExpansionDepotMarkersJson = LiveMapExpansionDepotMarkerService.BuildDepotMarkersJson();
            }
            catch
            {
                state.ExpansionDepotMarkersJson = "[]";
            }

            try
            {
                state.RegionOverlaysJson = LiveMapRegionOverlayService.BuildRegionOverlaysJson();
            }
            catch
            {
                state.RegionOverlaysJson = "[]";
            }

            try
            {
                state.DriverTrailsJson = LiveMapDriverRouteTrailService.BuildTrailsJson();
            }
            catch
            {
                state.DriverTrailsJson = "[]";
            }

            try
            {
                state.IllinoisMapJson = LiveMapIllinoisMapOverlayService.BuildIllinoisMapJson();
            }
            catch
            {
                state.IllinoisMapJson = "{}";
            }

            return state;
        }

        private void ShowWarning(string message)
        {
            MessageBox.Show(
                message,
                "Live Map",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        private void ShowError(string message)
        {
            MessageBox.Show(
                message,
                "Live Map",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        private sealed class AssetBootstrapState
        {
            public bool HasAtsPmtiles { get; set; }
            public string AtsPmtilesUrl { get; set; } = "";
            public string AtsSpritesUrl { get; set; } = "";
            public string AtsSprites2xUrl { get; set; } = "";
            public string AtsPoisJson { get; set; } = "[]";
            public string AtsRoadsJson { get; set; } = "[]";
            public string ExpansionDepotMarkersJson { get; set; } = "[]";
            public string DriverTrailsJson { get; set; } = "[]";
            public string RegionOverlaysJson { get; set; } = "[]";
            public string IllinoisMapJson { get; set; } = "{}";
        }
    }
}