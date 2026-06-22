using System;

namespace ATS_ELD.ViewModels
{
    public sealed class ShiftDebugViewModel
    {
        public string Title { get; }
        public string SummaryLine { get; }

        public string ShiftStart { get; }
        public string WindowEndRaw { get; }
        public string Paused { get; }
        public string WindowEndEffective { get; }

        public string DriveInShift { get; }
        public string DriveSinceBreak { get; }

        public string TriggerText { get; }
        public string TraceText { get; }

        // ✅ 10-argument constructor (last arg = traceText)
        public ShiftDebugViewModel(
            DateTime day,
            string shiftStart,
            string rawEnd,
            string paused,
            string effEnd,
            string driveInShift,
            string driveSinceBreak,
            string triggerText,
            string violationText,
            string traceText)
        {
            Title = $"Shift Debug — {day:ddd MM/dd}";
            SummaryLine = $"Violations: {violationText}";

            ShiftStart = shiftStart;
            WindowEndRaw = rawEnd;
            Paused = paused;
            WindowEndEffective = effEnd;

            DriveInShift = driveInShift;
            DriveSinceBreak = driveSinceBreak;

            TriggerText = triggerText;
            TraceText = traceText;
        }
    }
}
