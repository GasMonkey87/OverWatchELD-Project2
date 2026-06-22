using System;
using System.Reflection;
using System.Windows;

namespace OverWatchELD.Views
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();

            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var ver = asm.GetName().Version?.ToString() ?? "0.0.0.0";
                VersionText.Text = "Version: " + ver;
                BuildText.Text = "Build: " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm");
            }
            catch { }
        }
    }
}
