using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OverWatchELD.Services;
using OverWatchELD.ViewModels;

namespace OverWatchELD.Views
{
    public partial class LogsView : UserControl
    {
        private LogsViewModel? VM => DataContext as LogsViewModel;

        public LogsView()
        {
            InitializeComponent();
            DataContext ??= new LogsViewModel();

            Loaded += LogsView_Loaded;
            SizeChanged += (_, __) => UpdateGraphWidthFromLayout();
        }

        private void LogsView_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateGraphWidthFromLayout();
            VM?.Refresh();
        }

        private void PrevDay_Click(object sender, RoutedEventArgs e) => VM?.PrevDay();
        private void NextDay_Click(object sender, RoutedEventArgs e) => VM?.NextDay();

        private void UnsignedLogs_Click(object sender, RoutedEventArgs e)
        {
            OpenCertificationWindow();
        }

        private void Certify_Click(object sender, RoutedEventArgs e)
        {
            OpenCertificationWindow();
        }

        private void OpenCertificationWindow()
        {
            try
            {
                var win = new DailyLogCertificationWindow(VM ?? new LogsViewModel())
                {
                    Owner = Window.GetWindow(this)
                };
                win.ShowDialog();
                VM?.Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.GetBaseException().Message, "Certification", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ---- EDIT DUTY EVENTS ----

        private void DutyGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (VM == null) return;
            if (DutyGrid.SelectedItem is not DutyEventRowVm row) return;

            OpenEdit(row.Model);
        }

        private void Segment_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (VM == null) return;

            if (sender is not Border b) return;
            if (b.DataContext is not GraphSegmentVm seg) return;

            // Find matching row/model by ID
            var row = VM.DutyEvents.FirstOrDefault(r => r.Model.Id == seg.EventId);
            if (row == null) return;

            OpenEdit(row.Model);
        }

        private void OpenEdit(OverWatchELD.Models.DutyEvent model)
        {
            try
            {
                var dlg = new EditDutyDialog(model)
                {
                    Owner = Window.GetWindow(this)
                };

                if (dlg.ShowDialog() == true)
                {
                    // Save + refresh (EditDutyDialog edits the same DutyEvent instance)
                    DatabaseService.UpdateDutyEvent(model);
                    VM?.Refresh();
                }

            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.GetBaseException().Message, "Edit Duty Event", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateGraphWidthFromLayout()
        {
            if (VM == null) return;

            var full = ActualWidth;
            if (double.IsNaN(full) || full <= 0) return;

            // keep stable; don't touch the layout, just inform the VM
            VM.GraphWidth = Math.Max(240, full - 120);
        }
    }
}
