using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OverWatchELD.ViewModels;
using OverWatchELD.Services;
using OverWatchELD.Models;
namespace OverWatchELD.Views
{
    public partial class DashboardView : UserControl
    {
        private bool _owToolbarInjected;

        internal void OverWatchInjectToolbar()
        {
            if (_owToolbarInjected) return;
            _owToolbarInjected = true;

            try
            {
                var root = FindFirstGrid(this) as FrameworkElement ?? FindFirstPanel(this) as FrameworkElement;
                if (root == null) return;
                if (root.FindName("OW_TopRightPanel") != null) return;

                var panel = new StackPanel
                {
                    Name = "OW_TopRightPanel",
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 10, 10, 0)
                };

                var resetBtn = new Button
                {
                    Content = "Reset Clocks",
                    Width = 110,
                    Height = 30,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                resetBtn.Click += (_, __) => TryResetClocks();

                var logOffBtn = new Button
                {
                    Content = "Log Off",
                    Width = 80,
                    Height = 30,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                logOffBtn.Click += (_, __) => TryLogOff();

                var gearBtn = new Button
                {
                    Content = "⚙",
                    Width = 36,
                    Height = 30
                };
                gearBtn.Click += (_, __) => OpenSettings();

                panel.Children.Add(resetBtn);
                panel.Children.Add(logOffBtn);
                panel.Children.Add(gearBtn);

                if (root is Grid g) g.Children.Add(panel);
                else if (root is Panel p) p.Children.Add(panel);
            }
            catch { }
        }

        private void TryResetClocks()
        {
            try
            {
                var result = HosResetService.ResetClocksNow();
                if (!result.Ok)
                {
                    MessageBox.Show(result.Message, "OverWatch ELD", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    var app = Application.Current as OverWatchELD.App;
                    if (app?.DutyMachine != null)
                    {
                        app.DutyMachine.TrySet(DutyStatus.OffDuty);
                        app.DutyMachine.Perform34HourReset();
                    }
                }
                catch { }

                try
                {
                    DashboardClocksLiveViewModel.Shared.RefreshNow();
                }
                catch { }

                try
                {
                    if (DataContext is DashboardViewModel vm)
                    {
                        _ = vm.RefreshAsync();
                    }
                    else
                    {
                        var m = DataContext?.GetType().GetMethod("RefreshAsync");
                        m?.Invoke(DataContext, null);
                    }
                }
                catch { }

                MessageBox.Show("Clocks reset to a fresh 34-hour OFF-DUTY restart.", "OverWatch ELD", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show("Clock reset failed.\n" + ex.Message, "OverWatch ELD", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TryLogOff()
        {
            try
            {
                if (Application.Current?.MainWindow is global::OverWatchELD.MainWindow mw)
                {
                    mw.LogOffAndReturnToLogin();
                    return;
                }
            }
            catch { }
        }

        private void OpenSettings()
        {
            try
            {
                var win = new SettingsWindow();
                win.Owner = Window.GetWindow(this);
                win.ShowDialog();
            }
            catch { }
        }

        private static DependencyObject? FindFirstGrid(DependencyObject root)
        {
            try
            {
                if (root is Grid) return root;
                var count = VisualTreeHelper.GetChildrenCount(root);
                for (int i = 0; i < count; i++)
                {
                    var c = VisualTreeHelper.GetChild(root, i);
                    var r = FindFirstGrid(c);
                    if (r != null) return r;
                }
            }
            catch { }
            return null;
        }

        private static DependencyObject? FindFirstPanel(DependencyObject root)
        {
            try
            {
                if (root is Panel) return root;
                var count = VisualTreeHelper.GetChildrenCount(root);
                for (int i = 0; i < count; i++)
                {
                    var c = VisualTreeHelper.GetChild(root, i);
                    var r = FindFirstPanel(c);
                    if (r != null) return r;
                }
            }
            catch { }
            return null;
        }
    }
}
