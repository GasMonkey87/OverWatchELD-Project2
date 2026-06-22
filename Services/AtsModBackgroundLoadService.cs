using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace OverWatchELD.Services
{
    public static class AtsModBackgroundLoadService
    {
        private static int _started;
        private static int _finished;

        public static bool IsRunning => _started == 1 && _finished == 0;
        public static bool IsFinished => _finished == 1;

        public static event Action<string>? StatusChanged;

        public static void StartOnce()
        {
            if (Interlocked.Exchange(ref _started, 1) == 1)
                return;

            _finished = 0;

            Task.Run(async () =>
            {
                try
                {
                    StatusChanged?.Invoke("Loading ATS mods in background...");

                    await RunScannerBestEffortAsync();

                    StatusChanged?.Invoke("ATS mods loaded.");
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke("ATS mod loading failed: " + ex.Message);
                }
                finally
                {
                    _finished = 1;
                }
            });
        }

        private static async Task RunScannerBestEffortAsync()
        {
            try
            {
                var scannerType = typeof(AtsModScannerService);

                string[] preferredNames =
                {
                    "ScanActiveModsAsync",
                    "ScanAllAsync",
                    "LoadActiveModsAsync",
                    "RefreshAsync",
                    "ScanAsync",
                    "LoadAsync",
                    "ScanActiveMods",
                    "ScanAll",
                    "LoadActiveMods",
                    "Refresh",
                    "Scan",
                    "Load"
                };

                foreach (var name in preferredNames)
                {
                    var method = scannerType
                        .GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m =>
                            string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase) &&
                            m.GetParameters().Length == 0);

                    if (method == null)
                        continue;

                    var result = method.Invoke(null, null);

                    if (result is Task task)
                        await task.ConfigureAwait(false);

                    return;
                }

                System.Diagnostics.Debug.WriteLine("[ATS MOD BACKGROUND] No public static no-argument scanner method found.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[ATS MOD BACKGROUND] Scanner failed: " + ex.Message);
            }
        }

        public static void NotifyUi(Action<string> uiUpdate)
        {
            StatusChanged += msg =>
            {
                try
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        uiUpdate(msg);
                    }));
                }
                catch { }
            };
        }
    }
}