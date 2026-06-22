using System;
using System.Windows;

namespace OverWatchELD.Views
{
    public partial class DailyLogsExportWindow : Window
    {
        public DailyLogsExportWindow()
        {
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try { Activate(); } catch { }
        }
    }
}
