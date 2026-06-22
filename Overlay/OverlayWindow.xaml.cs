using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace OverWatchELD.Overlay
{
    public partial class OverlayWindow : Window
    {
        private readonly DispatcherTimer _refreshTimer;
        private readonly DispatcherTimer _saveTimer;
        private readonly OverlaySettings _settings;
        private bool _loaded;
        private bool _expanded = true;
        private double _expandedWidth = 330;
        private double _expandedHeight = 455;

        public OverlayWindow()
        {
            InitializeComponent();

            _settings = OverlaySettings.Load();
            _expanded = !_settings.StartHidden;

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _refreshTimer.Tick += (_, _) => RefreshOverlayData();
            _refreshTimer.Start();

            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _saveTimer.Tick += (_, _) =>
            {
                _saveTimer.Stop();
                SaveCurrentSettings();
            };
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            RestorePosition();
            Opacity = _settings.ClampOpacity();
            _loaded = true;
            ApplyLockState();

            if (!_expanded)
                CollapseToTab();

            RefreshOverlayData();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveCurrentSettings();
        }

        private void Window_SaveRequested(object sender, EventArgs e)
        {
            QueueSave();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_expanded)
            {
                ExpandFromTab();
                return;
            }

            if (!_settings.Locked && e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { }
                QueueSave();
            }
        }

        private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_settings.Locked || !_expanded)
                return;

            _settings.Opacity += e.Delta > 0 ? 0.04 : -0.04;
            Opacity = _settings.ClampOpacity();
            QueueSave();
        }

        public void ToggleVisible()
        {
            if (_expanded)
                CollapseToTab();
            else
                ExpandFromTab();
        }

        public void ToggleOverlayLock()
        {
            _settings.Locked = !_settings.Locked;
            ApplyLockState();
            QueueSave();
        }

        private void CollapseToTab()
        {
            _expanded = false;
            _expandedWidth = Math.Max(Width, 300);
            _expandedHeight = Math.Max(Height, 390);
            Width = 112;
            Height = 44;
            MinWidth = 112;
            MinHeight = 44;
            MaxWidth = 112;
            MaxHeight = 44;
            OverlayStateText.Text = "SHOW";
            DutyStatusText.Text = "ELD";
            HosText.Text = "F9";
            DriverText.Text = "Click to open";
            LoadText.Text = "";
            RouteText.Text = "";
            SpeedText.Text = "";
            FuelText.Text = "";
            MaintenanceText.Text = "";
            StatusText.Text = "";
            ResizeMode = ResizeMode.NoResize;
        }

        private void ExpandFromTab()
        {
            _expanded = true;
            MaxWidth = double.PositiveInfinity;
            MaxHeight = double.PositiveInfinity;
            MinWidth = 300;
            MinHeight = 390;
            Width = _expandedWidth;
            Height = _expandedHeight;
            ApplyLockState();
            RefreshOverlayData();
            Activate();
            Focus();
        }

        private void ApplyLockState()
        {
            if (!_expanded)
                return;

            if (_settings.Locked)
            {
                OverlayStateText.Text = "Locked Route Advisor Overlay";
                Cursor = Cursors.Arrow;
                ResizeMode = ResizeMode.NoResize;
            }
            else
            {
                OverlayStateText.Text = "Unlocked - drag panel / wheel opacity";
                Cursor = Cursors.SizeAll;
                ResizeMode = ResizeMode.CanResizeWithGrip;
            }
        }

        private void RestorePosition()
        {
            var workArea = SystemParameters.WorkArea;

            if (double.IsNaN(_settings.Left) || double.IsNaN(_settings.Top))
            {
                Left = workArea.Right - Width - 24;
                Top = workArea.Top + 90;
                return;
            }

            Left = Math.Max(workArea.Left, Math.Min(_settings.Left, workArea.Right - 120));
            Top = Math.Max(workArea.Top, Math.Min(_settings.Top, workArea.Bottom - 80));
        }

        private void QueueSave()
        {
            if (!_loaded)
                return;

            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void SaveCurrentSettings()
        {
            if (!_loaded || !_expanded)
                return;

            _settings.Left = Left;
            _settings.Top = Top;
            _settings.Opacity = Opacity;
            _settings.Save();
        }

        private void RefreshOverlayData()
        {
            if (!_expanded)
                return;

            try
            {
                var snapshot = BuildSnapshot();
                ApplySnapshot(snapshot);
            }
            catch
            {
                // Never let overlay refresh errors crash the main ELD.
            }
        }

        private OverlaySnapshot BuildSnapshot()
        {
            var app = Application.Current as App;

            return new OverlaySnapshot
            {
                DutyStatus = "ON DUTY",
                HosRemaining = DateTime.Now.ToString("HH:mm"),
                DriverName = "OverWatch Driver",
                LoadName = "OverWatch ELD Ready",
                Route = "Waiting for active load / ATS telemetry",
                Speed = "0 MPH",
                Fuel = "--",
                Maintenance = app?.Telemetry != null ? "READY" : "OFFLINE",
                StatusLine = "F9 now collapses to a small SHOW tab so the overlay can always come back.",
                UpdatedAt = DateTime.Now
            };
        }

        private void ApplySnapshot(OverlaySnapshot snapshot)
        {
            DutyStatusText.Text = snapshot.DutyStatus;
            HosText.Text = snapshot.HosRemaining;
            DriverText.Text = snapshot.DriverName;
            LoadText.Text = snapshot.LoadName;
            RouteText.Text = snapshot.Route;
            SpeedText.Text = snapshot.Speed;
            FuelText.Text = snapshot.Fuel;
            MaintenanceText.Text = snapshot.Maintenance;
            StatusText.Text = snapshot.StatusLine;
        }
    }
}
