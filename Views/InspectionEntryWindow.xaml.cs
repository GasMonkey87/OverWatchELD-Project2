using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using OverWatchELD.Models;
using OverWatchELD.Services;
using OverWatchELD.Stores;
using System.Linq;
using System.Threading.Tasks;

namespace OverWatchELD.Views
{
    public partial class InspectionEntryWindow : Window
    {
        private readonly string? _defaultType;
        private readonly ObservableCollection<InspectionChecklistItem> _items = new();
        private readonly DispatcherTimer _telemetryTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };

        private bool _isLocked;
        private bool _vehicleBoxUserEdited;
        private bool _settingVehicleBoxText;
        private bool _saveInProgress;
        private readonly bool _closeOnSuccessfulSave;

        public bool SavedSuccessfully { get; private set; }

        public InspectionEntryWindow() : this(null, false) { }

        public InspectionEntryWindow(string? defaultType) : this(defaultType, false) { }

        public InspectionEntryWindow(string? defaultType, bool closeOnSuccessfulSave)
        {
            _defaultType = NormalizeType(defaultType);
            _closeOnSuccessfulSave = closeOnSuccessfulSave;

            InitializeComponent();

            if (TypeText != null)
                TypeText.Text = _defaultType ?? "Pre-Trip";

            ChecklistList.ItemsSource = _items;

            var view = (ListCollectionView)CollectionViewSource.GetDefaultView(_items);
            view.GroupDescriptions.Clear();
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(InspectionChecklistItem.Category)));

            LoadDefaultChecklist();
            ApplyModeStylingAndLocking();

            if (VehicleBox != null)
            {
                VehicleBox.TextChanged += VehicleBox_TextChanged;
                SetVehicleBoxFromTelemetry(force: true);
            }

            Loaded += InspectionEntryWindow_Loaded;
            Closing += (_, __) =>
            {
                try { _telemetryTimer.Stop(); } catch { }
            };
        }

        private static string? NormalizeType(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            raw = raw.Trim();

            if (raw.Equals("PreTrip", StringComparison.OrdinalIgnoreCase)) return "Pre-Trip";
            if (raw.Equals("PostTrip", StringComparison.OrdinalIgnoreCase)) return "Post-Trip";
            if (raw.Equals("VehicleInspection", StringComparison.OrdinalIgnoreCase)) return "Trailer";
            if (raw.Equals("Vehicle Inspection", StringComparison.OrdinalIgnoreCase)) return "Trailer";
            if (raw.Equals("TrailerInspection", StringComparison.OrdinalIgnoreCase)) return "Trailer-Only";
            if (raw.Equals("TrailerOnly", StringComparison.OrdinalIgnoreCase)) return "Trailer-Only";

            return raw;
        }

        private bool IsPreTripMode =>
            string.Equals(_defaultType, "Pre-Trip", StringComparison.OrdinalIgnoreCase);

        private bool IsPostTripMode =>
            string.Equals(_defaultType, "Post-Trip", StringComparison.OrdinalIgnoreCase);

        private bool IsTrailerOnlyMode =>
            string.Equals(_defaultType, "Trailer", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_defaultType, "Trailer-Only", StringComparison.OrdinalIgnoreCase);

        private void ApplyModeStylingAndLocking()
        {
            if (IsPreTripMode)
                Title = "Pre-Trip Inspection (FMCSA Required Before Driving)";
            else if (IsPostTripMode)
                Title = "Post-Trip Inspection";
            else if (IsTrailerOnlyMode)
                Title = "Trailer Inspection";
            else
                Title = "Inspection";

            if (TypeText != null && string.IsNullOrWhiteSpace(TypeText.Text))
                TypeText.Text = _defaultType ?? "Pre-Trip";

            _telemetryTimer.Tick += (_, __) =>
            {
                UpdateLockFromTelemetry();
                SetVehicleBoxFromTelemetry(force: false);
            };

            _telemetryTimer.Start();

            UpdateLockFromTelemetry();
            SetVehicleBoxFromTelemetry(force: true);
        }

        private void LoadDefaultChecklist()
        {
            _items.Clear();

            var defaults = InspectionLog.CreateDefault();

            var tractor = IsPostTripMode ? defaults.PostTripTractorChecklist : defaults.PreTripTractorChecklist;
            var trailer = IsPostTripMode ? defaults.PostTripTrailerChecklist : defaults.PreTripTrailerChecklist;

            if (!IsTrailerOnlyMode)
            {
                foreach (var item in tractor)
                    _items.Add(item);
            }

            foreach (var item in trailer)
                _items.Add(item);
        }

        private void InspectionEntryWindow_Loaded(object sender, RoutedEventArgs e)
        {
            SetVehicleBoxFromTelemetry(force: true);
        }

        private void VehicleBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_settingVehicleBoxText)
                return;

            _vehicleBoxUserEdited = true;
        }

        private void SetVehicleBoxFromTelemetry(bool force)
        {
            try
            {
                if (VehicleBox == null)
                    return;

                if (!force && _vehicleBoxUserEdited)
                    return;

                var value = GetVehicleIdFromTelemetryOrUnknown();

                if (string.IsNullOrWhiteSpace(value))
                    value = "Unknown";

                if (string.Equals(VehicleBox.Text, value, StringComparison.Ordinal))
                    return;

                _settingVehicleBoxText = true;
                VehicleBox.Text = value;
            }
            catch { }
            finally
            {
                _settingVehicleBoxText = false;
            }
        }

        private string GetVehicleIdFromTelemetryOrUnknown()
        {
            try
            {
                var app = (App)Application.Current;
                var snap = app.Telemetry?.LastSnapshot;

                if (snap == null)
                    return AppendGameDate("Unknown");

                if (IsPreTripMode)
                {
                    var truck = BuildTruckDisplay(snap);
                    if (!string.IsNullOrWhiteSpace(truck))
                        return AppendGameDate(truck);
                }

                if (IsPostTripMode || IsTrailerOnlyMode)
                {
                    var trailer = BuildTrailerDisplay(snap);
                    if (!string.IsNullOrWhiteSpace(trailer))
                        return AppendGameDate(trailer);

                    var truckFallback = BuildTruckDisplay(snap);
                    if (!string.IsNullOrWhiteSpace(truckFallback))
                        return AppendGameDate(truckFallback);
                }

                var generic =
                    BuildTruckDisplay(snap) ??
                    BuildTrailerDisplay(snap) ??
                    ReadPropAsString(snap, "VehicleId") ??
                    ReadPropAsString(snap, "TruckId") ??
                    ReadPropAsString(snap, "TruckID") ??
                    ReadPropAsString(snap, "TruckName") ??
                    ReadPropAsString(snap, "VehicleName");

                if (!string.IsNullOrWhiteSpace(generic))
                    return AppendGameDate(generic.Trim());

                return AppendGameDate("Unknown");
            }
            catch
            {
                return AppendGameDate("Unknown");
            }
        }

        private string AppendGameDate(string baseText)
        {
            var gameDate = GetGameDateText();

            if (string.IsNullOrWhiteSpace(gameDate))
                return baseText;

            return $"{baseText} - {gameDate}";
        }

        private static string? BuildTruckDisplay(object snap)
        {
            try
            {
                var make =
                    ReadPropAsString(snap, "TruckMake") ??
                    ReadPropAsString(snap, "TruckBrand");

                var model = ReadPropAsString(snap, "TruckModel");

                var plate =
                    ReadPropAsString(snap, "TruckLicensePlate") ??
                    ReadPropAsString(snap, "LicensePlate");

                var combined = $"{make} {model}".Trim();

                if (!string.IsNullOrWhiteSpace(plate))
                    combined = !string.IsNullOrWhiteSpace(combined)
                        ? $"{combined} [{plate.Trim()}]"
                        : plate.Trim();

                if (!string.IsNullOrWhiteSpace(combined))
                    return combined;

                return
                    ReadPropAsString(snap, "TruckId") ??
                    ReadPropAsString(snap, "TruckID") ??
                    ReadPropAsString(snap, "TruckName") ??
                    ReadPropAsString(snap, "VehicleId") ??
                    ReadPropAsString(snap, "VehicleName");
            }
            catch
            {
                return null;
            }
        }

        private static string? BuildTrailerDisplay(object snap)
        {
            try
            {
                var trailerName =
                    ReadPropAsString(snap, "TrailerName") ??
                    ReadPropAsString(snap, "TrailerId") ??
                    ReadPropAsString(snap, "TrailerID") ??
                    ReadPropAsString(snap, "AttachedTrailerName");

                var cargoName =
                    ReadPropAsString(snap, "CargoName") ??
                    ReadPropAsString(snap, "TrailerCargo") ??
                    ReadPropAsString(snap, "JobCargo");

                if (!string.IsNullOrWhiteSpace(trailerName) && !string.IsNullOrWhiteSpace(cargoName))
                    return $"{trailerName.Trim()} / {cargoName.Trim()}";

                if (!string.IsNullOrWhiteSpace(trailerName))
                    return trailerName.Trim();

                if (!string.IsNullOrWhiteSpace(cargoName))
                    return cargoName.Trim();

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static string? ReadPropAsString(object obj, string propName)
        {
            var p = obj.GetType().GetProperty(propName, BindingFlags.Instance | BindingFlags.Public);

            if (p == null || !p.CanRead)
                return null;

            var v = p.GetValue(obj);
            return v?.ToString();
        }

        private static string GetGameDateText()
        {
            try
            {
                var gameNow = EldClock.UtcNow;

                if (gameNow == default)
                    return "";

                return gameNow.ToString("yyyy-MM-dd");
            }
            catch
            {
                return "";
            }
        }

        private void UpdateLockFromTelemetry()
        {
            try
            {
                var app = (App)Application.Current;
                var snap = app.Telemetry?.LastSnapshot;

                var moving = snap != null && snap.Connected && Math.Abs(snap.SpeedMps) > 0.5;

                if (IsPreTripMode && moving)
                {
                    SetLocked(true,
                        "FMCSA: Pre-Trip inspection must be completed and certified BEFORE driving.\n" +
                        "Stop the vehicle to complete this inspection.");

                    return;
                }

                SetLocked(moving, moving ? "Inspection is locked while the vehicle is moving." : null);
            }
            catch
            {
                SetLocked(false, null);
            }
        }

        private void SetLocked(bool locked, string? message)
        {
            _isLocked = locked;

            if (LockMsg != null)
            {
                if (!string.IsNullOrWhiteSpace(message))
                {
                    try { LockMsg.Text = message; } catch { }
                    LockMsg.Visibility = Visibility.Visible;
                }
                else
                {
                    LockMsg.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
                }
            }

            if (SaveButton != null) SaveButton.IsEnabled = !locked;
            if (VehicleBox != null) VehicleBox.IsEnabled = !locked;
            if (NotesBox != null) NotesBox.IsEnabled = !locked;
            if (SignatureBox != null) SignatureBox.IsEnabled = !locked;
            if (CertifyBox != null) CertifyBox.IsEnabled = !locked;
            if (ChecklistList != null) ChecklistList.IsEnabled = !locked;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            SavedSuccessfully = false;

            try { DialogResult = false; } catch { }

            Close();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_saveInProgress)
                return;

            _saveInProgress = true;

            try
            {
                try { _telemetryTimer.Stop(); } catch { }

                if (SaveButton != null) SaveButton.IsEnabled = false;
                if (NoDefectsButton != null) NoDefectsButton.IsEnabled = false;

                var type = _defaultType ?? "Pre-Trip";
                var vehicle = GetVehicleIdFromTelemetryOrUnknown();

                if (VehicleBox != null && !string.IsNullOrWhiteSpace(VehicleBox.Text))
                    vehicle = VehicleBox.Text.Trim();

                var notes = NotesBox?.Text ?? "";
                var sig = SignatureBox?.Text?.Trim() ?? "";
                var certified = CertifyBox?.IsChecked == true;
                var hasDefects = _items.Any(x => x.IsDefect || !x.IsOk);

                if (_isLocked)
                {
                    MessageBox.Show(this,
                        "Inspection is locked while the vehicle is moving.",
                        "Inspection",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    ResetSubmitState();
                    return;
                }

                if (!certified)
                {
                    MessageBox.Show(this,
                        "Certification is required.",
                        "Inspection",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    ResetSubmitState();
                    return;
                }

                if (string.IsNullOrWhiteSpace(sig))
                {
                    MessageBox.Show(this,
                        "Signature required.",
                        "Inspection",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    ResetSubmitState();
                    return;
                }

                // Save a tiny local submit marker only. Do not call SQLite stores, Discord sync,
                // maintenance ticket creation, score sync, or other services from this button.
                // Those paths were freezing/crashing after the window closed.
                SafeAppendInspectionSubmitQueue(type, vehicle, notes, hasDefects);

                SavedSuccessfully = true;

                try
                {
                    if (SavedMsg != null)
                        SavedMsg.Visibility = Visibility.Visible;
                }
                catch { }

                // Close immediately and return success to the caller.
                // Dashboard no longer opens a second pre-trip window after this.
                try { DialogResult = true; } catch { }
                try { Close(); } catch { }
            }
            catch (Exception ex)
            {
                ResetSubmitState();

                MessageBox.Show(this,
                    $"Inspection submit failed.\n\n{ex.Message}",
                    "Inspection",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void ResetSubmitState()
        {
            _saveInProgress = false;

            if (SaveButton != null) SaveButton.IsEnabled = !_isLocked;
            if (NoDefectsButton != null) NoDefectsButton.IsEnabled = !_isLocked;

            try { _telemetryTimer.Start(); } catch { }
        }

        private static void SafeAppendInspectionSubmitQueue(string type, string vehicle, string notes, bool hasDefects)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "OverWatchELD",
                    "InspectionSubmits");

                Directory.CreateDirectory(dir);

                var path = Path.Combine(dir, "inspection_submit_queue.jsonl");

                var row = new
                {
                    createdUtc = DateTime.UtcNow,
                    inspectionType = type,
                    vehicle,
                    passed = !hasDefects,
                    defects = hasDefects,
                    notes = string.IsNullOrWhiteSpace(notes)
                        ? (hasDefects ? "Inspection completed with defects." : "Inspection completed with no defects found.")
                        : notes,
                    driverName = ResolveDriverName()
                };

                File.AppendAllText(path, JsonSerializer.Serialize(row) + Environment.NewLine);
            }
            catch
            {
            }
        }


        private void CreateMaintenanceTicketFromDefect(string inspectionType, string vehicle, string notes)
        {
            try
            {
                var ticket = new MaintenanceRequestTicket
                {
                    TruckName = vehicle,
                    DriverName = ResolveDriverName(),
                    CurrentIssue = $"Defect reported during {inspectionType} inspection",
                    CurrentIssueSeverity = "Inspection Defect",
                    DamageRepairRequested = true,
                    OtherMaintenanceRequested = true,
                    OutOfService = true,
                    Notes = string.IsNullOrWhiteSpace(notes)
                        ? $"Inspection defect found during {inspectionType}. Review inspection history for details."
                        : notes,
                    Status = "Open",
                    CreatedUtc = DateTime.UtcNow
                };

                try
                {
                    var app = (App)Application.Current;
                    var snap = app.Telemetry?.LastSnapshot;

                    if (snap != null)
                    {
                        ticket.UnitNumber =
                            ReadPropAsString(snap, "TruckId") ??
                            ReadPropAsString(snap, "TruckID") ??
                            "";

                        ticket.PlateNumber =
                            ReadPropAsString(snap, "TruckLicensePlate") ??
                            ReadPropAsString(snap, "LicensePlate") ??
                            "";

                        ticket.Location =
                            ReadPropAsString(snap, "City") ??
                            ReadPropAsString(snap, "CurrentCity") ??
                            "";
                    }
                }
                catch { }

                ticket = new MaintenanceRequestTicketStore().Add(ticket);

                try
                {
                    if (AdminSettingsStore.Load().AutoLockTruckOnInspectionDefect)
                    {
                        var state = VtcMaintenanceStore.Load();

                        var truck = state.Trucks.FirstOrDefault(t =>
                            string.Equals(t.UnitNumber, ticket.UnitNumber, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(t.TruckName, ticket.TruckName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(t.PlateNumber, ticket.PlateNumber, StringComparison.OrdinalIgnoreCase));

                        if (truck != null)
                        {
                            truck.OutOfService = true;
                            truck.CurrentIssue = $"Locked out from inspection defect ticket {ticket.RequestNumber}";
                            truck.CurrentIssueSeverity = "Critical";
                            VtcMaintenanceStore.Save(state);
                        }
                    }
                }
                catch
                {
                }

                _ = new MaintenanceRequestDiscordPoster().PostAsync(ticket);

                // Ticket created silently; avoid modal popup during inspection submit.
            }
            catch
            {
            }
        }
        private bool HasAnyDefects()
        {
            foreach (var item in _items)
            {
                try
                {
                    if (!item.IsOk)
                        return true;
                }
                catch { }
            }

            return false;
        }

        private void SaveToInspectionHistory(string type, string vehicle, string notes, bool hasDefects)
        {
            try
            {
                var truck = vehicle;
                var unit = "";
                var plate = "";
                var location = "";

                try
                {
                    var app = (App)Application.Current;
                    var snap = app.Telemetry?.LastSnapshot;

                    if (snap != null)
                    {
                        unit =
                            ReadPropAsString(snap, "TruckId") ??
                            ReadPropAsString(snap, "TruckID") ??
                            "";

                        plate =
                            ReadPropAsString(snap, "TruckLicensePlate") ??
                            ReadPropAsString(snap, "LicensePlate") ??
                            "";

                        location =
                            ReadPropAsString(snap, "City") ??
                            ReadPropAsString(snap, "CurrentCity") ??
                            "";
                    }
                }
                catch { }

                new InspectionRecordStore().Add(
                    new InspectionRecord
                    {
                        InspectionType = type,
                        DriverName = ResolveDriverName(),
                        TruckName = truck,
                        UnitNumber = unit,
                        PlateNumber = plate,
                        Location = location,
                        Passed = !hasDefects,
                        Defects = hasDefects ? "Defects reported during inspection." : "No defects found.",
                        Notes = string.IsNullOrWhiteSpace(notes)
                            ? (hasDefects
                                ? "Inspection completed with defects."
                                : "Inspection completed with no defects found.")
                            : notes,
                        CreatedUtc = DateTime.UtcNow
                    });
            }
            catch { }
        }

        private void NoDefects_Click(object sender, RoutedEventArgs e)
        {
            if (_saveInProgress)
                return;

            try
            {
                foreach (var item in _items)
                {
                    item.IsOk = true;
                    item.IsDefect = false;
                }

                if (NotesBox != null && string.IsNullOrWhiteSpace(NotesBox.Text))
                    NotesBox.Text = "No defects found during inspection.";

                try { ChecklistList.Items.Refresh(); } catch { }

                // Users treat the green No Defects button as the submit button.
                // Do not show a modal popup here; submit and close immediately.
                Save_Click(sender, e);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"Unable to submit no-defects inspection.\n\n{ex.Message}",
                    "Inspection",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private static string ResolveDriverName()
        {
            try
            {
                var pairing = VtcPairingStore.Load();

                var name = FirstNonBlank(
                    GetProp(pairing, "DriverName"),
                    GetProp(pairing, "DisplayName"),
                    GetProp(pairing, "DiscordUsername"),
                    GetProp(pairing, "UserName"));

                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
            catch { }

            try
            {
                var cfg = VtcConfigService.Load(true);

                var name = FirstNonBlank(
                    GetProp(cfg, "DriverName"),
                    GetProp(cfg, "DisplayName"),
                    GetProp(cfg, "UserName"),
                    GetProp(GetPropObj(cfg, "Discord"), "Username"),
                    GetProp(GetPropObj(cfg, "Discord"), "DiscordUsername"));

                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
            catch { }

            return EldDriverIdentityResolver.DriverName();
        }

        private static object? GetPropObj(object? obj, string name)
        {
            try
            {
                return obj?.GetType().GetProperty(name)?.GetValue(obj);
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
                return obj?.GetType().GetProperty(name)?.GetValue(obj)?.ToString()?.Trim() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static string FirstNonBlank(params string[] values)
        {
            foreach (var v in values)
            {
                if (!string.IsNullOrWhiteSpace(v))
                    return v.Trim();
            }

            return "";
        }

        protected override void OnClosed(EventArgs e)
        {
            try { _telemetryTimer.Stop(); } catch { }
            base.OnClosed(e);
        }
    }
}