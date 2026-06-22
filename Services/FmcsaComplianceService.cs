using OverWatchELD.Models;
using OverWatchELD.ViewModels;
using System;

namespace OverWatchELD.Services
{
    public sealed class FmcsaComplianceService
    {
        private bool? _lastEngineOn;
        private bool _wasDriving;
        private DateTimeOffset? _stoppedSinceUtc;
        private DateTimeOffset? _lastIntermediateLogUtc;
        private DateTimeOffset? _lastTelemetrySeenUtc;

        private const double DrivingMph = 5.0;
        private const double StoppedMph = 0.2;

        private static readonly TimeSpan StoppedPromptDelay = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan StoppedAutoOnDutyDelay = TimeSpan.FromMinutes(6);
        private static readonly TimeSpan IntermediateInterval = TimeSpan.FromMinutes(60);
        private static readonly TimeSpan TelemetryLostDiagnosticDelay = TimeSpan.FromSeconds(5);

        public bool AdverseDrivingActive { get; private set; }
        public bool SplitSleeperCandidateActive { get; private set; }

        public void EnableAdverseDrivingException(string note = "")
        {
            AdverseDrivingActive = true;
            InsertSystemEvent(
                DutyStatus.OnDuty,
                "adverse-driving-exception",
                string.IsNullOrWhiteSpace(note)
                    ? "Adverse driving exception enabled."
                    : "Adverse driving exception enabled. " + note,
                locked: true);
        }

        public void DisableAdverseDrivingException()
        {
            AdverseDrivingActive = false;
            InsertSystemEvent(
                DutyStatus.OnDuty,
                "adverse-driving-exception-ended",
                "Adverse driving exception ended.",
                locked: true);
        }

        public void OnTelemetrySnapshot(TelemetrySnapshot snapshot, DutyStateMachine duty)
        {
            if (snapshot == null || duty == null)
                return;

            var nowUtc = EldClock.UtcNow;
            var speedMph = Math.Abs(snapshot.SpeedMps * 2.23694);
            var moving = snapshot.Connected && speedMph >= DrivingMph;
            var stopped = speedMph <= StoppedMph;

            _lastTelemetrySeenUtc = nowUtc;

            HandleEnginePowerEvents(snapshot, nowUtc);
            HandleAutoDriving(moving, stopped, nowUtc, duty);
            HandleIntermediateLogs(moving, nowUtc, snapshot);
            HandleSplitSleeperCandidate(duty.Current, nowUtc);
        }

        public void OnTelemetryMissing(DutyStateMachine duty)
        {
            var nowUtc = EldClock.UtcNow;

            if (_lastTelemetrySeenUtc.HasValue &&
                nowUtc - _lastTelemetrySeenUtc.Value >= TelemetryLostDiagnosticDelay)
            {
                InsertSystemEvent(
                    duty?.Current ?? DutyStatus.OnDuty,
                    "data-diagnostic",
                    "Engine synchronization / telemetry data diagnostic: telemetry missing for 5+ seconds.",
                    locked: true);

                _lastTelemetrySeenUtc = nowUtc;
            }
        }

        private void HandleEnginePowerEvents(TelemetrySnapshot snapshot, DateTimeOffset nowUtc)
        {
            if (!_lastEngineOn.HasValue)
            {
                _lastEngineOn = snapshot.EngineOn;
                return;
            }

            if (_lastEngineOn.Value == snapshot.EngineOn)
                return;

            _lastEngineOn = snapshot.EngineOn;

            InsertSystemEvent(
                snapshot.EngineOn ? DutyStatus.OnDuty : DutyStatus.OffDuty,
                snapshot.EngineOn ? "engine-power-up" : "engine-shutdown",
                snapshot.EngineOn ? "CMV engine power-up recorded." : "CMV engine shutdown recorded.",
                locked: true,
                lat: snapshot.GpsLatitude,
                lon: snapshot.GpsLongitude,
                location: snapshot.City);
        }

        private void HandleAutoDriving(bool moving, bool stopped, DateTimeOffset nowUtc, DutyStateMachine duty)
        {
            if (duty.Current == DutyStatus.PersonalConveyance ||
                duty.Current == DutyStatus.YardMove)
            {
                if (moving)
                    _wasDriving = true;

                return;
            }

            if (moving)
            {
                _stoppedSinceUtc = null;

                if (duty.Current != DutyStatus.Driving)
                {
                    duty.ForceSet(DutyStatus.Driving);
                    _wasDriving = true;
                }

                return;
            }

            if (duty.Current == DutyStatus.Driving && stopped)
            {
                _stoppedSinceUtc ??= nowUtc;

                var stoppedFor = nowUtc - _stoppedSinceUtc.Value;

                if (stoppedFor >= StoppedPromptDelay && stoppedFor < StoppedAutoOnDutyDelay)
                {
                    DashboardClocksLiveViewModel.Shared.Notes =
                        "FMCSA prompt: vehicle stopped 5 minutes. Change to On Duty or remain Driving.";
                    DashboardClocksLiveViewModel.Shared.Pulse = true;
                    DashboardClocksLiveViewModel.Shared.RefreshNow();
                }

                if (stoppedFor >= StoppedAutoOnDutyDelay)
                {
                    duty.ForceSet(DutyStatus.OnDuty);
                    _wasDriving = false;
                    _stoppedSinceUtc = null;
                }
            }
            else
            {
                _stoppedSinceUtc = null;
            }
        }

        private void HandleIntermediateLogs(bool moving, DateTimeOffset nowUtc, TelemetrySnapshot snapshot)
        {
            if (!moving)
                return;

            if (_lastIntermediateLogUtc.HasValue &&
                nowUtc - _lastIntermediateLogUtc.Value < IntermediateInterval)
                return;

            _lastIntermediateLogUtc = nowUtc;

            InsertSystemEvent(
                DutyStatus.Driving,
                "intermediate-log",
                "Automatic intermediate log recorded while vehicle in motion.",
                locked: true,
                lat: snapshot.GpsLatitude,
                lon: snapshot.GpsLongitude,
                location: snapshot.City);
        }

        private void HandleSplitSleeperCandidate(DutyStatus current, DateTimeOffset nowUtc)
        {
            if (current == DutyStatus.Sleeper)
            {
                SplitSleeperCandidateActive = true;
                return;
            }

            if (SplitSleeperCandidateActive &&
                current != DutyStatus.Sleeper &&
                current != DutyStatus.OffDuty)
            {
                SplitSleeperCandidateActive = false;
            }
        }

        public bool CanUsePersonalConveyance(DutyStatus current, bool loadedTrailer, bool dispatchActive)
        {
            if (current == DutyStatus.Driving)
                return false;

            if (loadedTrailer || dispatchActive)
                return false;

            return true;
        }

        public bool CanUseYardMove(DutyStatus current)
        {
            return current != DutyStatus.Driving;
        }

        public void RecordUnassignedDriving(TelemetrySnapshot snapshot)
        {
            InsertSystemEvent(
                DutyStatus.Driving,
                "unassigned-driving",
                "Unassigned driving detected. Driver must accept or reject.",
                locked: true,
                lat: snapshot.GpsLatitude,
                lon: snapshot.GpsLongitude,
                location: snapshot.City);
        }

        private static void InsertSystemEvent(
            DutyStatus status,
            string source,
            string notes,
            bool locked,
            double? lat = null,
            double? lon = null,
            string? location = null)
        {
            try
            {
                DatabaseService.InsertDutyEvent(new DutyEvent
                {
                    Status = status,
                    StartUtc = EldClock.UtcNow,
                    EndUtc = null,
                    Notes = notes,
                    Source = source,
                    LocationText = location,
                    Lat = lat,
                    Lon = lon,
                    IsEdited = false,
                    IsLocked = locked
                });
            }
            catch
            {
            }
        }
    }
}