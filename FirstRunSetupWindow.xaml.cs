using OverWatchELD.Services;
using System;
using System.Diagnostics;
using System.Windows;

namespace OverWatchELD.Views
{
    public partial class FirstTimeSetupWindow : Window
    {
        public FirstTimeSetupWindow()
        {
            InitializeComponent();
        }

        private void Standalone_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                VtcHardResetService.SetStandaloneMode();
                FirstRunSetupService.MarkStandalone();

                var main = new MainWindow();
                Application.Current.MainWindow = main;
                main.Show();

                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Standalone setup failed:\n" + ex.Message, "OverWatch ELD");
            }
        }

        private void DownloadTelemetryReader_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://overwatcheld.up.railway.app/downloads.html",
                UseShellExecute = true
            });
        }

        private void OpenSetupGuide_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://overwatcheld.up.railway.app/setup.html",
                UseShellExecute = true
            });
        }

        private void LinkVtc_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                FirstRunSetupService.MarkVtc();

                var login = new LoginWindow();
                Application.Current.MainWindow = login;
                login.Show();

                MessageBox.Show(
                    "Paste your Discord !link code and click Login to connect this ELD to your VTC.",
                    "VTC Link Setup",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("VTC setup failed:\n" + ex.Message, "OverWatch ELD");
            }
        }
    }
}
