using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using OverWatchELD.Services;

namespace OverWatchELD.Overlay
{
    public static class OverlayDataBridge
    {
        private sealed class OverlayHosClock
        {
            public TimeSpan DriveRemaining { get; set; } = TimeSpan.FromHours(11);
            public TimeSpan ShiftRemaining { get; set; } = TimeSpan.FromHours(14);
            public TimeSpan CycleRemaining { get; set; } = TimeSpan.FromHours(70);
            public TimeSpan BreakRemaining { get; set; } = TimeSpan.FromHours(8);
            public string Source { get; set; } = "fallback";
        }

        public static OverlaySnapshot Build()
        {
            var app = Application.Current as App;
            var telemetry = app?.Telemetry;
            var live = ReadValue(telemetry, "LastSnapshot");
            var dutyMachine = app?.DutyMachine;
            var hos = BuildHosClock(dutyMachine);

            var speed = FormatSpeed(live);
            var fuel = FormatFuel(live);
            var maintenance = FormatMaintenance(live);
            var route = BuildRoute(live, telemetry, app?.Session, app?.SessionState);
            var loadName = FirstText(live, telemetry, app?.Session, app?.SessionState, "CargoName", "CurrentLoad", "ActiveLoad", "LoadName", "Cargo") ?? "No active load";
            var connected = ReadBool(live, "Connected");
            var dutyText = FirstText(dutyMachine, "Current") ?? FirstText(dutyMachine, app?.Session, app?.SessionState, "CurrentDutyStatus", "DutyStatus", "Status", "CurrentStatus") ?? (connected == true ? "DRIVING" : "OFFLINE");

            var snapshot = new OverlaySnapshot
            {
                DutyStatus = FormatDutyStatus(dutyText),
                HosRemaining = FormatTimeSpan(hos.DriveRemaining),
                DriverName = FirstText(live, app?.Session, app?.SessionState, "DriverName", "CurrentDriver", "Username", "Name") ?? "Unknown Driver",
                LoadName = loadName,
                Route = route,
                Speed = speed,
                Fuel = fuel,
                Maintenance = maintenance,
                UpdatedAt = DateTime.Now
            };

            var clockLine = BuildClockLine(hos);

            if (live == null)
                snapshot.StatusLine = clockLine + " • Waiting for Telemetry.LastSnapshot.";
            else if (connected == false)
                snapshot.StatusLine = clockLine + " • Telemetry online, ATS not connected.";
            else
                snapshot.StatusLine = clockLine + " • Live ATS telemetry connected.";

            return snapshot;
        }

        private static OverlayHosClock BuildHosClock(object? dutyMachine)
        {
            var now = EldClock.UtcNow;

            try
            {
                var start = now.AddDays(-10);
                var events = DatabaseService.GetDutyEvents(start, now.AddHours(1));
                var hos = HosCalculator.GetCurrentClocks(events, now);

                var calculated = new OverlayHosClock
                {
                    DriveRemaining = ReadTimeSpan(hos, "DriveRemaining") ?? TimeSpan.FromHours(11),
                    ShiftRemaining = ReadTimeSpan(hos, "ShiftRemaining") ?? TimeSpan.FromHours(14),
                    CycleRemaining = ReadTimeSpan(hos, "CycleRemaining") ?? TimeSpan.FromHours(70),
                    BreakRemaining = ReadTimeSpan(hos, "BreakRemaining") ?? TimeSpan.FromHours(8),
                    Source = "calculator"
                };

                if (calculated.DriveRemaining != TimeSpan.Zero || calculated.ShiftRemaining != TimeSpan.Zero)
                    return calculated;
            }
            catch
            {
                // Use direct fallback below.
            }

            try
            {
                return BuildDirectDutyFallback(now, dutyMachine);
            }
            catch
            {
                return new OverlayHosClock { Source = "default" };
            }
        }

        private static OverlayHosClock BuildDirectDutyFallback(DateTimeOffset now, object? dutyMachine)
        {
            var dutyText = FirstText(dutyMachine, "Current") ?? "OffDuty";
            var dayStart = now.AddHours(-24);
            var cycleStart = now.AddDays(-8);
            var events = DatabaseService.GetDutyEvents(cycleStart, now.AddHours(1));
            var open = events.LastOrDefault(e => e.EndUtc == null) ?? events.LastOrDefault();
            var currentStart = open?.StartUtc ?? now;
            if (currentStart > now) currentStart = now;

            var currentElapsed = now - currentStart;
            if (currentElapsed < TimeSpan.Zero) currentElapsed = TimeSpan.Zero;

            TimeSpan driveUsed = TimeSpan.Zero;
            TimeSpan onDutyUsed = TimeSpan.Zero;
            TimeSpan cycleUsed = TimeSpan.Zero;

            foreach (var e in events)
            {
                var status = e.Status.ToString();
                var start = e.StartUtc < cycleStart ? cycleStart : e.StartUtc;
                var end = e.EndUtc ?? now;
                if (end > now) end = now;
                if (end <= start) continue;

                var span = end - start;
                if (IsDriving(status)) driveUsed += span;
                if (IsOnDuty(status)) cycleUsed += span;
                if (start >= dayStart && IsOnDuty(status)) onDutyUsed += span;
            }

            if (events.Count == 0)
            {
                if (IsDriving(dutyText))
                {
                    driveUsed = currentElapsed;
                    onDutyUsed = currentElapsed;
                    cycleUsed = currentElapsed;
                }
                else if (IsOnDuty(dutyText))
                {
                    onDutyUsed = currentElapsed;
                    cycleUsed = currentElapsed;
                }
            }

            return new OverlayHosClock
            {
                DriveRemaining = ClampRemaining(TimeSpan.FromHours(11) - driveUsed),
                ShiftRemaining = ClampRemaining(TimeSpan.FromHours(14) - onDutyUsed),
                CycleRemaining = ClampRemaining(TimeSpan.FromHours(70) - cycleUsed),
                BreakRemaining = IsDriving(dutyText) ? ClampRemaining(TimeSpan.FromHours(8) - currentElapsed) : TimeSpan.FromHours(8),
                Source = "direct"
            };
        }

        private static bool IsDriving(string? status)
            => (status ?? "").IndexOf("Driving", StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool IsOnDuty(string? status)
        {
            var s = status ?? "";
            return s.IndexOf("Driving", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   s.IndexOf("OnDuty", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   s.IndexOf("YardMove", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static TimeSpan ClampRemaining(TimeSpan value)
            => value < TimeSpan.Zero ? TimeSpan.Zero : value;

        private static string BuildClockLine(OverlayHosClock hos)
        {
            var drive = FormatTimeSpan(hos.DriveRemaining);
            var shift = FormatTimeSpan(hos.ShiftRemaining);
            var cycle = FormatTimeSpan(hos.CycleRemaining);
            var brk = FormatTimeSpan(hos.BreakRemaining);
            return $"Drive {drive} • Shift {shift} • Cycle {cycle} • Break {brk}";
        }

        private static string FormatDutyStatus(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "OFFLINE";

            return value.Replace("PersonalConveyance", "PC").Replace("YardMove", "YM").ToUpperInvariant();
        }

        private static string FormatTimeSpan(TimeSpan? value)
        {
            if (!value.HasValue)
                return "--:--";

            var time = value.Value;
            if (time < TimeSpan.Zero)
                time = TimeSpan.Zero;

            return $"{(int)time.TotalHours:00}:{time.Minutes:00}";
        }

        private static TimeSpan? ReadTimeSpan(object? source, string name)
        {
            var value = ReadValue(source, name);
            if (value is TimeSpan timeSpan)
                return timeSpan;

            if (value == null)
                return null;

            if (TimeSpan.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed))
                return parsed;

            return null;
        }

        private static string FormatSpeed(object? live)
        {
            var speedMps = ReadDouble(live, "SpeedMps");
            if (speedMps.HasValue)
                return $"{Math.Abs(speedMps.Value * 2.23694):0} MPH";

            var speedMph = ReadDouble(live, "SpeedMph") ?? ReadDouble(live, "SpeedMPH");
            if (speedMph.HasValue)
                return $"{Math.Abs(speedMph.Value):0} MPH";

            return "0 MPH";
        }

        private static string FormatFuel(object? live)
        {
            var gallons = ReadDouble(live, "FuelGallons");
            var pct = ReadDouble(live, "FuelPct");

            if (gallons.HasValue && pct.HasValue)
                return $"{gallons.Value:0} gal ({pct.Value:0}%)";

            if (gallons.HasValue)
                return $"{gallons.Value:0} gal";

            if (pct.HasValue)
                return $"{pct.Value:0}%";

            return "--";
        }

        private static string FormatMaintenance(object? live)
        {
            var damage = ReadDouble(live, "DamagePct");
            var trailerDamage = ReadDouble(live, "TrailerDamagePct");

            if (damage.HasValue || trailerDamage.HasValue)
            {
                var worst = Math.Max(damage ?? 0, trailerDamage ?? 0);
                return worst <= 0.5 ? "OK" : $"{worst:0}% DMG";
            }

            return FirstText(live, "MaintenanceStatus", "TruckStatus", "DamageStatus") ?? "READY";
        }

        private static string BuildRoute(params object?[] sources)
        {
            var pickupCity = FirstText(sources, "SourceCity", "PickupCity", "Pickup", "Origin", "From");
            var pickupCompany = FirstText(sources, "SourceCompany", "PickupCompany", "OriginCompany");
            var deliveryCity = FirstText(sources, "DestinationCity", "DeliveryCity", "Delivery", "Destination", "To");
            var deliveryCompany = FirstText(sources, "DestinationCompany", "DeliveryCompany");
            var remainingMiles = ReadDouble(sources.Length > 0 ? sources[0] : null, "RemainingMiles");

            var pickup = CombineLocation(pickupCompany, pickupCity, "Pickup");
            var delivery = CombineLocation(deliveryCompany, deliveryCity, "Delivery");
            var route = $"{pickup} → {delivery}";

            if (remainingMiles.HasValue)
                route += $" • {remainingMiles.Value:0} mi left";

            return route;
        }

        private static string CombineLocation(string? company, string? city, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(company) && !string.IsNullOrWhiteSpace(city))
                return $"{company} / {city}";

            if (!string.IsNullOrWhiteSpace(city))
                return city;

            if (!string.IsNullOrWhiteSpace(company))
                return company;

            return fallback;
        }

        private static string? FirstText(params object?[] sourcesAndNames)
        {
            if (sourcesAndNames.Length == 0)
                return null;

            var split = FindNameStart(sourcesAndNames);
            var sources = new object?[split];
            Array.Copy(sourcesAndNames, sources, split);

            var names = new string[sourcesAndNames.Length - split];
            for (var i = split; i < sourcesAndNames.Length; i++)
                names[i - split] = Convert.ToString(sourcesAndNames[i], CultureInfo.InvariantCulture) ?? string.Empty;

            return FirstText(sources, names);
        }

        private static string? FirstText(object?[] sources, params string[] names)
        {
            foreach (var source in sources)
            {
                foreach (var name in names)
                {
                    var value = ReadValue(source, name);
                    if (value == null)
                        continue;

                    var text = Convert.ToString(value, CultureInfo.InvariantCulture);
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }

            return null;
        }

        private static bool? ReadBool(object? source, string name)
        {
            var value = ReadValue(source, name);
            if (value is bool b)
                return b;

            if (bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed))
                return parsed;

            return null;
        }

        private static double? ReadDouble(object? source, string name)
        {
            var value = ReadValue(source, name);
            if (value == null)
                return null;

            if (value is double d) return d;
            if (value is float f) return f;
            if (value is int i) return i;
            if (value is long l) return l;
            if (value is decimal m) return (double)m;

            if (double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
                return number;

            return null;
        }

        private static object? ReadValue(object? source, string name)
        {
            if (source == null || string.IsNullOrWhiteSpace(name))
                return null;

            try
            {
                var type = source.GetType();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase;

                var property = type.GetProperty(name, flags);
                if (property != null && property.GetIndexParameters().Length == 0)
                    return property.GetValue(source);

                var field = type.GetField(name, flags);
                if (field != null)
                    return field.GetValue(source);

                var method = type.GetMethod(name, flags, null, Type.EmptyTypes, null);
                if (method != null)
                    return method.Invoke(source, null);
            }
            catch
            {
                return null;
            }

            return null;
        }

        private static int FindNameStart(object?[] values)
        {
            for (var i = 0; i < values.Length; i++)
            {
                if (values[i] is string)
                    return i;
            }

            return values.Length;
        }
    }
}
