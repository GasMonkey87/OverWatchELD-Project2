using OverWatchELD.Models.Fleet;
using System;
using System.Linq;

using OverWatchELD.Services;

namespace OverWatchELD.Services.Fleet
{
    public static class TelemetryFleetSyncService
    {
        public static void SyncActiveTruckFromTelemetry(object? snapshot)
        {
            try
            {
                if (snapshot == null)
                    return;

                var driverName = GetCurrentDriverName(snapshot);
                var driverDiscordId = GetCurrentDiscordId(snapshot);

                var truckName = FirstNonEmpty(
                    Read(snapshot, "TruckName"),
                    Read(snapshot, "TruckMakeModel"),
                    Read(snapshot, "VehicleName"),
                    Read(snapshot, "TruckModel"));

                var makeModel = FirstNonEmpty(
                    Read(snapshot, "TruckMakeModel"),
                    Join(Read(snapshot, "TruckMake"), Read(snapshot, "TruckModel")),
                    Read(snapshot, "Model"),
                    truckName);

                var plate = FirstNonEmpty(
                    Read(snapshot, "TruckLicensePlate"),
                    Read(snapshot, "LicensePlate"),
                    Read(snapshot, "TruckPlate"),
                    Read(snapshot, "Plate"));

                var city = Read(snapshot, "City");
                var state = Read(snapshot, "State");

                var location = FirstNonEmpty(
                    Read(snapshot, "Location"),
                    JoinLocation(city, state));

                var fuel = Clamp(
                    ReadDouble(snapshot, "FuelPercent",
                    ReadDouble(snapshot, "FuelPct", 100)), 0, 100);

                var truckDamage = Clamp(
                    ReadDouble(snapshot, "TruckDamagePercent",
                    ReadDouble(snapshot, "DamagePercent",
                    ReadDouble(snapshot, "DamagePct", 0))), 0, 100);

                var trailerDamage = Clamp(
                    ReadDouble(snapshot, "TrailerDamagePercent",
                    ReadDouble(snapshot, "TrailerDamagePct", 0)), 0, 100);

                if (truckDamage < 2) truckDamage = 0;
                if (trailerDamage < 2) trailerDamage = 0;

                var speedMph = Math.Round(Math.Abs(ReadDouble(snapshot, "SpeedMps", 0) * 2.23694), 1);

                var trailerName = Read(snapshot, "TrailerName");
                var cargoName = Read(snapshot, "CargoName");
                var destinationCity = Read(snapshot, "DestinationCity");
                var destinationCompany = Read(snapshot, "DestinationCompany");
                var destinationText = BuildDestination(destinationCity, destinationCompany);
                var remainingMiles = Math.Max(0, ReadDouble(snapshot, "RemainingMiles", 0));

                var warnings = BuildWarnings(fuel, truckDamage, trailerDamage);

                var odometer = Math.Max(
                    0,
                    ReadDouble(snapshot, "OdometerMiles",
                    ReadDouble(snapshot, "Odometer", 0)));

                var mapX = ReadNullableDouble(snapshot,
                    "MapX",
                    "WorldX",
                    "X");

                var mapY = ReadNullableDouble(snapshot,
                    "MapY",
                    "WorldY",
                    "Y");

                if (string.IsNullOrWhiteSpace(truckName) &&
                    string.IsNullOrWhiteSpace(makeModel) &&
                    string.IsNullOrWhiteSpace(plate))
                    return;

                var store = new FleetCommandStore();
                var activeStore = new ActiveTruckSelectionStore();
                var all = store.LoadAll();

                var selectedTruck = activeStore.GetActiveTruck(store, driverName, driverDiscordId);

                if (selectedTruck != null && !TruckMatchesTelemetry(selectedTruck, truckName, makeModel, plate))
                {
                    selectedTruck.Status = "Selected - ATS mismatch";
                    selectedTruck.UpdatedUtc = DateTimeOffset.UtcNow;
                    store.Save(selectedTruck);
                    return;
                }

                var truck = selectedTruck;

                if (truck == null)
                {
                    truck = all.FirstOrDefault(t =>
                        Same(t.PlateNumber, plate) ||
                        Same(t.TruckName, truckName) ||
                        Same(t.Model, makeModel));
                }

                if (truck == null)
                {
                    truck = new FleetCommandTruck
                    {
                        TruckNumber = store.GetNextAvailableTruckNumber(),
                        ServiceDueDate = DateTime.Today.AddDays(14),
                        InspectionDueDate = DateTime.Today.AddDays(7)
                    };
                }

                // only one ACTIVE truck per driver
                foreach (var other in all)
                {
                    if (other == null)
                        continue;

                    if (Same(other.Id, truck.Id))
                        continue;

                    if (Same(other.AssignedDriver, driverName) ||
                        Same(other.DriverDiscordId, driverDiscordId))
                    {
                        other.Status = "Inactive";
                        other.CurrentLoadNumber = "";
                        other.UpdatedUtc = DateTimeOffset.UtcNow;

                        other.IsParked = true;
                        other.ParkedUtc = DateTime.UtcNow;

                        store.Save(other);
                    }
                }

                if (!string.IsNullOrWhiteSpace(truckName))
                    truck.TruckName = truckName;

                if (!string.IsNullOrWhiteSpace(makeModel))
                    truck.Model = makeModel;

                if (!string.IsNullOrWhiteSpace(plate))
                    truck.PlateNumber = plate;
                truck.Location = location;

                if (!IsPlaceholderDriver(driverName))
                    truck.AssignedDriver = driverName;

                if (!string.IsNullOrWhiteSpace(driverDiscordId))
                    truck.DriverDiscordId = driverDiscordId;

                truck.FuelPercent = fuel;

                truck.HealthPercent =
                    (int)Math.Round(
                        Clamp(100 - truckDamage, 0, 100));

                truck.OdometerMiles = odometer;

                // Extra live telemetry fields for owner/admin Fleet Management views.
                // Reflection keeps this compatible even if older FleetCommandTruck models
                // do not yet declare these properties.
                SetDoubleIfExists(truck, "SpeedMph", speedMph);
                SetDoubleIfExists(truck, "CurrentSpeedMph", speedMph);
                SetDoubleIfExists(truck, "TruckDamagePercent", truckDamage);
                SetDoubleIfExists(truck, "DamagePercent", truckDamage);
                SetDoubleIfExists(truck, "TrailerDamagePercent", trailerDamage);
                SetStringIfExists(truck, "TrailerName", trailerName);
                SetStringIfExists(truck, "CargoName", cargoName);
                SetStringIfExists(truck, "DestinationCity", destinationCity);
                SetStringIfExists(truck, "DestinationCompany", destinationCompany);
                SetStringIfExists(truck, "Destination", destinationText);
                SetDoubleIfExists(truck, "RemainingMiles", remainingMiles);
                SetStringIfExists(truck, "Warnings", warnings);
                SetStringIfExists(truck, "LiveWarnings", warnings);

                truck.Status = "Active";
                truck.UpdatedUtc = DateTimeOffset.UtcNow;

                // LAST KNOWN LOCATION
                truck.LastKnownMapX = mapX;
                truck.LastKnownMapY = mapY;
                truck.LastKnownCity = city;
                truck.LastKnownState = state;

                // truck currently active
                truck.IsParked = false;

                store.Save(truck);

                DriverProfileMasterStore.LinkTruck(
                    truck.DriverDiscordId,
                    truck.AssignedDriver,
                    truck.AssignedDriver,
                    truck.TruckNumber,
                    truck.TruckName,
                    truck.PlateNumber,
                    "",
                    "Telemetry Connected Truck",
                    current: true);
            }
            catch
            {
            }
        }

        public static void MarkTruckOffline(string? truckNumber)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(truckNumber))
                    return;

                var store = new FleetCommandStore();

                var truck = store.LoadAll()
                    .FirstOrDefault(x =>
                        Same(x.TruckNumber, truckNumber));

                if (truck == null)
                    return;

                truck.IsParked = true;
                truck.ParkedUtc = DateTime.UtcNow;

                if (string.IsNullOrWhiteSpace(truck.Status) ||
                    Same(truck.Status, "Active"))
                {
                    truck.Status = "Offline";
                }

                truck.UpdatedUtc = DateTimeOffset.UtcNow;

                store.Save(truck);
            }
            catch
            {
            }
        }


        private static bool TruckMatchesTelemetry(FleetCommandTruck truck, string? truckName, string? makeModel, string? plate)
        {
            if (truck == null)
                return false;

            if (!string.IsNullOrWhiteSpace(plate) && Same(truck.PlateNumber, plate))
                return true;

            if (!string.IsNullOrWhiteSpace(truckName) &&
                (Same(truck.TruckName, truckName) || Same(truck.Model, truckName) || Same(truck.TruckNumber, truckName)))
                return true;

            if (!string.IsNullOrWhiteSpace(makeModel) &&
                (Same(truck.Model, makeModel) || Same(truck.TruckName, makeModel)))
                return true;

            return false;
        }

        private static string GetCurrentDriverName(object snapshot)
        {
            var fromSnapshot = FirstNonEmpty(
                CleanDriverName(Read(snapshot, "DriverName")),
                CleanDriverName(Read(snapshot, "Driver")),
                CleanDriverName(Read(snapshot, "DiscordUsername")),
                CleanDriverName(Read(snapshot, "Username")));

            if (!string.IsNullOrWhiteSpace(fromSnapshot))
                return fromSnapshot;

            try
            {
                var ident = DiscordIdentityStore.Load();

                if (!string.IsNullOrWhiteSpace(ident?.DiscordUsername))
                    return ident.DiscordUsername.Trim();
            }
            catch { }

            try
            {
                var app = System.Windows.Application.Current as App;

                if (!IsPlaceholderDriver(app?.Session?.DriverName))
                    return app!.Session!.DriverName.Trim();
            }
            catch { }

            return CleanDriverName(EldDriverIdentityResolver.DriverName());
        }


        private static string CleanDriverName(string? value)
        {
            value = value?.Trim() ?? "";

            return IsPlaceholderDriver(value) ? "" : value;
        }

        private static bool IsPlaceholderDriver(string? value)
        {
            value = value?.Trim() ?? "";

            return string.IsNullOrWhiteSpace(value) ||
                   Same(value, "Driver") ||
                   Same(value, "Unknown Driver") ||
                   Same(value, "User");
        }

        private static string GetCurrentDiscordId(object snapshot)
        {
            var fromSnapshot = FirstNonEmpty(
                Read(snapshot, "DriverDiscordUserId"),
                Read(snapshot, "DiscordUserId"),
                Read(snapshot, "DiscordId"));

            if (!string.IsNullOrWhiteSpace(fromSnapshot))
                return fromSnapshot;

            try
            {
                var ident = DiscordIdentityStore.Load();

                if (!string.IsNullOrWhiteSpace(ident?.DiscordUserId))
                    return ident.DiscordUserId.Trim();
            }
            catch { }

            return "";
        }

        private static string Read(object obj, string name)
        {
            try
            {
                return obj.GetType()
                    .GetProperty(name)?
                    .GetValue(obj)?
                    .ToString()?
                    .Trim() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static double ReadDouble(object obj, string name, double fallback)
        {
            var raw = Read(obj, name)
                .Replace("%", "")
                .Replace(",", "");

            if (double.TryParse(raw, out var v))
            {
                if (v > 0 &&
                    v <= 1 &&
                    name.Contains("Percent", StringComparison.OrdinalIgnoreCase))
                {
                    return v * 100;
                }

                return v;
            }

            return fallback;
        }

        private static double? ReadNullableDouble(object obj, params string[] names)
        {
            foreach (var n in names)
            {
                var raw = Read(obj, n);

                if (double.TryParse(raw, out var v))
                    return v;
            }

            return null;
        }

        private static bool Same(string? a, string? b)
        {
            return !string.IsNullOrWhiteSpace(a) &&
                   !string.IsNullOrWhiteSpace(b) &&
                   string.Equals(
                       a.Trim(),
                       b.Trim(),
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            return values
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))
                ?.Trim() ?? "";
        }

        private static string Join(string? a, string? b)
        {
            return string.Join(
                " ",
                new[] { a, b }
                .Where(x => !string.IsNullOrWhiteSpace(x)))
                .Trim();
        }

        private static string JoinLocation(string? city, string? state)
        {
            city = city?.Trim() ?? "";
            state = state?.Trim() ?? "";

            if (!string.IsNullOrWhiteSpace(city) &&
                !string.IsNullOrWhiteSpace(state))
            {
                return $"{city}, {state}";
            }

            return FirstNonEmpty(city, state);
        }


        private static string BuildDestination(string? city, string? company)
        {
            city = city?.Trim() ?? "";
            company = company?.Trim() ?? "";

            if (!string.IsNullOrWhiteSpace(city) && !string.IsNullOrWhiteSpace(company))
                return $"{city} - {company}";

            return FirstNonEmpty(city, company);
        }

        private static string BuildWarnings(double fuel, double truckDamage, double trailerDamage)
        {
            var parts = new System.Collections.Generic.List<string>();

            if (fuel > 0 && fuel <= 10) parts.Add("LOW FUEL");
            if (truckDamage >= 15) parts.Add("TRUCK DAMAGE");
            if (trailerDamage >= 15) parts.Add("TRAILER DAMAGE");

            return parts.Count == 0 ? "None" : string.Join(" • ", parts);
        }

        private static void SetStringIfExists(object obj, string name, string? value)
        {
            try
            {
                var prop = obj.GetType().GetProperty(name);
                if (prop == null || !prop.CanWrite) return;

                if (prop.PropertyType == typeof(string) || prop.PropertyType == typeof(string))
                    prop.SetValue(obj, value ?? "");
            }
            catch
            {
            }
        }

        private static void SetDoubleIfExists(object obj, string name, double value)
        {
            try
            {
                var prop = obj.GetType().GetProperty(name);
                if (prop == null || !prop.CanWrite) return;

                var t = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

                if (t == typeof(double)) prop.SetValue(obj, value);
                else if (t == typeof(float)) prop.SetValue(obj, (float)value);
                else if (t == typeof(decimal)) prop.SetValue(obj, (decimal)value);
                else if (t == typeof(int)) prop.SetValue(obj, (int)Math.Round(value));
                else if (t == typeof(long)) prop.SetValue(obj, (long)Math.Round(value));
                else if (t == typeof(string)) prop.SetValue(obj, value.ToString("0.##"));
            }
            catch
            {
            }
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}