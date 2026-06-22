using System;
using System.Globalization;
using System.Reflection;
using System.Windows;

namespace OverWatchELD.Overlay
{
    public static class OverlayDataBridge
    {
        public static OverlaySnapshot Build()
        {
            var app = Application.Current as App;
            var telemetry = app?.Telemetry;
            var dutyMachine = app?.DutyMachine;

            var snapshot = new OverlaySnapshot
            {
                DutyStatus = FirstText(dutyMachine, telemetry, "CurrentDutyStatus", "DutyStatus", "Status", "CurrentStatus") ?? "ON DUTY",
                HosRemaining = FirstTimeText(dutyMachine, telemetry, "DriveRemaining", "DriveTimeRemaining", "RemainingDriveTime", "HosRemaining", "HoursRemaining") ?? DateTime.Now.ToString("HH:mm"),
                DriverName = FirstText(app?.Session, app?.SessionState, telemetry, "DriverName", "CurrentDriver", "Username", "Name") ?? "OverWatch Driver",
                LoadName = FirstText(telemetry, app?.Session, app?.SessionState, "CurrentLoad", "ActiveLoad", "LoadName", "Cargo", "CargoName") ?? "No active load",
                Route = BuildRoute(telemetry, app?.Session, app?.SessionState),
                Speed = FirstNumberText(" MPH", telemetry, "SpeedMph", "CurrentSpeedMph", "Speed", "TruckSpeed", "SpeedMPH") ?? "0 MPH",
                Fuel = FirstNumberText(" gal", telemetry, "FuelGallons", "Fuel", "FuelAmount", "FuelLevel", "TruckFuel") ?? "--",
                Maintenance = FirstText(telemetry, app?.Session, app?.SessionState, "MaintenanceStatus", "TruckStatus", "DamageStatus") ?? "READY",
                UpdatedAt = DateTime.Now
            };

            snapshot.StatusLine = telemetry == null
                ? "Telemetry service not available yet."
                : "Live bridge active. Showing real values when matching ELD/ATS fields are available.";

            return snapshot;
        }

        private static string BuildRoute(params object?[] sources)
        {
            var pickup = FirstText(sources, "Pickup", "PickupCity", "Origin", "SourceCity", "From");
            var delivery = FirstText(sources, "Delivery", "DeliveryCity", "Destination", "DestinationCity", "To");

            if (!string.IsNullOrWhiteSpace(pickup) || !string.IsNullOrWhiteSpace(delivery))
                return $"{pickup ?? "Pickup"} → {delivery ?? "Delivery"}";

            return "Waiting for active load / route data";
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

        private static string? FirstTimeText(object? source1, object? source2, params string[] names)
        {
            foreach (var source in new[] { source1, source2 })
            {
                foreach (var name in names)
                {
                    var value = ReadValue(source, name);
                    if (value == null)
                        continue;

                    if (value is TimeSpan timeSpan)
                        return $"{(int)timeSpan.TotalHours:00}:{timeSpan.Minutes:00}";

                    var text = Convert.ToString(value, CultureInfo.InvariantCulture);
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }

            return null;
        }

        private static string? FirstNumberText(string suffix, object? source, params string[] names)
        {
            foreach (var name in names)
            {
                var value = ReadValue(source, name);
                if (value == null)
                    continue;

                if (double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
                    return $"{number:0}{suffix}";

                var text = Convert.ToString(value, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }

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
