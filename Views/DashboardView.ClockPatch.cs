using OverWatchELD.Services;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
            catch { }
        }

        private void ForceVisibleClockRefresh()
        {
            try
            {
                var snapshot = DashboardSnapshotProvider.BuildSnapshot();
                SetClockCards(
                    FormatClock(snapshot.DriveRemaining),
                    FormatClock(snapshot.ShiftRemaining),
                    FormatClock(snapshot.BreakRemaining),
                    FormatClock(snapshot.CycleRemaining));
            }
            catch { }
        }

        private void SetClockCards(string drive, string shift, string brk, string cycle)
        {
            try
            {
                var clockBlocks = FindVisualChildren<TextBlock>(this)
                    .Where(t => t.FontSize >= 24 && t.FontSize <= 27)
                    .Where(t => !string.IsNullOrWhiteSpace(t.Text))
                    .Take(4)
                    .ToList();

                if (clockBlocks.Count >= 4)
                {
                    clockBlocks[0].Text = drive;
                    clockBlocks[1].Text = shift;
                    clockBlocks[2].Text = brk;
                    clockBlocks[3].Text = cycle;
                }
            }
            catch { }
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
            try { count = VisualTreeHelper.GetChildrenCount(parent); }
            catch { yield break; }

            for (var i = 0; i < count; i++)
            {
                DependencyObject child;
                try { child = VisualTreeHelper.GetChild(parent, i); }
                catch { continue; }

                if (child is T typed)
                    yield return typed;

                foreach (var descendant in FindVisualChildren<T>(child))
                    yield return descendant;
            }
        }
    }
}
