using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace OverWatchELD.Views.Fleet
{
    public sealed class FleetToastWindow : Window
    {
        private readonly DispatcherTimer _timer = new DispatcherTimer();
        private int _ticks;

        public FleetToastWindow(string text)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            Width = 360;
            Height = 86;

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(230, 25, 25, 25)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
                BorderThickness = new Thickness(1)
            };

            border.Child = new TextBlock
            {
                Text = text ?? "",
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13
            };

            Content = border;

            _timer.Interval = TimeSpan.FromMilliseconds(200);
            _timer.Tick += (_, __) =>
            {
                _ticks++;
                if (_ticks >= 18) // ~3.6 seconds
                {
                    try { _timer.Stop(); } catch { }
                    try { Close(); } catch { }
                }
            };

            Loaded += (_, __) =>
            {
                PositionBottomRight();
                try { _timer.Start(); } catch { }
            };
        }

        private void PositionBottomRight()
        {
            try
            {
                var wa = SystemParameters.WorkArea;
                Left = wa.Right - Width - 18;
                Top = wa.Bottom - Height - 18;
            }
            catch { }
        }
    }
}