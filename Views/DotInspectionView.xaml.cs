using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Linq;
using OverWatchELD.ViewModels;
using OverWatchELD.Services;

namespace OverWatchELD.Views
{
    public partial class DotInspectionView : UserControl
    {
        private readonly DotInspectionViewModel _vm = new DotInspectionViewModel();

        public DotInspectionView()
        {
            InitializeComponent();

            // Export shortcut
            this.PreviewKeyDown += DotInspectionView_PreviewKeyDown;
            DataContext = _vm;
        }

private void DotInspectionView_PreviewKeyDown(object sender, KeyEventArgs e)
{
    // Ctrl+E export DOT inspection table
    if (e.Key == Key.E && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
    {
        try
        {
            var days = _vm.Days ?? Enumerable.Empty<DotDaySummary>();
            var path = DotInspectionExportService.ExportDaysToCsv(days);
            MessageBox.Show($"DOT CSV exported:\n{path}", "ATS ELD", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed:\n{ex.Message}", "ATS ELD", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        e.Handled = true;
    }
}

        private void DotGrid_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (e.ChangedButton != MouseButton.Left) return;
                if (sender is not DataGrid grid) return;

                // Only open when the click is on an actual row (not header/empty space/scrollbar)
                var dep = e.OriginalSource as DependencyObject;
                var rowContainer = FindAncestor<DataGridRow>(dep);
                if (rowContainer?.Item is not DotDaySummary day) return;

                // Keep selection in sync
                grid.SelectedItem = day;

                // Combine TriggerText + TraceText into the one textbox (works with your current 9-arg ctor)
                var combined = day.TriggerText ?? "";
                if (!string.IsNullOrWhiteSpace(day.TraceText))
                    combined += Environment.NewLine + Environment.NewLine + "=== SEGMENT TRACE ===" + Environment.NewLine + day.TraceText;

                var dbg = new ShiftDebugViewModel(
                    day.Day,
                    day.ShiftStartLocalText ?? "-",
                    day.WindowEndLocalText ?? "-",
                    day.PausedText ?? "00:00",
                    day.EffectiveWindowEndLocalText ?? "-",
                    day.DriveInShiftText ?? "00:00",
                    day.DriveSinceBreakText ?? "00:00",
                    combined,
                    day.ViolationText ?? "OK"
                );

                var win = new ShiftDebugWindow(dbg)
                {
                    Owner = Window.GetWindow(this)
                };

                win.ShowDialog();
                e.Handled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.ToString(),
                    "DOT Double-Click Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
        {
            while (start != null)
            {
                if (start is T match) return match;
                start = VisualTreeHelper.GetParent(start);
            }
            return null;
        }
    }
}