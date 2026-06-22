using System;
using System.Threading.Tasks;
using Velopack;

namespace OverWatchELD.Services
{
    public static class UpdateService
    {
        // Where your releases are hosted (examples):
        // - https://yourdomain.com/updates/overwatcheld/
        // - GitHub Releases feed (Velopack supports multiple sources depending on setup)
        public static string UpdateUrl { get; set; } = "";

        public static async Task<(bool ok, string msg)> CheckAndApplyAsync(bool silent = false)
        {
            try
            {
                var url = (UpdateUrl ?? "").Trim();
                if (string.IsNullOrWhiteSpace(url))
                    return (false, "Update URL not configured.");

                var mgr = new UpdateManager(url);

                var update = await mgr.CheckForUpdatesAsync();
                if (update == null)
                    return (true, "No updates available.");

                await mgr.DownloadUpdatesAsync(update);
                mgr.ApplyUpdatesAndRestart(update);

                return (true, "Updating… restarting.");
            }
            catch (Exception ex)
            {
                return (false, "Update failed: " + ex.Message);
            }
        }
    }
}