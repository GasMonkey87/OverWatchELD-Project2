using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using OverWatchELD.Services;

namespace OverWatchELD.Views
{
    public partial class ConnectVehicleWindow : Window
    {
        private readonly TelemetryService _telemetry;
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        public bool Connected { get; private set; }

        private Action<TelemetrySnapshot>? _handler;

        public ConnectVehicleWindow(TelemetryService telemetry)
        {
            InitializeComponent();
            _telemetry = telemetry;

            EndpointBox.Text = telemetry.EndpointUrl;

            Loaded += async (_, __) => await DetectTelemetryAsync();

            Closed += (_, __) => SafeUnhook();
        }

        private async Task DetectTelemetryAsync()
        {
            StatusText.Text = "Detecting telemetry…";
            DetailText.Text = "Scanning local telemetry endpoints";

            string[] endpoints =
            {
                "http://127.0.0.1:25555/api/ets2/telemetry",
                "http://localhost:25555/api/ets2/telemetry",
                "http://127.0.0.1:25555/api/ats/telemetry",
                "http://localhost:25555/api/ats/telemetry"
            };

            foreach (var url in endpoints)
            {
                try
                {
                    var resp = await _http.GetAsync(url);
                    if (resp.IsSuccessStatusCode)
                    {
                        EndpointBox.Text = url;
                        StatusText.Text = "Telemetry detected";
                        DetailText.Text = url;
                        return;
                    }
                }
                catch { }
            }

            StatusText.Text = "No telemetry detected";
            DetailText.Text = "Start ATS/ETS2 + telemetry server";
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Connected = false;
            Close();
        }

        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var url = (EndpointBox.Text ?? "").Trim();

                if (string.IsNullOrWhiteSpace(url))
                {
                    StatusText.Text = "Enter an endpoint URL";
                    return;
                }

                _telemetry.EndpointUrl = url;

                SafeUnhook();

                StatusText.Text = "Connecting…";
                DetailText.Text = "Starting telemetry";

                _handler = (snap) =>
                {
                    try
                    {
                        Dispatcher.Invoke(() => ApplySnapshot(snap));
                    }
                    catch { }
                };

                _telemetry.Updated += _handler;
                _telemetry.Start();

                if (!RunRequiredPreTripInspection())
                {
                    Connected = false;
                    StatusText.Text = "Canceled";
                    DetailText.Text = "Pre-trip required";

                    try { _telemetry.Stop(); } catch { }
                    SafeUnhook();
                    Close();
                    return;
                }

                if (IsCurrentTruckLockedOut())
                {
                    Connected = false;
                    StatusText.Text = "Blocked";
                    DetailText.Text = "Truck is locked out by maintenance.";

                    try { _telemetry.Stop(); } catch { }
                    SafeUnhook();
                    return;
                }

                Connected = true;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                StatusText.Text = "Failed";
                DetailText.Text = ex.Message;
                try { _telemetry.Stop(); } catch { }
                SafeUnhook();
            }
        }

        private bool IsCurrentTruckLockedOut()
{
    try
    {
        if (!OverWatchELD.Stores.AdminSettingsStore.Load().AutoLockTruckOnInspectionDefect)
            return false;

        var snap = _telemetry.LastSnapshot;
        if (snap == null)
            return false;

        var truckName = snap.TruckName ?? "";
        var truckId = snap.TruckId ?? "";

        var state = OverWatchELD.Stores.VtcMaintenanceStore.Load();

        var locked = state.Trucks.FirstOrDefault(t =>
            t.OutOfService &&
            (
                string.Equals(t.UnitNumber, truckId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.TruckName, truckName, StringComparison.OrdinalIgnoreCase)
            ));

        if (locked == null)
            return false;

        MessageBox.Show(
            $"This truck is locked out by maintenance.\n\nTruck: {locked.TruckName}\nUnit: {locked.UnitNumber}\nIssue: {locked.CurrentIssue}",
            "Truck Locked Out",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        return true;
    }
    catch
    {
        return false;
    }
}

        private bool RunRequiredPreTripInspection()
        {
            try
            {
                var inspection = new InspectionEntryWindow("PreTrip", true)
                {
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                MessageBox.Show(
                    "Complete pre-trip inspection before connecting.",
                    "Pre-Trip Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                var result = inspection.ShowDialog();
                return result == true || inspection.SavedSuccessfully;
            }
            catch
            {
                return false;
            }
        }

        private void ApplySnapshot(TelemetrySnapshot snap)
        {
            if (snap.Connected)
            {
                StatusText.Text = "Connected";
                DetailText.Text = $"{snap.Source} • {snap.City}, {snap.State}";
                Connected = true;
            }
            else
            {
                StatusText.Text = "Not connected";
                DetailText.Text = snap.Source;
                Connected = false;
            }
        }

        private void SafeUnhook()
        {
            try
            {
                if (_handler != null)
                    _telemetry.Updated -= _handler;
            }
            catch { }
            finally
            {
                _handler = null;
            }
        }
    }
}