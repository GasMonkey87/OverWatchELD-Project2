using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace OverWatchELD.Overlay
{
    public partial class OverlayWindow : Window
    {
        private readonly DispatcherTimer _refreshTimer;
        private bool _locked = true;
        private bool _visible = true;

        public OverlayWindow()
        {
            InitializeComponent();

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _refreshTimer.Tick += (_, _) => RefreshOverlayData();
            _refreshTimer.Start();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Left = SystemParameters.WorkArea.Right - Width - 24;
            Top = SystemParameters.WorkArea.Top + 90;
            ApplyLockState();
            RefreshOverlayData();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_locked && e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { }
            }
        }

        public void ToggleVisible()
        {
            _visible = !_visible;
            Visibility = _visible ? Visibility.Visible : Visibility.Hidden;
        }

        public void ToggleClickThrough()
        {
            _locked = !_locked;
            ApplyLockState();
        }

        private void ApplyLockState()
        {
            // Pure WPF starter mode: locked reduces interaction and opacity.
            // If Win32 click-through is added later, this method is the correct hook point.
            Opacity = _locked ? 0.88 : 0.98;
            Cursor = _locked ? Cursors.Arrow : Cursors.SizeAll;
        }

        private void RefreshOverlayData()
        {
            try
            {
                var app = Application.Current as App;

                // Starter values. These are intentionally safe fallbacks until the ATS telemetry fields
                // are mapped into this view.
                DutyStatusText.Text = "ON DUTY";
                HosText.Text = DateTime.Now.ToString("HH:mm");
                LoadText.Text = "OverWatch ELD Ready";
                RouteText.Text = "Waiting for active load / ATS telemetry";
                SpeedText.Text = "0 MPH";
                FuelText.Text = "--";
                MaintenanceText.Text = app?.Telemetry != null ? "READY" : "OFFLINE";
            }
            catch
            {
                // Never let overlay refresh errors crash the main ELD.
            }
        }
    }
}
