using OverWatchELD.Services;
using System;
using System.Threading.Tasks;

namespace OverWatchELD.Services.Performance
{
    /// <summary>
    /// Processes TelemetrySnapshot samples and records driver performance events into SQLite.
    /// Rules:
    /// - ETS2: soft 100 km/h, grace up to 110 for 5s
    /// - ATS: soft 130 km/h, grace up to 140 for 5s
    /// - Race invalid: >180 km/h ignore sample
    /// - Hard brake: drop >22 km/h in <=1.2s; severe >35 km/h in <=1.2s
    /// - Idle: engine on + speed <3 km/h for >5 minutes (records once per minute after grace)
    /// </summary>
    public sealed class PerformanceAnalyzer
    {
        private readonly PerformanceEventSink _sink;

        private double _lastSpeedKmh;
        private DateTime _lastSampleUtc = DateTime.UtcNow;

        private double _speedGraceSeconds;
        private double _idleSeconds;
        private DateTime _lastIdleRecordUtc = DateTime.MinValue;

        public PerformanceAnalyzer(string connString)
        {
            _sink = new PerformanceEventSink(connString);
        }

        public async Task ProcessSnapshotAsync(TelemetrySnapshot snapshot, string driverName, string game)
        {
            if (snapshot == null) return;

            var now = DateTime.UtcNow;
            var dt = (now - _lastSampleUtc).TotalSeconds;
            if (dt <= 0) dt = 0.1;
            if (dt > 10) dt = 10; // clamp huge gaps

            var speedKmh = snapshot.SpeedMps * 3.6;

            // ignore crazy "race stats" samples
            if (speedKmh > 180)
            {
                _lastSpeedKmh = speedKmh;
                _lastSampleUtc = now;
                return;
            }

            // ---- Hard brake detection (no accel feed) ----
            var deltaSpeed = _lastSpeedKmh - speedKmh; // positive when slowing
            if (dt <= 1.2)
            {
                if (deltaSpeed > 35)
                    await _sink.RecordAsync(driverName, "HardBrakeSevere", 3, deltaSpeed);
                else if (deltaSpeed > 22)
                    await _sink.RecordAsync(driverName, "HardBrake", 2, deltaSpeed);
            }

            _lastSpeedKmh = speedKmh;
            _lastSampleUtc = now;

            // ---- Speeding detection ----
            var isAts = string.Equals((game ?? "").Trim(), "ATS", StringComparison.OrdinalIgnoreCase);
            double softLimit = isAts ? 130 : 100;
            double graceLimit = isAts ? 140 : 110;

            if (speedKmh > softLimit)
            {
                _speedGraceSeconds += dt;

                // if beyond graceLimit OR stayed above softLimit for >5 seconds
                if (speedKmh > graceLimit)
                {
                    await _sink.RecordAsync(driverName, "SpeedingSevere", 3, speedKmh - softLimit);
                }
                else if (_speedGraceSeconds > 5)
                {
                    await _sink.RecordAsync(driverName, "Speeding", 2, speedKmh - softLimit);
                }
            }
            else
            {
                _speedGraceSeconds = 0;
            }

            // ---- Idle detection ----
            if (snapshot.EngineOn && speedKmh < 3)
            {
                _idleSeconds += dt;

                // 5 min grace
                if (_idleSeconds > 300)
                {
                    // record at most once per minute to avoid spam
                    if (_lastIdleRecordUtc == DateTime.MinValue || (now - _lastIdleRecordUtc).TotalSeconds >= 60)
                    {
                        var minutes = _idleSeconds / 60.0;
                        await _sink.RecordAsync(driverName, "Idle", 1, minutes);
                        _lastIdleRecordUtc = now;
                    }
                }
            }
            else
            {
                _idleSeconds = 0;
                _lastIdleRecordUtc = DateTime.MinValue;
            }
        }
    }
}
