using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using OverWatchELD.Models;

namespace OverWatchELD.Views
{
    public partial class ToastNotificationWindow : Window
    {
        private readonly DispatcherTimer _timer = new();

        public event Action? Clicked;

        public ToastNotificationWindow(DispatchMessage msg, TimeSpan? autoClose = null)
        {
            InitializeComponent();

            DataContext = msg;

            MouseLeftButtonUp += (_, __) =>
            {
                Clicked?.Invoke();
                Close();
            };

            _timer.Interval = autoClose ?? TimeSpan.FromSeconds(6);
            _timer.Tick += (_, __) => Close();
            _timer.Start();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _timer.Stop();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
