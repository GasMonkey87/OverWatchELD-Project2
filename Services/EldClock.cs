using System;

namespace OverWatchELD.Services
{
    public static class EldClock
    {
        private static readonly object _lock = new();

        // Backwards-compatible API used by TelemetryService / EldEngine
        public static void SetGameTime(DateTimeOffset? gameUtc)
        {
            SetTelemetryGameUtc(gameUtc);
        }

        public static void SetGameTime(DateTime? gameUtc)
        {
            if (gameUtc == null)
                SetTelemetryGameUtc(null);
            else
                SetTelemetryGameUtc(new DateTimeOffset(DateTime.SpecifyKind(gameUtc.Value, DateTimeKind.Utc)));
        }

        // FMCSA-style accuracy: do NOT use scaled time when telemetry isn't available.
        // If later you want ATS game-time, you can flip this back on intentionally.
        public static bool PreferTelemetryGameTime { get; set; } = true;

        private static readonly HashSet<DateOnly> _activeGameDays = new();

        public static void MarkActivityForDay(DateOnly day)
        {
            lock (_lock)
                _activeGameDays.Add(day);
        }

        public static bool HasActivityForDay(DateOnly day)
        {
            lock (_lock)
                return _activeGameDays.Contains(day);
        }

        public static bool IsGameTimeAvailable
        {
            get
            {
                lock (_lock)
                    return _telemetryGameUtc.HasValue;
            }
        }

        public static void ClearGameTime()
        {
            lock (_lock)
            {
                _telemetryGameUtc = null;
            }
        }

        private static DateTimeOffset? _telemetryGameUtc;
        private static DateTimeOffset _telemetryRealBaseUtc = DateTimeOffset.UtcNow;

        // Fallback if telemetry not available
        private static DateTimeOffset _realBaseUtc = DateTimeOffset.UtcNow;
        private static DateTimeOffset _eldBaseUtc = DateTimeOffset.UtcNow;

        // MUST be 1.0 to prevent "future days" when telemetry is missing
        public static double FallbackScale { get; set; } = 1.0;

        // Unified UTC clock
        public static DateTimeOffset UtcNow
        {
            get
            {
                lock (_lock)
                {
                    if (PreferTelemetryGameTime && _telemetryGameUtc.HasValue)
                    {
                        // Advance smoothly between telemetry packets so the dashboard clocks
                        // still count by the second instead of freezing on the last packet.
                        var realDelta = DateTimeOffset.UtcNow - _telemetryRealBaseUtc;
                        return _telemetryGameUtc.Value + realDelta;
                    }

                    // Real-time fallback (unscaled unless intentionally changed)
                    var now = DateTimeOffset.UtcNow;
                    var fallbackDelta = now - _realBaseUtc;
                    var scaledTicks = (long)(fallbackDelta.Ticks * FallbackScale);
                    return _eldBaseUtc + TimeSpan.FromTicks(scaledTicks);
                }
            }
        }

        public static DateTime LocalNow => UtcNow.LocalDateTime;

        public static TimeSpan LocalOffset =>
            TimeZoneInfo.Local.GetUtcOffset(UtcNow);

        public static void ResetFallbackToNow()
        {
            lock (_lock)
            {
                var now = DateTimeOffset.UtcNow;
                _realBaseUtc = now;
                _eldBaseUtc = now;
            }
        }

        // Telemetry can set this if you wire it; optional.
        public static void SetTelemetryGameUtc(DateTimeOffset? gameUtc)
        {
            lock (_lock)
            {
                _telemetryGameUtc = gameUtc;
                _telemetryRealBaseUtc = DateTimeOffset.UtcNow;
            }
        }
    }
}