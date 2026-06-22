using System;
using DutyStatus = OverWatchELD.Models.DutyStatus;
using OverWatchELD.Models;

namespace OverWatchELD.Services
{
    public static class EldEngine
    {
        private static readonly object _stateLock = new object();
        private static TelemetrySnapshot? _lastSnap;

        public static DutyStatus CurrentStatus { get; private set; } = DutyStatus.OffDuty;
        public static DateTimeOffset CurrentStatusStartUtc { get; private set; } = EldClock.UtcNow;

        private const double DrivingSpeedThresholdMps = 2.2352; // ~5 mph
        private static readonly TimeSpan StoppedToOnDutyDelay = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan MovingDebounce = TimeSpan.FromSeconds(5);

        private static DateTimeOffset? _stoppedSinceEldUtc;
        private static DateTimeOffset? _movingSinceEldUtc;

        public static event Action<DutyStatus>? StatusChanged;

        public static void UpdateTelemetry(TelemetrySnapshot snap)
        {
            OnTelemetryUpdated(snap);
        }

        public static void SetStatus(
            DutyStatus newStatus,
            string? notes = null,
            string source = "Manual")
        {
            if (CurrentStatus == newStatus)
                return;

            CurrentStatus = newStatus;
            CurrentStatusStartUtc = EldClock.UtcNow;

            // Log the duty status change
            DatabaseService.InsertDutyEvent(new DutyEvent
            {
                Status = newStatus,
                StartUtc = CurrentStatusStartUtc,
                Notes = notes,
                Source = source
            });

            StatusChanged?.Invoke(newStatus);
        }

        private static void OnTelemetryUpdated(TelemetrySnapshot snap)
        {
            lock (_stateLock)
            {
                _lastSnap = snap;

                if (snap.GameTimeUtc.HasValue)
                    EldClock.SetGameTime(snap.GameTimeUtc.Value);

                var now = EldClock.UtcNow;

                bool isSpecialMode =
                    CurrentStatus == DutyStatus.PersonalConveyance ||
                    CurrentStatus == DutyStatus.YardMove;

                // ENGINE OFF
                if (!snap.EngineOn)
                {
                    _movingSinceEldUtc = null;
                    _stoppedSinceEldUtc = null;

                    if (CurrentStatus == DutyStatus.Sleeper ||
                        CurrentStatus == DutyStatus.OffDuty)
                        return;

                    SetStatus(DutyStatus.OffDuty, source: "Telemetry");
                    return;
                }

                // MOVING
                if (snap.SpeedMps >= DrivingSpeedThresholdMps)
                {
                    _stoppedSinceEldUtc = null;

                    if (_movingSinceEldUtc == null)
                        _movingSinceEldUtc = now;

                    if (now - _movingSinceEldUtc.Value >= MovingDebounce)
                    {
                        if (!isSpecialMode && CurrentStatus != DutyStatus.Driving)
                            SetStatus(DutyStatus.Driving, source: "Telemetry");
                    }

                    return;
                }

                // NOT MOVING
                _movingSinceEldUtc = null;

                if (CurrentStatus == DutyStatus.Driving)
                {
                    if (_stoppedSinceEldUtc == null)
                        _stoppedSinceEldUtc = now;

                    if (now - _stoppedSinceEldUtc.Value >= StoppedToOnDutyDelay)
                    {
                        if (!isSpecialMode)
                            SetStatus(
                                DutyStatus.OnDuty,
                                notes: "Stopped > 3 min (ELD)",
                                source: "Telemetry");
                    }

                    return;
                }

                _stoppedSinceEldUtc = null;
            }
        }

        public static void ForceResetToOffDuty()
        {
            lock (_stateLock)
            {
                CurrentStatus = DutyStatus.Off;
                CurrentStatusStartUtc = EldClock.UtcNow;
                _stoppedSinceEldUtc = null;
                _movingSinceEldUtc = null;

                StatusChanged?.Invoke(CurrentStatus);
            }
        }

    }
}
