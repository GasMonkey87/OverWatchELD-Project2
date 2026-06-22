// Views/BolWindow.xaml.cs  ✅ FULL COPY/REPLACE
// Adds:
//  - Recent/Search list
//  - Debounced auto-save drafts
//  - Load selected record into form
//  - Import telemetry button
//  - Send BOL to Discord button

using Microsoft.Win32;
using OverWatchELD.Services;
using OverWatchELD.Services.Discord;
using OverWatchELD.ViewModels;
using System;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace OverWatchELD.Views
{
    public partial class BolWindow : Window
    {
        private readonly DispatcherTimer _autosaveTimer;
        private bool _loadingRecord;
        private readonly bool _canViewAllBols;
        private string _loadedDriverId = "";
        private string _loadedDriverDiscordUserId = "";
        private string _loadedDriverDiscordName = "";
        private string _loadedDriverName = "";

        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        public BolWindow() : this(false)
        {
        }

        public BolWindow(bool forceViewAllBols)
        {
            // forceViewAllBols is used only by owner/admin/dispatcher entry points
            // that already performed their own admin access check. This fixes cases
            // where the admin window knows the user is Owner but the generic session
            // role reader cannot detect it yet.
            _canViewAllBols = forceViewAllBols || BolAccessService.CanViewAllBols();
            InitializeComponent();

            if (DataContext == null)
                DataContext = new BolViewModel();

            ConfigureRoleBasedUi();

            _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _autosaveTimer.Tick += (_, __) =>
            {
                _autosaveTimer.Stop();
                TryAutosaveDraft();
            };
        }

        private void BolWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                SetIfEmpty(DateBox, DateTime.Now.ToString("MM/dd/yyyy"));
                SetIfEmpty(TimeBox, DateTime.Now.ToString("HH:mm"));

                if (!_canViewAllBols)
                    AutosaveStatusText.Text = "Driver mode: showing only your BOLs.";
                else
                    AutosaveStatusText.Text = "Admin/Dispatch mode: showing all driver BOLs.";

                if (string.IsNullOrWhiteSpace(LoadNumberBox.Text))
                    LoadNumberBox.Text = BolStore.GenerateNextLoadNumber();

                var snap = TryGetTelemetrySnapshot();
                if (snap != null)
                {
                    var truck = GetString(snap, "TruckMakeModel", "Truck", "TruckName", "Vehicle", "VehicleName", "TruckModel");
                    if (!string.IsNullOrWhiteSpace(truck)) SetIfEmpty(TruckBox, truck);

                    var city = GetString(snap, "City", "CurrentCity", "LocationCity");
                    var state = GetString(snap, "State", "CurrentState", "LocationState");
                    var cityState = JoinCityState(city, state);
                    if (!string.IsNullOrWhiteSpace(cityState)) SetIfEmpty(CityOriginBox, cityState);

                    var miles = GetDouble(snap, "OdometerMiles", "TotalMiles", "TripMiles", "DistanceMiles", "MilesDriven");
                    if (miles == null)
                    {
                        var meters = GetDouble(snap, "OdometerMeters", "TotalDistanceMeters", "TripMeters", "DistanceMeters", "MetersDriven");
                        if (meters != null) miles = meters.Value * 0.000621371;
                    }
                    if (miles != null && miles.Value > 0)
                        SetIfEmpty(MileageBox, Math.Round(miles.Value, 1).ToString(CultureInfo.InvariantCulture));
                }

                RefreshRecentList();
                TryAutosaveDraft();
            }
            catch { }
        }

        private void AnyField_Changed(object sender, TextChangedEventArgs e)
        {
            if (_loadingRecord) return;

            AutosaveStatusText.Text = "Draft autosave: pending...";
            _autosaveTimer.Stop();
            _autosaveTimer.Start();
        }

        private void TryAutosaveDraft()
        {
            try
            {
                if (_loadingRecord) return;

                var rec = ReadFormRecord();
                if (!BolAccessService.CanCurrentUserAccess(rec))
                {
                    MessageBox.Show("Drivers can only save/print their own BOLs.", "BOL", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrWhiteSpace(rec.LoadNumber))
                    rec.LoadNumber = BolStore.GenerateNextLoadNumber();

                BolStore.Upsert(rec);
                LinkBolToMasterProfile(rec);
                AutosaveStatusText.Text = $"Draft autosave: saved {DateTime.Now:HH:mm:ss}";
                RefreshRecentList();
            }
            catch
            {
                AutosaveStatusText.Text = "Draft autosave: failed";
            }
        }

        private void SearchBox_Changed(object sender, TextChangedEventArgs e) => RefreshRecentList();

        private void RefreshRecent_Click(object sender, RoutedEventArgs e) => RefreshRecentList();

        private void RefreshRecentList()
        {
            try
            {
                var q = SearchBox.Text?.Trim() ?? "";
                var items = _canViewAllBols
                    ? BolStore.SearchRecent(q, max: 80)
                    : BolStore.SearchRecentForCurrentUser(q, max: 80);
                RecentList.ItemsSource = items;
            }
            catch
            {
                RecentList.ItemsSource = null;
            }
        }

        private void LoadSelected_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (RecentList.SelectedItem is not BolStore.BolListItem item) return;
                var rec = BolStore.TryGetForCurrentUser(item.LoadNumber);
                if (rec == null) return;

                LoadRecordIntoForm(rec);
                AutosaveStatusText.Text = $"Loaded: {rec.LoadNumber}";
            }
            catch { }
        }

        private void ImportTelemetry_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var snap = TryGetTelemetrySnapshot();
                if (snap == null)
                {
                    MessageBox.Show("No telemetry available.", "BOL", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var truck = GetString(snap, "TruckMakeModel", "Truck", "TruckName", "Vehicle", "VehicleName", "TruckModel");
                if (!string.IsNullOrWhiteSpace(truck))
                    SetIfEmpty(TruckBox, truck);

                var city = GetString(snap, "City", "CurrentCity", "LocationCity");
                var state = GetString(snap, "State", "CurrentState", "LocationState");
                var cityState = JoinCityState(city, state);
                if (!string.IsNullOrWhiteSpace(cityState))
                    SetIfEmpty(CityOriginBox, cityState);

                var miles = GetDouble(snap, "OdometerMiles", "TotalMiles", "TripMiles", "DistanceMiles", "MilesDriven");
                if (miles == null)
                {
                    var meters = GetDouble(snap, "OdometerMeters", "TotalDistanceMeters", "TripMeters", "DistanceMeters", "MetersDriven");
                    if (meters != null) miles = meters.Value * 0.000621371;
                }

                if (miles != null && miles.Value > 0)
                    SetIfEmpty(MileageBox, Math.Round(miles.Value, 1).ToString(CultureInfo.InvariantCulture));

                MessageBox.Show("Telemetry imported into the BOL form.", "BOL", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Telemetry Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void SendBolToDiscord_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;

            if (!BolAccessService.CanSendBolToDiscord())
            {
                MessageBox.Show("Only Owners, Admins, and Dispatchers can send BOLs to Discord.", "BOL Discord", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                if (btn != null)
                    btn.IsEnabled = false;

                AutosaveStatusText.Text = "Sending BOL to Discord...";

                await Dispatcher.InvokeAsync(
                    () => { },
                    System.Windows.Threading.DispatcherPriority.Background);

                await SendBolToDiscordAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"BOL send failed:\n\n{ex.Message}",
                    "BOL Discord",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                if (btn != null)
                    btn.IsEnabled = true;
            }
        }

        private void NewLoad_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LoadNumberBox.Text = BolStore.GenerateNextLoadNumber();
                _loadedDriverId = "";
                _loadedDriverDiscordUserId = "";
                _loadedDriverDiscordName = "";
                _loadedDriverName = "";
                TryAutosaveDraft();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Load # Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SavePdf_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var rec = ReadFormRecord();
                if (!BolAccessService.CanCurrentUserAccess(rec))
                {
                    MessageBox.Show("Drivers can only save/print their own BOLs.", "BOL", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrWhiteSpace(rec.LoadNumber))
                    rec.LoadNumber = BolStore.GenerateNextLoadNumber();

                LoadNumberBox.Text = rec.LoadNumber;

                var dlg = new SaveFileDialog
                {
                    Filter = "PDF (*.pdf)|*.pdf",
                    FileName = $"BOL_{rec.LoadNumber}.pdf"
                };

                if (dlg.ShowDialog() != true)
                    return;

                rec.SavedUtc = DateTimeOffset.UtcNow;
                BolStore.Upsert(rec);
                LinkBolToMasterProfile(rec);

                BolPdfExporter.Export(
                    dlg.FileName,
                    loadNumber: rec.LoadNumber,
                    truck: rec.Truck,
                    licensePlate: rec.LicensePlate,
                    date: rec.Date,
                    time: rec.Time,
                    cityOrigin: rec.CityOrigin,
                    cityDestination: rec.CityDestination,
                    companyPickup: rec.CompanyPickup,
                    companyDropoff: rec.CompanyDropoff,
                    commodity: rec.Commodity,
                    mileage: rec.Mileage,
                    weightLbs: rec.WeightLbs,
                    notes: rec.Notes
                );

                AutosaveStatusText.Text = $"Exported PDF + saved: {rec.LoadNumber}";
                RefreshRecentList();
                MessageBox.Show("PDF exported. Draft saved for lookup.",
                    "BOL Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "BOL Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PrintBol_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var rec = ReadFormRecord();
                if (!BolAccessService.CanCurrentUserAccess(rec))
                {
                    MessageBox.Show("Drivers can only save/print their own BOLs.", "BOL", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrWhiteSpace(rec.LoadNumber))
                {
                    rec.LoadNumber = BolStore.GenerateNextLoadNumber();
                    LoadNumberBox.Text = rec.LoadNumber;
                }

                rec.SavedUtc = DateTimeOffset.UtcNow;
                BolStore.Upsert(rec);
                LinkBolToMasterProfile(rec);

                var fileName = Path.Combine(Path.GetTempPath(), $"BOL_{rec.LoadNumber}_{DateTime.Now:yyyyMMddHHmmss}.pdf");
                BolPdfExporter.Export(
                    fileName,
                    loadNumber: rec.LoadNumber,
                    truck: rec.Truck,
                    licensePlate: rec.LicensePlate,
                    date: rec.Date,
                    time: rec.Time,
                    cityOrigin: rec.CityOrigin,
                    cityDestination: rec.CityDestination,
                    companyPickup: rec.CompanyPickup,
                    companyDropoff: rec.CompanyDropoff,
                    commodity: rec.Commodity,
                    mileage: rec.Mileage,
                    weightLbs: rec.WeightLbs,
                    notes: rec.Notes
                );

                Process.Start(new ProcessStartInfo(fileName)
                {
                    UseShellExecute = true,
                    Verb = "print"
                });

                AutosaveStatusText.Text = $"Sent BOL to printer: {rec.LoadNumber}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "BOL Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            try { Close(); } catch { }
        }

        private async Task SendBolToDiscordAsync()
        {
            if (!BolAccessService.CanSendBolToDiscord())
                throw new InvalidOperationException("Only Owners, Admins, and Dispatchers can send BOLs to Discord.");

            var settingsPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Config",
                "settings_window.discord.json");

            string webhook = "";

            try
            {
                if (File.Exists(settingsPath))
                {
                    var settingsJson = File.ReadAllText(settingsPath);
                    using var settingsDoc = JsonDocument.Parse(settingsJson);

                    if (settingsDoc.RootElement.TryGetProperty("BolWebhookUrl", out var bolWebhookEl))
                        webhook = (bolWebhookEl.GetString() ?? "").Trim();
                }
            }
            catch
            {
                webhook = "";
            }

            var rec = ReadFormRecord();

            if (string.IsNullOrWhiteSpace(rec.LoadNumber))
            {
                rec.LoadNumber = BolStore.GenerateNextLoadNumber();
                LoadNumberBox.Text = rec.LoadNumber;
            }

            rec.SavedUtc = DateTimeOffset.UtcNow;
            BolStore.Upsert(rec);
            LinkBolToMasterProfile(rec);

            var payload = new
            {
                username = "OverWatch ELD",
                embeds = new[]
                {
            new
            {
                title = $"Bill of Lading • {rec.LoadNumber}",
                description = "Completed BOL form",
                fields = new object[]
                {
                    new { name = "Load Number", value = SafeDiscord(rec.LoadNumber), inline = true },
                    new { name = "Date", value = SafeDiscord(rec.Date), inline = true },
                    new { name = "Time", value = SafeDiscord(rec.Time), inline = true },
                    new { name = "Truck", value = SafeDiscord(rec.Truck), inline = true },
                    new { name = "License Plate", value = SafeDiscord(rec.LicensePlate), inline = true },
                    new { name = "Weight (lbs)", value = SafeDiscord(rec.WeightLbs), inline = true },
                    new { name = "Commodity", value = SafeDiscord(rec.Commodity), inline = false },
                    new { name = "Mileage", value = SafeDiscord(rec.Mileage), inline = true },
                    new { name = "City Origin", value = SafeDiscord(rec.CityOrigin), inline = true },
                    new { name = "City Destination", value = SafeDiscord(rec.CityDestination), inline = true },
                    new { name = "Company Pick Up", value = SafeDiscord(rec.CompanyPickup), inline = false },
                    new { name = "Company Drop Off", value = SafeDiscord(rec.CompanyDropoff), inline = false },
                    new { name = "Notes", value = SafeDiscord(string.IsNullOrWhiteSpace(rec.Notes) ? "None" : rec.Notes), inline = false }
                },
                footer = new { text = "OverWatch ELD • BOL Window" },
                timestamp = DateTime.UtcNow.ToString("o")
            }
        }
            };

            if (!string.IsNullOrWhiteSpace(webhook))
            {
                var json = JsonSerializer.Serialize(payload);

                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var resp = await _http.PostAsync(webhook, content);

                if (resp.IsSuccessStatusCode)
                {
                    AutosaveStatusText.Text = $"BOL sent to Discord: {rec.LoadNumber}";
                    MessageBox.Show("BOL sent to Discord successfully.", "BOL Discord", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }

            var ok = await DiscordNotificationPushService.PushAsync(
                "BOL",
                $"Bill of Lading • {rec.LoadNumber}",
                $"Truck: {SafeDiscord(rec.Truck)}\nCommodity: {SafeDiscord(rec.Commodity)}\nFrom: {SafeDiscord(rec.CityOrigin)}\nTo: {SafeDiscord(rec.CityDestination)}",
                "OverWatch ELD • BOL Window");

            if (!ok)
            {
                MessageBox.Show(
                    "BOL could not be sent. Set the BOL channel/webhook in Admin Notification Settings or run !setbolchannel.",
                    "BOL Discord",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            AutosaveStatusText.Text = $"BOL sent to Discord: {rec.LoadNumber}";
            MessageBox.Show("BOL sent to Discord successfully.", "BOL Discord", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void LinkBolToMasterProfile(BolStore.BolRecord rec)
        {
            try
            {
                var identity = DriverProfileIdentitySnapshot.Current();

                DriverProfileMasterStore.AddBol(
                    identity.DiscordUserId,
                    identity.DiscordName,
                    identity.DisplayName,
                    rec.LoadNumber);

                DriverProfileMasterStore.LinkTruck(
                    identity.DiscordUserId,
                    identity.DiscordName,
                    identity.DisplayName,
                    rec.Truck,
                    rec.Truck,
                    rec.LicensePlate,
                    "",
                    "BOL",
                    current: true);
            }
            catch
            {
            }
        }

        private static string SafeDiscord(string? value)
        {
            var s = (value ?? "").Trim();
            return string.IsNullOrWhiteSpace(s) ? "N/A" : s;
        }

        private BolStore.BolRecord ReadFormRecord()
        {
            return new BolStore.BolRecord
            {
                LoadNumber = LoadNumberBox.Text?.Trim() ?? "",
                Date = DateBox.Text ?? "",
                Time = TimeBox.Text ?? "",
                Truck = TruckBox.Text ?? "",
                LicensePlate = PlateBox.Text ?? "",
                WeightLbs = WeightLbsBox.Text ?? "",
                Commodity = CommodityBox.Text ?? "",
                Mileage = MileageBox.Text ?? "",
                CityOrigin = CityOriginBox.Text ?? "",
                CityDestination = CityDestinationBox.Text ?? "",
                CompanyPickup = CompanyPickupBox.Text ?? "",
                CompanyDropoff = CompanyDropoffBox.Text ?? "",
                Notes = NotesBox.Text ?? "",
                DriverId = !string.IsNullOrWhiteSpace(_loadedDriverId) ? _loadedDriverId : BolAccessService.GetCurrentDriverId(),
                DriverDiscordUserId = !string.IsNullOrWhiteSpace(_loadedDriverDiscordUserId) ? _loadedDriverDiscordUserId : DriverProfileIdentitySnapshot.Current().DiscordUserId,
                DriverDiscordName = !string.IsNullOrWhiteSpace(_loadedDriverDiscordName) ? _loadedDriverDiscordName : DriverProfileIdentitySnapshot.Current().DiscordName,
                DriverName = !string.IsNullOrWhiteSpace(_loadedDriverName) ? _loadedDriverName : DriverProfileIdentitySnapshot.Current().DisplayName,
                SavedUtc = DateTimeOffset.UtcNow
            };
        }

        private void LoadRecordIntoForm(BolStore.BolRecord rec)
        {
            _loadingRecord = true;
            try
            {
                LoadNumberBox.Text = rec.LoadNumber;
                DateBox.Text = rec.Date;
                TimeBox.Text = rec.Time;
                TruckBox.Text = rec.Truck;
                PlateBox.Text = rec.LicensePlate;
                WeightLbsBox.Text = rec.WeightLbs;
                CommodityBox.Text = rec.Commodity;
                MileageBox.Text = rec.Mileage;
                CityOriginBox.Text = rec.CityOrigin;
                CityDestinationBox.Text = rec.CityDestination;
                CompanyPickupBox.Text = rec.CompanyPickup;
                CompanyDropoffBox.Text = rec.CompanyDropoff;
                NotesBox.Text = rec.Notes;
                _loadedDriverId = rec.DriverId ?? "";
                _loadedDriverDiscordUserId = rec.DriverDiscordUserId ?? "";
                _loadedDriverDiscordName = rec.DriverDiscordName ?? "";
                _loadedDriverName = rec.DriverName ?? "";
            }
            finally
            {
                _loadingRecord = false;
            }
        }

        private void ConfigureRoleBasedUi()
        {
            try
            {
                if (SendDiscordButton != null)
                {
                    SendDiscordButton.Visibility = _canViewAllBols ? Visibility.Visible : Visibility.Collapsed;
                    SendDiscordButton.IsEnabled = _canViewAllBols;
                }

                if (WindowModeText != null)
                {
                    WindowModeText.Text = _canViewAllBols
                        ? "Owners/Admins/Dispatchers: all driver BOLs + Discord sending."
                        : "Driver mode: only your own BOLs are shown here.";
                }
            }
            catch { }
        }

        private static object? TryGetTelemetrySnapshot()
        {
            try
            {
                var app = Application.Current;
                if (app == null) return null;

                var telObj = GetProp(app, "Telemetry") ?? GetProp(app, "TelemetryService");
                if (telObj == null) return null;

                var snap =
                    GetProp(telObj, "LastSnapshot") ??
                    GetProp(telObj, "Snapshot") ??
                    GetProp(telObj, "CurrentSnapshot") ??
                    GetProp(telObj, "Current") ??
                    GetProp(telObj, "Latest");

                return snap ?? telObj;
            }
            catch { return null; }
        }

        private static object? GetProp(object obj, string name)
        {
            try
            {
                var p = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                return p?.GetValue(obj);
            }
            catch { return null; }
        }

        private static string? GetString(object obj, params string[] names)
        {
            foreach (var n in names)
            {
                try
                {
                    var p = obj.GetType().GetProperty(n, BindingFlags.Public | BindingFlags.Instance);
                    if (p == null) continue;
                    var v = p.GetValue(obj);
                    var s = v?.ToString();
                    if (!string.IsNullOrWhiteSpace(s))
                        return s.Trim();
                }
                catch { }
            }
            return null;
        }

        private static double? GetDouble(object obj, params string[] names)
        {
            foreach (var n in names)
            {
                try
                {
                    var p = obj.GetType().GetProperty(n, BindingFlags.Public | BindingFlags.Instance);
                    if (p == null) continue;

                    var v = p.GetValue(obj);
                    if (v == null) continue;

                    if (v is double d) return d;
                    if (v is float f) return f;
                    if (v is int i) return i;
                    if (v is long l) return l;

                    if (double.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                        return parsed;
                }
                catch { }
            }
            return null;
        }

        private static string JoinCityState(string? city, string? state)
        {
            city = string.IsNullOrWhiteSpace(city) ? null : city.Trim();
            state = string.IsNullOrWhiteSpace(state) ? null : state.Trim();
            if (city == null && state == null) return "";
            if (city != null && state != null) return $"{city}, {state}";
            return city ?? state ?? "";
        }

        private static void SetIfEmpty(TextBox box, string value)
        {
            if (string.IsNullOrWhiteSpace(box.Text))
                box.Text = value;
        }
    }
}