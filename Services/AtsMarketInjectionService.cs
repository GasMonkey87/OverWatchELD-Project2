using System;

namespace OverWatchELD.Services
{
    public static class AtsMarketInjectionService
    {
        public static bool QueueSingleJob(DispatchJob job)
        {
            if (job == null)
                return false;

            var trailerOwner = GetTrailerOwner(job);

            // Company trailer => Freight Market
            if (string.Equals(trailerOwner, "Company", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(trailerOwner))
            {
                return AtsFreightMarketInjectorService.QueueSingleJob(job);
            }

            // Driver/Leased/etc => Cargo Market
            return AtsCargoMarketInjectorService.QueueSingleJob(job);
        }

        private static string GetTrailerOwner(DispatchJob job)
        {
            try
            {
                var prop = job.GetType().GetProperty("TrailerOwner");
                var value = prop?.GetValue(job);
                return value?.ToString()?.Trim() ?? "";
            }
            catch
            {
                return "";
            }
        }
    }
}