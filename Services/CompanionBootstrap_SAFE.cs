// Services\CompanionBootstrap_SAFE.cs
// ✅ FULL COPY/REPLACE
// Bootstrap wrapper ONLY (no duplicate host logic, no direct DutyStateMachine calls)

namespace OverWatchELD.Services
{
    public static class CompanionBootstrapSafe
    {
        private static bool _started;

        public static void Start()
        {
            if (_started) return;
            _started = true;

            try
            {
                CompanionApiHostSafe.Start();
            }
            catch
            {
                // never block app startup
            }
        }

        public static void Stop()
        {
            try
            {
                CompanionApiHostSafe.Stop();
            }
            catch
            {
                // ignore
            }
            finally
            {
                _started = false;
            }
        }
    }
}
