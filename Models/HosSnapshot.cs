using System;

namespace OverWatchELD.Models
{
    // Single snapshot object the UI can bind to (NO static fields).
    public sealed class HosSnapshot
    {
        // Remaining clocks
        public TimeSpan ShiftRemaining { get; init; }
        public TimeSpan DriveRemaining { get; init; }
        public TimeSpan CycleRemaining { get; init; }
        public TimeSpan BreakRemaining { get; init; }

        // Flags
        public bool BreakRequired { get; init; }
        public bool IsBreakDue { get; init; }

        public bool DriveViolation { get; init; }
        public bool ShiftViolation { get; init; }
        public bool BreakViolation { get; init; }
        public bool CycleViolation { get; init; }

        public bool ShouldPulse { get; init; }

        // UI helpers
        public string Notes { get; init; } = "";

        // “Used percent” for rings/graphs (0..1)
        public double DriveUsedPct { get; init; }
        public double ShiftUsedPct { get; init; }
        public double BreakUsedPct { get; init; }
        public double CycleUsedPct { get; init; }
    }
}
