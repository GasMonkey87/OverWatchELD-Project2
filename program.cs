using System;
using System.Windows;
using Velopack;

namespace OverWatchELD
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            VelopackApp.Build().Run(); // ✅ REQUIRED HERE

            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
    }
}