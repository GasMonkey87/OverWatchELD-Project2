using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OverWatchELD.Views.Controls
{
    public partial class HosGraphOverlay : UserControl
    {
        // You can bind these if you want, but it works stand-alone too.
        public event Action<int>? MinuteClicked; // minute-of-day 0..1439

        public HosGraphOverlay()
        {
            InitializeComponent();

            Loaded += (_, __) =>
            {
                MouseMove += OnMouseMove;
                MouseLeave += OnMouseLeave;
                MouseLeftButtonDown += OnMouseLeftButtonDown;
            };
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            var p = e.GetPosition(this);
            var w = Math.Max(1, ActualWidth);

            // Map X => minute of day
            var minute = (int)Math.Round((p.X / w) * 24 * 60);
            if (minute < 0) minute = 0;
            if (minute > 24 * 60 - 1) minute = 24 * 60 - 1;

            // Line
            HoverLine.Visibility = Visibility.Visible;
            HoverLine.Height = ActualHeight;
            HoverLine.Margin = new Thickness(p.X, 0, 0, 0);

            // Bubble (clamp to stay on screen)
            var hh = minute / 60;
            var mm = minute % 60;

            HoverText.Text = $"{hh:00}:{mm:00}";
            HoverBubble.Visibility = Visibility.Visible;

            var bubbleX = p.X + 10;
            var bubbleMaxX = Math.Max(0, ActualWidth - 90); // approx bubble width
            if (bubbleX > bubbleMaxX) bubbleX = bubbleMaxX;

            HoverBubble.Margin = new Thickness(bubbleX, 10, 0, 0);
        }

        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            HoverLine.Visibility = Visibility.Collapsed;
            HoverBubble.Visibility = Visibility.Collapsed;
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var p = e.GetPosition(this);
            var w = Math.Max(1, ActualWidth);

            var minute = (int)Math.Round((p.X / w) * 24 * 60);
            if (minute < 0) minute = 0;
            if (minute > 24 * 60 - 1) minute = 24 * 60 - 1;

            MinuteClicked?.Invoke(minute);
        }
    }
}
