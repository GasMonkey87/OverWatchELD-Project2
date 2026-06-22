using System;
using OverWatchELD.Models;

namespace OverWatchELD.Services
{
    public enum ResetScope
    {
        DutyOnly,
        DutyAndInspections,
        Everything
    }

    public static class ResetService
    {
        public static event Action<DateTimeOffset, ResetScope>? ResetPerformed;

        public static DateTimeOffset LastResetUtc { get; private set; } = DateTimeOffset.MinValue;
        public static ResetScope LastScope { get; private set; } = ResetScope.DutyOnly;

        public static void Reset(ResetScope scope)
        {
            // clear stored data first
            DatabaseService.DeleteAllDutyEvents();
            if (scope == ResetScope.DutyAndInspections || scope == ResetScope.Everything)
                DatabaseService.DeleteAllInspections();

            // If you later add other stores, handle here when scope == Everything.

            // reset engine state
            EldEngine.ForceResetToOffDuty();

            LastResetUtc = EldClock.UtcNow;
            LastScope = scope;
            ResetPerformed?.Invoke(LastResetUtc, scope);
        }

        public static string FormatResetTooltip(DateTimeOffset utc, ResetScope scope)
        {
            if (utc == DateTimeOffset.MinValue) return "";
            var local = utc.ToLocalTime();
            var scopeText = scope switch
            {
                ResetScope.DutyOnly => "Duty only",
                ResetScope.DutyAndInspections => "Duty + inspections",
                _ => "Everything"
            };
            return $"Reset: {local:MMM d, yyyy h:mm tt} ({scopeText})";
        }
    }
}
