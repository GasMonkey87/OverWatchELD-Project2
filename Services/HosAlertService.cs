using System;
using System.Media;
using System.Windows;
using OverWatchELD.Models;

namespace OverWatchELD.Services
{
    /// <summary>
    /// Centralized "real ELD" alert behavior:
    ///  - Audible alert when a violation begins or when break becomes due
    ///  - Acknowledgement prompt (one-time per violation until it clears)
    ///  - Simple throttling so it doesn't spam at 1Hz refresh
    /// </summary>
    public static class HosAlertService
    {
        private static DateTimeOffset _lastBeepUtc = DateTimeOffset.MinValue;

        private static bool _driveAcked;
        private static bool _shiftAcked;
        private static bool _breakAcked;
        private static bool _cycleAcked;

        public static void HandleSnapshot(HosSnapshot snap)
        {
            try
            {
                // Reset ack flags when cleared
                if (!snap.DriveViolation) _driveAcked = false;
                if (!snap.ShiftViolation) _shiftAcked = false;
                if (!snap.CycleViolation) _cycleAcked = false;
                if (!snap.IsBreakDue) _breakAcked = false;

                // Decide whether we should alert right now
                var shouldAlert =
                    snap.DriveViolation ||
                    snap.ShiftViolation ||
                    snap.CycleViolation ||
                    snap.IsBreakDue;

                if (shouldAlert)
                    TryBeep();

                // Acknowledgement prompts (show once per condition until cleared)
                if (snap.DriveViolation && !_driveAcked)
                {
                    _driveAcked = true;
                    SafePrompt("HOS Violation: Driving limit exceeded", snap.Notes);
                }
                if (snap.ShiftViolation && !_shiftAcked)
                {
                    _shiftAcked = true;
                    SafePrompt("HOS Violation: 14-hour shift window exceeded", snap.Notes);
                }
                if (snap.CycleViolation && !_cycleAcked)
                {
                    _cycleAcked = true;
                    SafePrompt("HOS Violation: 70-hour cycle limit exceeded", snap.Notes);
                }
                if (snap.IsBreakDue && !_breakAcked)
                {
                    _breakAcked = true;
                    SafePrompt("Break required", "You have reached 8 hours of driving without a 30-minute non-driving break.");
                }
            }
            catch
            {
                // never crash the app because of alerts
            }
        }

        private static void TryBeep()
        {
            var now = EldClock.UtcNow;
            if ((now - _lastBeepUtc) < TimeSpan.FromSeconds(10))
                return;

            _lastBeepUtc = now;

            try
            {
                SystemSounds.Exclamation.Play();
            }
            catch { }
        }

        private static void SafePrompt(string title, string? details)
        {
            try
            {
                // Keep it simple and reliable (WPF MessageBox).
                var msg = string.IsNullOrWhiteSpace(details) ? title : (title + "\n\n" + details);
                MessageBox.Show(msg, "ATS ELD", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch { }
        }
    }
}