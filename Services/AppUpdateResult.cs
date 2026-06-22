namespace OverWatchELD.Services
{
    public sealed class AppUpdateResult
    {
        public bool Success { get; set; }
        public bool UpdateAvailable { get; set; }
        public bool UpdateApplied { get; set; }

        public string CurrentVersion { get; set; } = "";
        public string LatestVersion { get; set; } = "";
        public string Message { get; set; } = "";
    }
}