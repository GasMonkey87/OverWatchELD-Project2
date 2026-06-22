using System;
using System.Globalization;
using System.Reflection;
using System.Windows;
using OverWatchELD.Services;

namespace OverWatchELD.Overlay
{
    public static class OverlayDataBridge
    {
        public static OverlaySnapshot Build()
        {
            var app = Application.Current as App;
            var telemetry = app?.Telemetry;
            var live = ReadValue(telemetry, "LastSnapshot");
            var dutyMachine = app?.DutyMachine;
            var hos = BuildHosSnapshot();

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
                HosRemaining = hos != null ? FormatTimeSpan(ReadTimeSpan(hos, "DriveRemaining")) : "--:--",
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

        private static object? BuildHosSnapshot()
        {
            try
            {
                var now = EldClock.UtcNow;
                var start = now.AddDays(-10);
                var events = DatabaseService.GetDutyEvents(start, now.AddHours(1));
                return HosCalculator.GetCurrentClocks(events, now);
            }
            catch
            {
                return null;
            }
        }

        private static string BuildClockLine(object? hos)
        {
            if (hos == null)
                return "HOS clocks unavailable";

            var drive = FormatTimeSpan(ReadTimeSpan(hos, "DriveRemaining"));
            var shift = FormatTimeSpan(ReadTimeSpan(hos, "ShiftRemaining"));
            var cycle = FormatTimeSpan(ReadTimeSpan(hos, "CycleRemaining"));
            var brk = FormatTimeSpan(ReadTimeSpan(hos, "BreakRemaining"));

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
