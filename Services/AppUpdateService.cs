using System.Threading.Tasks;

namespace OverWatchELD.Services
{
    /// <summary>
    /// App-facing update API expected by App.xaml.cs.
    /// This keeps the rest of the app stable even if we change update internals.
    /// </summary>
    public static class AppUpdateService
    {
        private static bool _inited;

        /// <summary>
        /// Called during startup to initialize update system.
        /// Safe no-op if called multiple times.
        /// </summary>
        public static void Init()
        {
            if (_inited) return;
            _inited = true;

            // Velopack has its own early-init call (optional depending on how you package).
            // If you already call VelopackApp.Build().Run() elsewhere, leaving this empty is fine.
            // We keep it safe and non-breaking.
            try
            {
                // Intentionally minimal here.
            }
            catch { }
        }

        /// <summary>
        /// Startup background update check expected by App.xaml.cs.
        /// Never blocks UI. Never throws.
        /// </summary>
        public static Task CheckAndApplyInBackgroundAsync()
        {
            try
            {
                AutoUpdateService.StartBackground();
            }
            catch { }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Manual "Check for Updates" button hook.
        /// </summary>
        public static Task<AppUpdateResult> CheckNowAsync()
            => AutoUpdateService.CheckNowAsync();
    }
}