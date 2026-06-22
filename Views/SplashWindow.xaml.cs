using System.Reflection;
using System.Windows;

namespace OverWatchELD.Views
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();

            try
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                VersionText.Text = version == null
                    ? "Version 1.0.0"
                    : $"Version {version.Major}.{version.Minor}.{version.Build}";
            }
            catch
            {
                VersionText.Text = "Version 1.0.0";
            }
        }

        public void SetStatus(string text)
        {
            Dispatcher.Invoke(() =>
            {
                if (StatusText != null)
                    StatusText.Text = text;
            });
        }
    }
}