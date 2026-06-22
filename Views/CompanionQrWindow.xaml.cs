using QRCoder;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace OverWatchELD.Views
{
    public partial class CompanionQrWindow : Window
    {
        private readonly string _url;

        public CompanionQrWindow(string url)
        {
            InitializeComponent();

            _url = (url ?? "").Trim();
            UrlTextBox.Text = _url;

            GenerateQr(_url);
        }

        private void GenerateQr(string text)
        {
            try
            {
                using var generator = new QRCodeGenerator();
                using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
                var pngQr = new PngByteQRCode(data);
                var bytes = pngQr.GetGraphic(20);

                using var ms = new MemoryStream(bytes);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = ms;
                image.EndInit();
                image.Freeze();

                QrImage.Source = image;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Failed to generate QR code.\n\n" + ex.Message,
                    "OverWatch ELD",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void CopyLink_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(_url);

                MessageBox.Show(
                    "Companion link copied:\n\n" + _url,
                    "OverWatch ELD",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Failed to copy link.\n\n" + ex.Message,
                    "OverWatch ELD",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void OpenBrowser_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Failed to open browser.\n\n" + ex.Message,
                    "OverWatch ELD",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}