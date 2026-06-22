using System;

namespace OverWatchELD.Models
{
    public sealed class ComplianceResult
    {
        public DateTimeOffset AsOfLocal { get; set; }

        public TimeSpan DrivingUsed { get; set; }
        public TimeSpan DrivingRemaining { get; set; }

        public TimeSpan OnDutyUsed { get; set; }
        public TimeSpan OnDutyRemaining { get; set; }

        public TimeSpan ShiftRemaining { get; set; } // 14-hr window remaining

        public bool BreakRequired { get; set; }
        public TimeSpan BreakRemaining { get; set; } // time until break is required

        public bool DrivingViolation { get; set; }
        public bool ShiftViolation { get; set; }
        public bool BreakViolation { get; set; }

        public string Summary { get; set; } = "";
    }
}