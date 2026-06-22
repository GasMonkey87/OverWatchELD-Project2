using OverWatchELD.Models;

namespace OverWatchELD.Services
{
    public static class HosSnapshotExtensions
    {
        public static bool IsShiftExpired(this HosSnapshot s)
            => s.ShiftRemaining <= TimeSpan.Zero;

        public static bool IsDriveExpired(this HosSnapshot s)
            => s.DriveRemaining <= TimeSpan.Zero;

        public static bool IsCycleExpired(this HosSnapshot s)
            => s.CycleRemaining <= TimeSpan.Zero;

        public static bool IsBreakRequired(this HosSnapshot s)
            => s.BreakRequired;
    }
}
