using System;
using System.Windows;
using OverWatchELD.Views;

namespace OverWatchELD
{
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Logs off and returns to the Login screen.
        /// Keeps app running; if user cancels login, the app exits.
        /// </summary>
        public void LogOffAndReturnToLogin()
        {
            try
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.Invoke(LogOffAndReturnToLogin);
                    return;
                }

                // Best-effort: clear session in memory (login window will repopulate)
                try
                {
                    if (Application.Current is App app)
                    {
                        app.EnsureSession();
                        if (app.Session != null)
                        {
                            app.Session.DriverName = "Driver";
                            app.Session.Save();
                        }
                    }
                }
                catch { }

                // Hide main while logging in
                try { Hide(); } catch { }

                var login = new LoginWindow
                {
                    Owner = null,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };

                var ok = login.ShowDialog() == true;
                if (!ok)
                {
                    try { Application.Current.Shutdown(); } catch { }
                    return;
                }

                // Back to dashboard with refreshed session
                try
                {
                    Show();
                    WindowState = WindowState.Normal;
                    Activate();
                    NavigateTo("Dashboard");
                }
                catch { }
            }
            catch
            {
                try { Application.Current.Shutdown(); } catch { }
            }
        }
    }
}
