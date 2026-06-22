using System;
using System.Windows;

namespace OverWatchELD.Views
{
    public partial class UpdateDownloadWindow : Window
    {
        public UpdateDownloadWindow()
        {
            InitializeComponent();
        }

        public void SetStatus(string text)
        {
            StatusText.Text = text ?? "";
        }

        public void SetProgress(double? percent)
        {
            if (percent.HasValue)
            {
                Progress.IsIndeterminate = false;
                Progress.Value = Math.Max(0, Math.Min(100, percent.Value));
            }
            else
            {
                Progress.IsIndeterminate = true;
            }
        }
    }
}