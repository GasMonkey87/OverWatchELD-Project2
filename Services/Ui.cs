using System;
using System.Windows;

namespace OverWatchELD.Services
{
    internal static class Ui
    {
        public static void OnUi(Action action)
        {
            try
            {
                var d = Application.Current?.Dispatcher;
                if (d == null || d.CheckAccess()) { action(); return; }
                d.BeginInvoke(action);
            }
            catch { }
        }
    }
}
