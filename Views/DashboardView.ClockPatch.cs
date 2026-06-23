using OverWatchELD.Services;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace OverWatchELD.Views
{
    public partial class DashboardView
    {
        private DispatcherTimer? _visibleClockPatchTimer;

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);

            try
            {
                _visibleClockPatchTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };

                _visibleClockPatchTimer.Tick += (_, _) => ForceVisibleClockRefresh();
                _visibleClockPatchTimer.Start();
            }
            catch
            {
            }
        }

        private void ForceVisibleClockRefresh()
        {
            try
            {
                var snapshot = DashboardSnapshotProvider.BuildSnapshot();

                SetBoundClockText("DriveTime", FormatClock(snapshot.DriveRemaining));
                SetBoundClockText("ShiftTime", FormatClock(snapshot.ShiftRemaining));
                SetBoundClockText("BreakTime", FormatClock(snapshot.BreakRemaining));
                SetBoundClockText("CycleTime", FormatClock(snapshot.CycleRemaining));
            }
            catch
            {
            }
        }

        private void SetBoundClockText(string bindingPath, string value)
        {
            try
            {
                foreach (var textBlock in FindVisualChildren<TextBlock>(this))
                {
                    var binding = BindingOperations.GetBinding(textBlock, TextBlock.TextProperty);
                    var path = binding?.Path?.Path;

                    if (string.Equals(path, bindingPath, StringComparison.OrdinalIgnoreCase))
                    {
                        textBlock.Text = value;
                    }
                }
            }
            catch
            {
            }
        }

        private static string FormatClock(TimeSpan value)
        {
            if (value < TimeSpan.Zero)
                value = TimeSpan.Zero;

            var totalHours = (int)value.TotalHours;
            return $"{totalHours:00}:{value.Minutes:00}:{value.Seconds:00}";
        }

        private static System.Collections.Generic.IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
            where T : DependencyObject
        {
            if (parent == null)
                yield break;

            int count;
            try
            {
                count = VisualTreeHelper.GetChildrenCount(parent);
            }
            catch
            {
                yield break;
            }

            for (var i = 0; i < count; i++)
            {
                DependencyObject child;
                try
                {
                    child = VisualTreeHelper.GetChild(parent, i);
                }
                catch
                {
                    continue;
                }

                if (child is T typed)
                    yield return typed;

                foreach (var descendant in FindVisualChildren<T>(child))
                    yield return descendant;
            }
        }
    }
}
