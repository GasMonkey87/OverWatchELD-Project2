using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using DutyStatus = OverWatchELD.Models.DutyStatus;

namespace OverWatchELD.Services
{
    public sealed class TelemetryDutyAutoService
    {
        private const double DrivingThresholdMph = 5.0;
        private const double StoppedThresholdMph = 0.2;

        private static readonly TimeSpan StoppedToOnDutyDelay = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan DriverPromptDelay = TimeSpan.FromMinutes(1);

        private DateTimeOffset? _stoppedSinceUtc;

        public void OnTelemetryTick(
            double speedMph,
            bool engineOn,
            bool parkingBrake,
            DateTimeOffset gameNowUtc,
            DutyStateMachine duty)
        {
            if (duty == null)
                return;

            var absSpeed = Math.Abs(speedMph);

            RunOnUiThread(() =>
            {
                EldClock.SetGameTime(gameNowUtc);

                var current = duty.Current;

                Debug.WriteLine($"[AUTO DUTY] speed={absSpeed:N1} engine={engineOn} brake={parkingBrake} current={current}");

                if (current == DutyStatus.PersonalConveyance ||
                    current == DutyStatus.YardMove)
                {
                    _stoppedSinceUtc = null;
                    return;
                }

                if (!parkingBrake && absSpeed >= DrivingThresholdMph)
                {
                    _stoppedSinceUtc = null;

                    if (current != DutyStatus.Driving)
                    {
                        Debug.WriteLine("[AUTO DUTY] FMCSA auto-switching to DRIVING.");
                        ForceDutyStatus(duty, DutyStatus.Driving);
                    }

                    return;
                }

                if (current == DutyStatus.Driving && absSpeed <= StoppedThresholdMph)
                {
                    _stoppedSinceUtc ??= gameNowUtc;

                    var stoppedFor = gameNowUtc - _stoppedSinceUtc.Value;

                    if (stoppedFor >= StoppedToOnDutyDelay + DriverPromptDelay)
                    {
                        Debug.WriteLine("[AUTO DUTY] FMCSA stopped timeout reached. Switching to ON DUTY.");
                        ForceDutyStatus(duty, DutyStatus.OnDuty);
                    }
                }
                else
                {
                    _stoppedSinceUtc = null;
                }
            });
        }

        private static void ForceDutyStatus(DutyStateMachine duty, DutyStatus status)
        {
            try
            {
                if (duty.TrySet(status))
                    return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[AUTO DUTY] TrySet failed: " + ex.Message);
            }

            var type = duty.GetType();

            foreach (var methodName in new[]
            {
                "Set",
                "SetStatus",
                "SetDutyStatus",
                "ChangeStatus",
                "ChangeDutyStatus",
                "ForceSet",
                "ForceSetStatus",
                "ForceDutyStatus"
            })
            {
                try
                {
                    var method = type.GetMethod(
                        methodName,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(DutyStatus) },
                        null);

                    if (method == null)
                        continue;

                    method.Invoke(duty, new object[] { status });
                    Debug.WriteLine($"[AUTO DUTY] Forced through {methodName}({status}).");
                    return;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AUTO DUTY] {methodName} failed: {ex.Message}");
                }
            }

            Debug.WriteLine("[AUTO DUTY] No usable duty transition method found.");
        }

        private static void RunOnUiThread(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;

            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            dispatcher.BeginInvoke(action);
        }
    }
}