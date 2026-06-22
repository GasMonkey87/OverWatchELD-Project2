using OverWatchELD.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace OverWatchELD.Views
{
    public partial class FleetManagerWindow : Window
    {
        private readonly ObservableCollection<DriverFleetStats> _rows = new();

        public FleetManagerWindow()
        {
            InitializeComponent();
            GridDrivers.ItemsSource = _rows;
            Loaded += FleetManagerWindow_Loaded;
        }

        private async void FleetManagerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshAsync();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            try
            {
                StatusText.Text = "Loading…";

                var (fromUtc, toUtc) = GetRangeUtc();
                var data = await FleetMetricsService.GetDriverStatsAsync(fromUtc, toUtc);

                _rows.Clear();
                foreach (var row in data)
                    _rows.Add(row);

                StatusText.Text = $"Loaded {_rows.Count} driver(s). DB: {System.IO.Path.GetFileName(FleetDb.GetDbPath())}";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Failed";
                MessageBox.Show(ex.ToString(), "Fleet Manager Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private (DateTime fromUtc, DateTime toUtc) GetRangeUtc()
        {
            var nowUtc = DateTime.UtcNow;
            var toUtc = nowUtc;

            var idx = RangeCombo.SelectedIndex;
            if (idx == 0) // Today
            {
                var local = DateTime.Now.Date;
                var fromLocal = local;
                return (fromLocal.ToUniversalTime(), toUtc);
            }
            if (idx == 1) // 7 days
                return (nowUtc.AddDays(-7), toUtc);
            if (idx == 2) // 30 days
                return (nowUtc.AddDays(-30), toUtc);

            // All time
            return (DateTime.UnixEpoch, toUtc);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            try { Close(); } catch { }
        }
    }
}