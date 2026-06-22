// Services\CompanionStateBridge_SAFE.cs
// FULL COPY/REPLACE ADD-ON
// Purpose: keeps the phone Companion and desktop ELD state synced without changing dashboard/clocks rendering.

using OverWatchELD.Models;
using OverWatchELD.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;

namespace OverWatchELD.Services
{
    public static class CompanionStateBridgeSafe
    {
        private static readonly object _sync = new();
        private static DutyStatus _lastCompanionStatus = DutyStatus.OffDuty;
        private static DateTimeOffset _lastCompanionChangeUtc = DateTimeOffset.MinValue;

        public static async Task<bool> SetDutyAsync(string dutyValue, string source = "companion")
        {
            if (!TryParseDutyStatus(dutyValue, out var status))
                return false;

            var app = Application.Current as App;
            if (app?.Dispatcher == null)
                return ApplyDuty(status, source);

            return await app.Dispatcher.InvokeAsync(() => ApplyDuty(status, source));
        }

        public static object GetStateSnapshot()
        {
            var nowUtc = EldClock.UtcNow;
            var current = SafeCurrentStatus();
            var startUtc = SafeCurrentStartUtc(current, nowUtc);
            var telemetry = SafeTelemetryObject();

            return new
            {
                ok = true,
                generatedUtc = nowUtc,
                duty = new
                {
                    status = current.ToString(),
                    label = ToLabel(current),
                    shortStatus = ToShort(current),
                    currentStatusStartUtc = startUtc,
                    lastCompanionStatus = _lastCompanionStatus.ToString(),
                    lastCompanionChangeUtc = _lastCompanionChangeUtc == DateTimeOffset.MinValue ? (DateTimeOffset?)null : _lastCompanionChangeUtc
                },
                telemetry = telemetry,
                logs = GetTodayLogsSnapshot()
            };
        }

        public static object GetTodayLogsSnapshot()
        {
            try
            {
                DatabaseService.Initialize();

                var nowLocal = DateTime.Now;
                var startLocal = nowLocal.Date;
                var endLocal = startLocal.AddDays(1);
                var startUtc = new DateTimeOffset(startLocal).ToUniversalTime();
                var endUtc = new DateTimeOffset(endLocal).ToUniversalTime();

                var events = DatabaseService.GetDutyEvents(startUtc, endUtc) ?? new List<DutyEvent>();
                var rows = events
                    .OrderBy(e => e.StartUtc)
                    .Select(e => new
                    {
                        id = e.Id,
                        status = e.Status.ToString(),
                        label = ToLabel(e.Status),
                        shortStatus = ToShort(e.Status),
                        startUtc = e.StartUtc,
                        endUtc = e.EndUtc,
                        notes = e.Notes ?? string.Empty,
                        source = e.Source ?? string.Empty,
                        location = e.LocationText ?? string.Empty,
                        isEdited = e.IsEdited
                    })
                    .ToList();

                return new
                {
                    ok = true,
                    localDate = startLocal.ToString("yyyy-MM-dd"),
                    count = rows.Count,
                    events = rows
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, error = ex.GetBaseException().Message, events = Array.Empty<object>() };
            }
        }

        private static bool ApplyDuty(DutyStatus status, string source)
        {
            try
            {
                var current = SafeCurrentStatus();

                // Keep desktop memory state current even if DB already had this status.
                TryUpdateAppDutyMachine(status);

                if (current != status)
                {
                    ELDStateService.SetCurrentStatus(status, source == "companion" ? "Changed from Companion" : source);
                }

                // Force all live ELD clock/log bindings to re-read the database.
                TryRefreshDesktopState(status);

                lock (_sync)
                {
                    _lastCompanionStatus = status;
                    _lastCompanionChangeUtc = EldClock.UtcNow;
                }

                Debug.WriteLine($"[COMPANION SYNC] Duty synced to desktop: {status}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[COMPANION SYNC ERROR] " + ex.GetBaseException().Message);
                return false;
            }
        }

        private static DutyStatus SafeCurrentStatus()
        {
            try { return ELDStateService.CurrentStatus; } catch { return DutyStatus.OffDuty; }
        }

        private static DateTimeOffset SafeCurrentStartUtc(DutyStatus status, DateTimeOffset nowUtc)
        {
            try
            {
                var events = DatabaseService.GetDutyEvents(nowUtc.AddDays(-14), nowUtc.AddMinutes(1));
                var last = events?.OrderBy(e => e.StartUtc).LastOrDefault(e => e.Status == status);
                if (last != null) return last.StartUtc;
            }
            catch { }

            try { return ELDStateService.CurrentStatusStartUtc; } catch { return nowUtc; }
        }

        private static object? SafeTelemetryObject()
        {
            try
            {
                var app = Application.Current as App;
                var telemetry = app?.Telemetry;
                if (telemetry == null) return null;

                var t = ((object)telemetry).GetType();
                foreach (var name in new[] { "CurrentSnapshot", "Snapshot", "LastSnapshot", "Current", "Latest", "LatestTelemetry" })
                {
                    var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                    if (p != null) return p.GetValue(telemetry);

                    var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                    if (f != null) return f.GetValue(telemetry);
                }
            }
            catch { }

            return null;
        }

        private static void TryUpdateAppDutyMachine(DutyStatus status)
        {
            try
            {
                var app = Application.Current as App;
                var machine = app?.DutyMachine;
                if (machine == null) return;

                var target = (object)machine;
                var label = ToShort(status);
                var longLabel = ToLabel(status);

                InvokeIfExists(target, "SetStatus", label);
                InvokeIfExists(target, "SetDuty", status);
                InvokeIfExists(target, "SetDutyStatus", status);
                InvokeIfExists(target, "ChangeDutyStatus", status);
                InvokeIfExists(target, "NotifyEventAdded");
                InvokeIfExists(target, "NotifyStateChanged");

                SetPropertyIfExists(target, "Current", status);
                SetPropertyIfExists(target, "CurrentStatus", label);
                SetPropertyIfExists(target, "Status", longLabel);
            }
            catch { }
        }

        private static void TryRefreshDesktopState(DutyStatus status)
        {
            try { DashboardClocksLiveViewModel.Shared.RefreshNow(); } catch { }

            try
            {
                var app = Application.Current as App;
                var main = app?.MainWindow;
                if (main == null) return;

                InvokeIfExists(main, "RefreshDashboard");
                InvokeIfExists(main, "RefreshDashboardAsync");
                InvokeIfExists(main, "RefreshLogs");
                InvokeIfExists(main, "RefreshLogsAsync");
                InvokeIfExists(main, "RefreshDutyStatus");
                InvokeIfExists(main, "RefreshDutyStatusAsync");
                InvokeIfExists(main, "RefreshCompanionStatusUi");
            }
            catch { }
        }

        private static void InvokeIfExists(object target, string methodName, params object[] args)
        {
            try
            {
                var methods = target.GetType()
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase)
                    .Where(m => string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase));

                foreach (var m in methods)
                {
                    var p = m.GetParameters();
                    if (p.Length != args.Length) continue;

                    var finalArgs = new object?[args.Length];
                    var ok = true;
                    for (var i = 0; i < args.Length; i++)
                    {
                        if (TryConvert(args[i], p[i].ParameterType, out var converted))
                            finalArgs[i] = converted;
                        else
                        {
                            ok = false;
                            break;
                        }
                    }

                    if (!ok) continue;
                    m.Invoke(target, finalArgs);
                    return;
                }
            }
            catch { }
        }

        private static void SetPropertyIfExists(object target, string propertyName, object value)
        {
            try
            {
                var p = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                if (p == null || !p.CanWrite) return;
                if (TryConvert(value, p.PropertyType, out var converted))
                    p.SetValue(target, converted);
            }
            catch { }
        }

        private static bool TryConvert(object input, Type targetType, out object? value)
        {
            value = null;
            try
            {
                var nonNullType = Nullable.GetUnderlyingType(targetType) ?? targetType;
                if (input == null)
                    return !nonNullType.IsValueType;

                if (nonNullType.IsInstanceOfType(input))
                {
                    value = input;
                    return true;
                }

                if (nonNullType.IsEnum)
                {
                    if (input is DutyStatus ds)
                    {
                        value = Enum.Parse(nonNullType, ds.ToString(), true);
                        return true;
                    }
                    value = Enum.Parse(nonNullType, input.ToString() ?? "OffDuty", true);
                    return true;
                }

                if (nonNullType == typeof(string))
                {
                    value = input.ToString();
                    return true;
                }

                value = Convert.ChangeType(input, nonNullType);
                return true;
            }
            catch { return false; }
        }

        private static bool TryParseDutyStatus(string value, out DutyStatus status)
        {
            var s = (value ?? string.Empty).Trim().ToLowerInvariant().Replace("_", "").Replace("-", "");
            status = s switch
            {
                "off" or "offduty" => DutyStatus.OffDuty,
                "sb" or "sleeper" or "sleeperberth" => DutyStatus.Sleeper,
                "driving" or "drive" or "d" => DutyStatus.Driving,
                "on" or "onduty" or "on-duty" => DutyStatus.OnDuty,
                "pc" or "personalconveyance" => DutyStatus.PersonalConveyance,
                "ym" or "yardmove" => DutyStatus.YardMove,
                _ => DutyStatus.Unknown
            };

            if (status != DutyStatus.Unknown) return true;
            return Enum.TryParse(value, true, out status) && status != DutyStatus.Unknown;
        }

        private static string ToShort(DutyStatus status) => status switch
        {
            DutyStatus.OffDuty => "OFF",
            DutyStatus.Sleeper => "SB",
            DutyStatus.Driving => "D",
            DutyStatus.OnDuty => "ON",
            DutyStatus.PersonalConveyance => "PC",
            DutyStatus.YardMove => "YM",
            _ => "OFF"
        };

        private static string ToLabel(DutyStatus status) => status switch
        {
            DutyStatus.OffDuty => "Off Duty",
            DutyStatus.Sleeper => "Sleeper Berth",
            DutyStatus.Driving => "Driving",
            DutyStatus.OnDuty => "On Duty",
            DutyStatus.PersonalConveyance => "Personal Conveyance",
            DutyStatus.YardMove => "Yard Move",
            _ => "Off Duty"
        };
    }
}
