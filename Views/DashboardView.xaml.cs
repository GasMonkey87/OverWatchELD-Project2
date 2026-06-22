using OverWatchELD.Services;
using OverWatchELD.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace OverWatchELD.Views
{
    public partial class DashboardView : UserControl
    {
        private DashboardViewModel? VM => DataContext as DashboardViewModel;

        private Action<TelemetrySnapshot>? _telemetryHook;

        private readonly DispatcherTimer _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        private readonly DispatcherTimer _toastTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };

        private DispatcherTimer? _dashboardToastTimer;
        private Border? _dashboardToastBorder;
        private TextBlock? _dashboardToastTitle;
        private TextBlock? _dashboardToastMessage;

        private readonly Dictionary<string, DateTimeOffset> _dashboardAlertLastShownUtc = new(StringComparer.OrdinalIgnoreCase);
        private string _lastDashboardWarningText = "";

        public DashboardView()
        {
            InitializeComponent();

            DashboardToastService.ToastRequested += DashboardToastService_ToastRequested;
            BuildDashboardToastHost();

            Loaded += DashboardView_Loaded;
            Unloaded += DashboardView_Unloaded;

            _clockTimer.Tick += (_, __) =>
            {
                try { VM?.Tick(); } catch { }
                try { TryRefreshTruckFromTelemetry(); } catch { }
            };

            _toastTimer.Tick += (_, __) =>
            {
                try
                {
                    _toastTimer.Stop();
                    DashboardToast.Visibility = Visibility.Collapsed;
                }
                catch { }
            };
        }

        private void ResetClocks_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = VM;
                if (vm == null)
                    return;

                var names = new[]
                {
                    "ResetClocks", "ResetAllClocks", "ResetHosClocks", "ResetTimers", "Reset"
                };

                foreach (var name in names)
                {
                    var method = vm.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                    if (method == null)
                        continue;

                    method.Invoke(vm, null);
                    try { vm.Tick(); } catch { }
                    return;
                }

                try { vm.Tick(); } catch { }
            }
            catch { }
        }

        private void ShowDashboardToast(string title, string message, string color = "#38BDF8")
        {
            try
            {
                DashboardToastTitle.Text = title;
                DashboardToastMessage.Text = message;

                DashboardToast.BorderBrush =
                    new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString(color));

                DashboardToast.Visibility = Visibility.Visible;

                _toastTimer.Stop();
                _toastTimer.Start();
            }
            catch { }
        }

        private void BuildDashboardToastHost()
        {
            if (Content is not Grid root)
                return;

            _dashboardToastTitle = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 15,
                FontWeight = FontWeights.Bold
            };

            _dashboardToastMessage = new TextBlock
            {
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CBD5E1")),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };

            var stack = new StackPanel();
            stack.Children.Add(_dashboardToastTitle);
            stack.Children.Add(_dashboardToastMessage);

            _dashboardToastBorder = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0B1424")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38BDF8")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14),
                Width = 360,
                Opacity = 0,
                Visibility = Visibility.Collapsed,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 18, 18, 0),
                Child = stack
            };

            Panel.SetZIndex(_dashboardToastBorder, 9999);
            root.Children.Add(_dashboardToastBorder);

            _dashboardToastTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };

            _dashboardToastTimer.Tick += (_, _) => HideDashboardToast();
        }

        private void DashboardToastService_ToastRequested(object? sender, DashboardToastEventArgs e)
        {
            ShowDashboardToast(e.Title, e.Message, e.Type);
        }

        private void ShowDashboardToast(string title, string message, DashboardToastType type)
        {
            if (_dashboardToastBorder == null || _dashboardToastTitle == null || _dashboardToastMessage == null)
                return;

            _dashboardToastTitle.Text = title;
            _dashboardToastMessage.Text = message;

            _dashboardToastBorder.BorderBrush = type switch
            {
                DashboardToastType.Success => Brush("#22C55E"),
                DashboardToastType.Warning => Brush("#F59E0B"),
                DashboardToastType.Danger => Brush("#EF4444"),
                DashboardToastType.Message => Brush("#38BDF8"),
                DashboardToastType.Malfunction => Brush("#EF4444"),
                _ => Brush("#38BDF8")
            };

            _dashboardToastBorder.Background = type switch
            {
                DashboardToastType.Success => Brush("#052E1A"),
                DashboardToastType.Warning => Brush("#2A1F08"),
                DashboardToastType.Danger => Brush("#2B0B0B"),
                DashboardToastType.Malfunction => Brush("#2B0B0B"),
                _ => Brush("#0B1424")
            };

            _dashboardToastBorder.Visibility = Visibility.Visible;
            _dashboardToastBorder.Opacity = 1;

            _dashboardToastTimer?.Stop();
            _dashboardToastTimer?.Start();
        }

        private void HideDashboardToast()
        {
            _dashboardToastTimer?.Stop();

            if (_dashboardToastBorder == null)
                return;

            _dashboardToastBorder.Opacity = 0;
            _dashboardToastBorder.Visibility = Visibility.Collapsed;
        }

        private static SolidColorBrush Brush(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }

        private void DashboardView_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is not DashboardViewModel)
                DataContext = new DashboardViewModel();

            VM?.Tick();
            VM?.OverWatchRefreshDriverName();

            TryRefreshTruckFromTelemetry();

            try
            {
                if (!_clockTimer.IsEnabled)
                    _clockTimer.Start();
            }
            catch { }

            try
            {
                var app = Application.Current as App;
                var tel = app?.Telemetry;

                if (tel != null)
                {
                    _telemetryHook ??= (snap) =>
                    {
                        try
                        {
                            Dispatcher.Invoke(() =>
                            {
                                try { VM?.Tick(); } catch { }
                                UpdateTruckConnection(snap);
                            });
                        }
                        catch { }
                    };

                    tel.Updated -= _telemetryHook;
                    tel.Updated += _telemetryHook;
                }
            }
            catch { }

            try
            {
                var host = Window.GetWindow(this) ?? Application.Current?.MainWindow;

                if (host != null)
                    ForceHostDark(host);
            }
            catch { }
        }

        private void DashboardView_Unloaded(object sender, RoutedEventArgs e)
        {
            try { _clockTimer.Stop(); } catch { }

            try
            {
                DashboardToastService.ToastRequested -= DashboardToastService_ToastRequested;
            }
            catch { }

            try
            {
                var app = Application.Current as App;
                var tel = app?.Telemetry;

                if (tel != null && _telemetryHook != null)
                    tel.Updated -= _telemetryHook;
            }
            catch { }
        }

        private void Nav(string routeLower)
        {
            try
            {
                (Application.Current?.MainWindow as MainWindow)
                    ?.NavigateTo(routeLower);
            }
            catch { }
        }

        private void NavLogs_Click(object sender, RoutedEventArgs e) => Nav("logs");
        private void NavMessages_Click(object sender, RoutedEventArgs e) => Nav("messages");
        private void NavSupport_Click(object sender, RoutedEventArgs e) => Nav("support");
        private void NavDocuments_Click(object sender, RoutedEventArgs e) => Nav("documents");

        private void OpenUnsignedLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vmForCert = new LogsViewModel();

                var win = new DailyLogCertificationWindow(vmForCert)
                {
                    Owner = Window.GetWindow(this) ?? Application.Current?.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                win.ShowDialog();
            }
            catch { }

            try
            {
                if (DataContext is DashboardViewModel vm)
                {
                    vm.RefreshUnsignedLogsCount();
                    vm.Tick();
                }
            }
            catch { }
        }

        private void NavPreTrip_Click(object sender, RoutedEventArgs e) => OpenInspection("PreTrip");
        private void NavPostTrip_Click(object sender, RoutedEventArgs e) => OpenInspection("PostTrip");
        private void NavVehicleInspection_Click(object sender, RoutedEventArgs e) => OpenInspection("VehicleInspection");

        private void OpenInspection(string kind)
        {
            try
            {
                Window win;

                try
                {
                    win = (Window)Activator.CreateInstance(typeof(InspectionEntryWindow), kind)!;
                }
                catch
                {
                    win = new InspectionEntryWindow();
                }

                var owner = Window.GetWindow(this) ?? Application.Current?.MainWindow;

                if (owner != null)
                    win.Owner = owner;

                win.Tag = kind;
                win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to open Inspection.\n\n{ex.Message}",
                    "Open Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ConnectVehicle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var app = Application.Current as App;
                var telemetry = app?.Telemetry;

                try { app?.TryAutoStartTelemetry(); } catch { }
                try { telemetry?.Start(); } catch { }

                if (telemetry == null)
                {
                    MessageBox.Show(
                        "Telemetry service is not initialized.",
                        "Connect to Vehicle",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                var win = new ConnectVehicleWindow(telemetry)
                {
                    Owner = Window.GetWindow(this) ?? Application.Current?.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                win.ShowDialog();

                try { VM?.Tick(); } catch { }
                try { TryRefreshTruckFromTelemetry(); } catch { }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to open Connect window.\n\n{ex.Message}",
                    "Connect to Vehicle",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void TryRefreshTruckFromTelemetry()
        {
            try
            {
                var app = Application.Current as App;
                var tel = app?.Telemetry;

                if (tel == null)
                {
                    UpdateTruckConnection(null);
                    return;
                }

                var prop = tel.GetType().GetProperty(
                    "LastSnapshot",
                    BindingFlags.Public | BindingFlags.Instance);

                if (prop?.GetValue(tel) is TelemetrySnapshot snap)
                {
                    UpdateTruckConnection(snap);
                    return;
                }

                UpdateTruckConnection(null);
            }
            catch
            {
                UpdateTruckConnection(null);
            }
        }

        private void UpdateTruckConnection(TelemetrySnapshot? snapshot)
        {
            try
            {
                bool connected =
                    snapshot != null &&
                    (
                        snapshot.EngineOn ||
                        !string.IsNullOrWhiteSpace(snapshot.TruckId) ||
                        !string.IsNullOrWhiteSpace(snapshot.TruckName) ||
                        !string.IsNullOrWhiteSpace(snapshot.TruckMakeModel)
                    );

                if (connected)
                {
                    TruckConnectionStatusText.Text = "Connected";
                    TruckConnectionBadgeText.Text = "ONLINE";
                    TruckConnectionBadgeText.Foreground =
                        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E"));

                    try
                    {
                        double speed = NormalizePercent(ReadDouble(snapshot,
                            "Speed", "SpeedMph", "TruckSpeed", "SpeedMPH", "GameSpeed"), false);

                        double fuel = NormalizePercent(ReadDouble(snapshot,
                            "FuelPercent", "FuelPercentage", "FuelPct", "TruckFuelPercent", "TruckFuelPct",
                            "Fuel", "TruckFuel", "FuelLevel", "FuelRatio"));

                        double truckDamage = NormalizePercent(ReadDouble(snapshot,
                            "DamagePct", "TruckDamagePct", "TruckDamagePercent", "DamagePercent",
                            "TruckDamage", "Damage", "WearPct", "WearPercent", "TruckWear", "TruckWearPercent",
                            "ChassisWear", "CabinWear", "EngineWear", "TransmissionWear", "WheelsWear", "WearEngine"));

                        double trailerDamage = NormalizePercent(ReadDouble(snapshot,
                            "TrailerDamagePct", "TrailerDamagePercent", "TrailerDamage",
                            "TrailerWearPct", "TrailerWearPercent", "TrailerWear",
                            "TrailerChassisWear", "TrailerBodyWear", "TrailerCargoWear", "CargoDamage", "CargoDamagePercent",
                            "TrailerWearBody", "TrailerWearChassis", "TrailerWearWheels"));

                        double destination = ReadDouble(snapshot,
                            "DistanceToDestination", "NavigationDistanceMiles", "RouteDistanceMiles", "RemainingMiles");

                        string truck = ReadString(snapshot,
                            "TruckName", "TruckMakeModel", "TruckModel", "Model");

                        string truckId = ReadString(snapshot,
                            "TruckId", "AssignedTruckNumber", "TruckNumber");

                        TruckNumberText.Text = !string.IsNullOrWhiteSpace(truckId)
                            ? $"Truck #: {truckId}"
                            : "Truck #: N/A";

                        TruckNameText.Text = !string.IsNullOrWhiteSpace(truck)
                            ? $"Truck: {truck}"
                            : "Truck: Unknown Truck";

                        TruckDamageText.Text = $"Damage: Truck {ClampPercent(truckDamage):0}% • Trailer {ClampPercent(trailerDamage):0}%";

                        TruckSpeedText.Text = $"Speed: {Math.Max(0, speed):0} MPH";

                        TruckFuelText.Text = fuel > 0
                            ? $"Fuel: {ClampPercent(fuel):0}%"
                            : "Fuel: --";

                        MilesToDestinationText.Text = destination > 0
                            ? $"Destination: {destination:0} mi"
                            : "Destination: --";

                        string warning = BuildDashboardWarningText(snapshot, fuel, truckDamage, trailerDamage, destination, speed);

                        TruckWarningsText.Text = $"Warnings: {warning}";

                        TryShowDashboardAlerts(snapshot, warning, fuel, truckDamage, trailerDamage, destination, speed);
                    }
                    catch
                    {
                    }
                }
                else
                {
                    ResetTruckInfo();
                }
            }
            catch
            {
                ResetTruckInfo();
            }
        }


        private string BuildDashboardWarningText(
            TelemetrySnapshot snapshot,
            double fuel,
            double truckDamage,
            double trailerDamage,
            double destination,
            double speed)
        {
            string warning = "None";

            if (fuel > 0 && fuel <= 10)
                warning = AddWarning(warning, "LOW FUEL");

            if (truckDamage >= 15)
                warning = AddWarning(warning, "TRUCK DAMAGE");

            if (trailerDamage >= 15)
                warning = AddWarning(warning, "TRAILER DAMAGE");

            if (ReadBool(snapshot, "AirPressureWarningOn", "AirWarningOn", "LowAirWarning", "AirPressureEmergencyOn"))
                warning = AddWarning(warning, "AIR PRESSURE");

            if (ReadBool(snapshot, "OilPressureWarningOn", "OilWarningOn"))
                warning = AddWarning(warning, "OIL PRESSURE");

            if (ReadBool(snapshot, "BatteryVoltageWarningOn", "BatteryWarningOn"))
                warning = AddWarning(warning, "BATTERY");

            if (ReadBool(snapshot, "WaterTemperatureWarningOn", "WaterTempWarningOn", "EngineTemperatureWarningOn"))
                warning = AddWarning(warning, "ENGINE TEMP");

            if (ReadBool(snapshot, "FuelWarningOn", "LowFuelWarningOn"))
                warning = AddWarning(warning, "LOW FUEL");

            if (ReadBool(snapshot, "MalfunctionActive", "HasMalfunction", "ActiveMalfunction", "NeedsAttention"))
                warning = AddWarning(warning, "MALFUNCTION");

            if (ReadBool(snapshot, "HosViolation", "HOSViolation", "HasHosViolation", "TimeViolation"))
                warning = AddWarning(warning, "HOS VIOLATION");

            if (ReadBool(snapshot, "BreakDue", "BreakRequired", "BreakWarning", "RestBreakDue"))
                warning = AddWarning(warning, "BREAK DUE");

            if (ReadBool(snapshot, "ShiftLimitWarning", "DriveLimitWarning", "CycleLimitWarning"))
                warning = AddWarning(warning, "TIME ALERT");

            if (destination > 0 && destination <= 25)
                warning = AddWarning(warning, "NEAR DESTINATION");

            if (Math.Abs(speed) > 80)
                warning = AddWarning(warning, "HIGH SPEED");

            return warning;
        }

        private void TryShowDashboardAlerts(
            TelemetrySnapshot snapshot,
            string warning,
            double fuel,
            double truckDamage,
            double trailerDamage,
            double destination,
            double speed)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(warning) ||
                    warning.Equals("None", StringComparison.OrdinalIgnoreCase))
                {
                    _lastDashboardWarningText = "";
                    return;
                }

                var truck = FirstNonEmpty(
                    ReadString(snapshot, "TruckName", "TruckMakeModel", "TruckModel", "Model"),
                    "Truck");

                var location = FirstNonEmpty(
                    JoinNonEmpty(ReadString(snapshot, "City"), ReadString(snapshot, "State")),
                    ReadString(snapshot, "Location"),
                    "current location");

                var driver = FirstNonEmpty(
                    ReadString(snapshot, "DriverName", "Driver", "DiscordUsername", "Username"),
                    "Driver");

                if (fuel > 0 && fuel <= 10)
                {
                    ShowThrottledAlert(
                        "LOW_FUEL",
                        "⛽ Low Fuel",
                        $"{driver}, fuel is down to {fuel:0}% in {truck}. Location: {location}.",
                        DashboardToastType.Warning,
                        TimeSpan.FromMinutes(5));
                }

                if (truckDamage >= 15)
                {
                    ShowThrottledAlert(
                        "TRUCK_DAMAGE",
                        "🛠️ Truck Damage Alert",
                        $"{truck} has {truckDamage:0}% truck damage. Stop for repair when safe. Location: {location}.",
                        truckDamage >= 40 ? DashboardToastType.Danger : DashboardToastType.Malfunction,
                        TimeSpan.FromMinutes(5));
                }

                if (trailerDamage >= 15)
                {
                    ShowThrottledAlert(
                        "TRAILER_DAMAGE",
                        "⚠️ Trailer Damage Alert",
                        $"Trailer damage is {trailerDamage:0}%. Check load/trailer condition. Location: {location}.",
                        trailerDamage >= 40 ? DashboardToastType.Danger : DashboardToastType.Warning,
                        TimeSpan.FromMinutes(5));
                }

                if (ReadBool(snapshot, "AirPressureWarningOn", "AirWarningOn", "LowAirWarning", "AirPressureEmergencyOn"))
                {
                    ShowThrottledAlert(
                        "AIR_PRESSURE",
                        "🚨 Air Pressure Warning",
                        $"Air pressure warning active in {truck}. Pull over safely and inspect the vehicle.",
                        DashboardToastType.Danger,
                        TimeSpan.FromMinutes(4));
                }

                if (ReadBool(snapshot, "OilPressureWarningOn", "OilWarningOn"))
                {
                    ShowThrottledAlert(
                        "OIL_PRESSURE",
                        "🚨 Oil Pressure Warning",
                        $"Oil pressure warning active in {truck}. Stop and inspect before continuing.",
                        DashboardToastType.Danger,
                        TimeSpan.FromMinutes(4));
                }

                if (ReadBool(snapshot, "BatteryVoltageWarningOn", "BatteryWarningOn"))
                {
                    ShowThrottledAlert(
                        "BATTERY",
                        "🔋 Battery Warning",
                        $"Battery voltage warning active in {truck}. Monitor electrical systems.",
                        DashboardToastType.Warning,
                        TimeSpan.FromMinutes(5));
                }

                if (ReadBool(snapshot, "WaterTemperatureWarningOn", "WaterTempWarningOn", "EngineTemperatureWarningOn"))
                {
                    ShowThrottledAlert(
                        "ENGINE_TEMP",
                        "🌡️ Engine Temperature Warning",
                        $"Engine temperature warning active in {truck}. Reduce load and stop if temperature rises.",
                        DashboardToastType.Danger,
                        TimeSpan.FromMinutes(4));
                }

                if (ReadBool(snapshot, "MalfunctionActive", "HasMalfunction", "ActiveMalfunction", "NeedsAttention"))
                {
                    var malText = FirstNonEmpty(
                        ReadString(snapshot, "MalfunctionText", "ActiveMalfunctionText", "CurrentIssue", "IssueText"),
                        "Vehicle malfunction detected.");

                    ShowThrottledAlert(
                        "MALFUNCTION",
                        "🧰 Malfunction Alert",
                        $"{malText} Location: {location}.",
                        DashboardToastType.Malfunction,
                        TimeSpan.FromMinutes(5));
                }

                if (ReadBool(snapshot, "HosViolation", "HOSViolation", "HasHosViolation", "TimeViolation"))
                {
                    ShowThrottledAlert(
                        "HOS_VIOLATION",
                        "⏱️ HOS Violation",
                        "Hours-of-service warning/violation detected. Check your Logs screen now.",
                        DashboardToastType.Danger,
                        TimeSpan.FromMinutes(5));
                }

                if (ReadBool(snapshot, "BreakDue", "BreakRequired", "BreakWarning", "RestBreakDue"))
                {
                    ShowThrottledAlert(
                        "BREAK_DUE",
                        "☕ Break Alert",
                        "Break/rest warning detected. Check your remaining time before continuing.",
                        DashboardToastType.Warning,
                        TimeSpan.FromMinutes(5));
                }

                if (ReadBool(snapshot, "ShiftLimitWarning", "DriveLimitWarning", "CycleLimitWarning"))
                {
                    ShowThrottledAlert(
                        "TIME_ALERT",
                        "⏰ Time Alert",
                        "Driving/shift/cycle time alert detected. Review your duty clocks.",
                        DashboardToastType.Warning,
                        TimeSpan.FromMinutes(5));
                }

                if (destination > 0 && destination <= 25)
                {
                    ShowThrottledAlert(
                        "NEAR_DESTINATION",
                        "📍 Near Destination",
                        $"You are about {destination:0} miles from destination.",
                        DashboardToastType.Message,
                        TimeSpan.FromMinutes(10));
                }

                if (Math.Abs(speed) > 80)
                {
                    ShowThrottledAlert(
                        "HIGH_SPEED",
                        "🚨 Speed Alert",
                        $"Speed is {Math.Abs(speed):0} MPH. Slow down and drive safely.",
                        DashboardToastType.Warning,
                        TimeSpan.FromMinutes(3));
                }

                if (!warning.Equals(_lastDashboardWarningText, StringComparison.OrdinalIgnoreCase))
                {
                    _lastDashboardWarningText = warning;
                    ShowThrottledAlert(
                        "WARNING_SUMMARY",
                        "⚠️ Dashboard Warnings",
                        warning,
                        warning.Contains("DAMAGE", StringComparison.OrdinalIgnoreCase) ||
                        warning.Contains("MALFUNCTION", StringComparison.OrdinalIgnoreCase)
                            ? DashboardToastType.Malfunction
                            : DashboardToastType.Warning,
                        TimeSpan.FromMinutes(2));
                }
            }
            catch
            {
            }
        }

        private void ShowThrottledAlert(
            string key,
            string title,
            string message,
            DashboardToastType type,
            TimeSpan cooldown)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;

                if (_dashboardAlertLastShownUtc.TryGetValue(key, out var last) &&
                    now - last < cooldown)
                    return;

                _dashboardAlertLastShownUtc[key] = now;
                ShowDashboardToast(title, message, type);
            }
            catch
            {
            }
        }

        private static bool ReadBool(object? obj, params string[] names)
        {
            if (obj == null)
                return false;

            foreach (var name in names)
            {
                try
                {
                    var p = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                    if (p == null)
                        continue;

                    var v = p.GetValue(obj);

                    if (v == null)
                        continue;

                    if (v is bool bb)
                        return bb;

                    if (v is int ii)
                        return ii != 0;

                    if (v is long ll)
                        return ll != 0;

                    if (v is double dd)
                        return Math.Abs(dd) > 0.0001;

                    if (bool.TryParse(v.ToString(), out var parsedBool))
                        return parsedBool;

                    if (double.TryParse(v.ToString(), out var parsedDouble))
                        return Math.Abs(parsedDouble) > 0.0001;
                }
                catch
                {
                }
            }

            return false;
        }

        private static string JoinNonEmpty(params string?[] values)
        {
            return string.Join(", ", values.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }

        private static string AddWarning(string current, string next)
        {
            if (string.IsNullOrWhiteSpace(current) || current.Equals("None", StringComparison.OrdinalIgnoreCase))
                return next;

            if (current.Contains(next, StringComparison.OrdinalIgnoreCase))
                return current;

            return current + " • " + next;
        }

        private static string ReadString(object? obj, params string[] names)
        {
            if (obj == null)
                return "";

            foreach (var name in names)
            {
                try
                {
                    var p = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                    if (p == null)
                        continue;

                    var v = p.GetValue(obj)?.ToString();

                    if (!string.IsNullOrWhiteSpace(v))
                        return v.Trim();
                }
                catch { }
            }

            return "";
        }

        private void ResetTruckInfo()
        {
            try
            {
                TruckConnectionStatusText.Text = "Disconnected";
                TruckNumberText.Text = "Truck #: N/A";
                TruckNameText.Text = "Truck: No Vehicle Connected";

                TruckConnectionBadgeText.Text = "OFFLINE";
                TruckConnectionBadgeText.Foreground =
                    new SolidColorBrush((Color)ColorConverter.ConvertFromString("#AFC0D7"));

                TruckDamageText.Text = "Damage: Truck -- • Trailer --";
                TruckSpeedText.Text = "Speed: --";
                TruckFuelText.Text = "Fuel: --";
                MilesToDestinationText.Text = "Destination: --";
                TruckWarningsText.Text = "Warnings: None";
            }
            catch { }
        }

        private static double ReadDouble(object? obj, params string[] names)
        {
            if (obj == null)
                return 0;

            foreach (var name in names)
            {
                try
                {
                    var p = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                    if (p == null)
                        continue;

                    var v = p.GetValue(obj);

                    if (v == null)
                        continue;

                    if (v is double dd) return dd;
                    if (v is float ff) return ff;
                    if (v is decimal mm) return (double)mm;
                    if (v is int ii) return ii;
                    if (v is long ll) return ll;

                    var text = v.ToString()?.Trim().Replace("%", "").Replace(",", "") ?? "";

                    if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                        return d;

                    if (double.TryParse(text, out d))
                        return d;
                }
                catch { }
            }

            return 0;
        }

        private static double NormalizePercent(double value, bool convertFraction = true)
        {
            if (convertFraction && value > 0 && value <= 1)
                value *= 100;

            return value;
        }

        private static double ClampPercent(double value)
        {
            if (value < 0) return 0;
            if (value > 100) return 100;
            return value;
        }

        private static void ForceHostDark(DependencyObject root)
        {
            var dark = (SolidColorBrush)new BrushConverter().ConvertFromString("#0B1220");

            try
            {
                if (root is Window w)
                    w.Background = dark;

                int count = VisualTreeHelper.GetChildrenCount(root);

                for (int i = 0; i < count; i++)
                {
                    var child = VisualTreeHelper.GetChild(root, i);

                    if (child is Border b && b.Background is SolidColorBrush sb1 && IsNearWhite(sb1.Color))
                        b.Background = Brushes.Transparent;

                    if (child is Panel p && p.Background is SolidColorBrush sb2 && IsNearWhite(sb2.Color))
                        p.Background = Brushes.Transparent;

                    if (child is Control c && c.Background is SolidColorBrush sb3 && IsNearWhite(sb3.Color))
                        c.Background = Brushes.Transparent;

                    if (child is Frame f)
                        f.Background = dark;

                    ForceHostDark(child);
                }
            }
            catch { }
        }

        private static bool IsNearWhite(Color c)
        {
            return c.R > 235 && c.G > 235 && c.B > 235;
        }
    }
}
