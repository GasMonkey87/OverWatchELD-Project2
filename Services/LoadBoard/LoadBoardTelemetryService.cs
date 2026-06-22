using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using OverWatchELD.Services;
using OverWatchELD.Services.Fleet;
using OverWatchELD.Services.Discord;
using OverWatchELD.Views;

namespace OverWatchELD.Services.LoadBoard
{
    public sealed class LoadBoardTelemetryService
    {
        public static LoadBoardTelemetryService Shared { get; } = new();

        private bool _lastHadLoad;
        private DateTimeOffset _lastBolPopupUtc = DateTimeOffset.MinValue;

        private LoadBoardTelemetryService() { }

        public void OnTelemetry(TelemetrySnapshot snapshot)
        {
            if (snapshot == null) return;

            var hasLoad = HasRealLoad(snapshot);
            var driverName = Clean(snapshot.DriverName, "Unknown Driver");
            var driverId = GetDriverDiscordUserId(snapshot);
            var active = LoadBoardStore.GetActiveForDriver(driverId, driverName);

            if (!_lastHadLoad && hasLoad)
            {
                active ??= CreatePickupLoad(snapshot, driverName, driverId);
                MarkAtShipper(active, snapshot);
                OpenBolWindowOnce(active.LoadNumber);
            }
            else if (_lastHadLoad && hasLoad && active != null)
            {
                MarkInTransit(active, snapshot);
            }
            else if (_lastHadLoad && !hasLoad && active != null)
            {
                MarkDelivered(active, snapshot);
            }

            _lastHadLoad = hasLoad;
        }

        public void MarkBolComplete(string loadNumber)
        {
            var load = LoadBoardStore.LoadAll().FirstOrDefault(x => Same(x.LoadNumber, loadNumber));
            if (load == null) return;

            load.Status = "BOL Complete";
            load.BolCompletedUtc = DateTimeOffset.UtcNow;
            LoadBoardStore.Upsert(load);
            LoadBoardFleetLinkService.ApplyLoadNumber(load.DriverName, load.DriverDiscordId, load.LoadNumber);
        }

        private static LoadBoardLoad CreatePickupLoad(TelemetrySnapshot snap, string driverName, string driverId)
        {
            var load = new LoadBoardLoad
            {
                LoadNumber = LoadBoardStore.GenerateLoadNumber(),
                Status = "At Shipper",
                DriverName = driverName,
                DriverDiscordId = driverId,
                TruckName = Clean(FirstNonEmpty(snap.TruckName, snap.TruckMakeModel), "Truck"),
                Commodity = GetBestCargoName(snap),
                WeightLbs = GetBestWeight(snap),
                ShipperName = Clean(snap.SourceCompany, "Shipper"),
                ShipperCity = FirstNonEmpty(snap.SourceCity, FormatLocation(snap.City, snap.State, "")),
                ReceiverName = Clean(snap.DestinationCompany, "Receiver"),
                ReceiverCity = Clean(snap.DestinationCity, ""),
                CurrentLocation = FormatLocation(snap.City, snap.State, ""),
                RevenueUsd = ParseRevenue(snap.RevenueDisplay),
                RevenueSource = string.IsNullOrWhiteSpace(snap.RevenueDisplay) ? "" : "ATS Telemetry: " + snap.RevenueDisplay.Trim(),
                RevenueCapturedUtc = ParseRevenue(snap.RevenueDisplay) > 0 ? DateTimeOffset.UtcNow : null,
                CreatedUtc = DateTimeOffset.UtcNow,
                AtShipperUtc = DateTimeOffset.UtcNow
            };

            FillTruckAndTrailer(load, snap);
            LoadBoardStore.Upsert(load);
            LoadBoardFleetLinkService.ApplyLoadNumber(load.DriverName, load.DriverDiscordId, load.LoadNumber);
            PostPickupToDiscordOnce(load, snap);
            return load;
        }

        private static void MarkAtShipper(LoadBoardLoad load, TelemetrySnapshot snap)
        {
            load.Status = string.IsNullOrWhiteSpace(load.Status) || Same(load.Status, "Available") ? "At Shipper" : load.Status;
            load.AtShipperUtc ??= DateTimeOffset.UtcNow;
            load.CurrentLocation = FormatLocation(snap.City, snap.State, load.CurrentLocation);
            if (string.IsNullOrWhiteSpace(load.Commodity)) load.Commodity = GetBestCargoName(snap);
            if (load.WeightLbs <= 0) load.WeightLbs = GetBestWeight(snap);
            ApplyRevenue(load, snap);
            FillTruckAndTrailer(load, snap);
            LoadBoardStore.Upsert(load);
            LoadBoardFleetLinkService.ApplyLoadNumber(load.DriverName, load.DriverDiscordId, load.LoadNumber);
            PostPickupToDiscordOnce(load, snap);
        }

        private static void PostPickupToDiscordOnce(LoadBoardLoad load, TelemetrySnapshot snap)
        {
            if (load == null) return;
            if (load.PickupDiscordSentUtc.HasValue) return;

            load.PickupDiscordSentUtc = DateTimeOffset.UtcNow;
            LoadBoardStore.Upsert(load);

            var title = "Load Picked Up";
            var route = $"{Clean(load.ShipperName, "Shipper")} / {Clean(load.ShipperCity, "Origin")} → {Clean(load.ReceiverName, "Receiver")} / {Clean(load.ReceiverCity, "Destination")}";
            var message =
                $"Driver: {Clean(load.DriverName, "Unknown Driver")}\n" +
                $"Truck: {Clean(load.TruckName, "Truck")}\n" +
                $"Load #: {Clean(load.LoadNumber, "Pending")}\n" +
                $"Cargo: {Clean(load.Commodity, "Freight")}\n" +
                $"Weight: {(load.WeightLbs > 0 ? load.WeightLbs.ToString("N0") + " lbs" : "Unknown")}\n" +
                $"Route: {route}\n" +
                $"Revenue: {(load.RevenueUsd > 0 ? load.RevenueUsd.ToString("C2") : Clean(snap?.RevenueDisplay, "Pending from ATS"))}";

            var details =
                $"Status: Picked Up / At Shipper\n" +
                $"Current Location: {Clean(load.CurrentLocation, "Unknown")}\n" +
                $"Trailer: {Clean(FirstNonEmpty(load.TrailerNumber, load.TrailerName), "Unknown")}\n" +
                $"Time: {DateTimeOffset.Now:yyyy-MM-dd HH:mm}";

            DiscordNotificationPushService.PushFireAndForget("BOL", title, message, details);
        }

        private static void MarkInTransit(LoadBoardLoad load, TelemetrySnapshot snap)
        {
            if (Same(load.Status, "At Shipper") || Same(load.Status, "BOL Complete"))
            {
                load.Status = "In Transit";
                load.InTransitUtc ??= DateTimeOffset.UtcNow;
            }
            load.CurrentLocation = FormatLocation(snap.City, snap.State, load.CurrentLocation);
            ApplyRevenue(load, snap);
            LoadBoardStore.Upsert(load);
            LoadBoardFleetLinkService.ApplyLoadNumber(load.DriverName, load.DriverDiscordId, load.LoadNumber);
        }

        private static void MarkDelivered(LoadBoardLoad load, TelemetrySnapshot snap)
        {
            load.Status = "Delivered";
            load.DeliveredUtc = DateTimeOffset.UtcNow;
            load.CurrentLocation = FormatLocation(snap.City, snap.State, load.CurrentLocation);
            ApplyRevenue(load, snap);
            LoadBoardStore.Upsert(load);
            LoadBoardFleetLinkService.ClearLoadNumber(load.DriverName, load.DriverDiscordId, load.LoadNumber);

            _ = BolDiscordOnlyService.Shared.PostAsync(load.LoadNumber, load.DriverName, load.TruckName, load.Commodity, load.WeightLbs, load.ShipperCity, load.CurrentLocation, "Delivered");
        }

        private void OpenBolWindowOnce(string loadNumber)
        {
            if ((DateTimeOffset.UtcNow - _lastBolPopupUtc).TotalSeconds < 20)
                return;

            _lastBolPopupUtc = DateTimeOffset.UtcNow;

            try
            {
                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var win = new LoadBoardBolWindow(loadNumber);
                        win.Show();
                        win.Activate();
                    }
                    catch { }
                }));
            }
            catch { }
        }

        private static void FillTruckAndTrailer(LoadBoardLoad load, TelemetrySnapshot snap)
        {
            try
            {
                var store = new FleetCommandStore();
                var trucks = store.LoadAll();
                var truck = trucks.FirstOrDefault(t => Same(Read(t, "AssignedDriver"), load.DriverName) || Same(Read(t, "DriverDiscordId"), load.DriverDiscordId) || Same(Read(t, "Status"), "Active"));
                if (truck != null)
                {
                    load.TruckNumber = Read(truck, "TruckNumber");
                    load.TruckName = FirstNonEmpty(Read(truck, "TruckName"), Read(truck, "Model"), load.TruckName);
                }
            }
            catch { }

            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var storeType = asm.GetType("OverWatchELD.Services.Fleet.TrailerFleetStore") ?? asm.GetType("OverWatchELD.Services.Fleet.FleetTrailerStore") ?? asm.GetType("OverWatchELD.Services.Fleet.TrailerCommandStore");
                if (storeType == null) return;
                var store = Activator.CreateInstance(storeType);
                var rows = storeType.GetMethod("LoadAll")?.Invoke(store, null) as System.Collections.IEnumerable;
                if (rows == null) return;
                foreach (var trailer in rows)
                {
                    if (trailer == null) continue;
                    if (Same(Read(trailer, "AssignedDriver"), load.DriverName) || Same(Read(trailer, "DriverDiscordId"), load.DriverDiscordId) || Same(Read(trailer, "Status"), "Active"))
                    {
                        load.TrailerNumber = FirstNonEmpty(Read(trailer, "TrailerNumber"), Read(trailer, "UnitNumber"), Read(trailer, "Number"));
                        load.TrailerName = FirstNonEmpty(Read(trailer, "TrailerName"), Read(trailer, "Model"), Read(trailer, "Name"));
                        break;
                    }
                }
            }
            catch { }
        }


        private static void ApplyRevenue(LoadBoardLoad load, TelemetrySnapshot snap)
        {
            if (load == null || snap == null) return;

            var revenue = ParseRevenue(snap.RevenueDisplay);
            if (revenue <= 0) return;

            if (load.RevenueUsd <= 0)
                load.RevenueUsd = revenue;

            load.RevenueCapturedUtc ??= DateTimeOffset.UtcNow;
            load.RevenueSource = string.IsNullOrWhiteSpace(snap.RevenueDisplay)
                ? "ATS Telemetry"
                : "ATS Telemetry: " + snap.RevenueDisplay.Trim();
        }

        private static decimal ParseRevenue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0m;

            var chars = new System.Collections.Generic.List<char>();
            foreach (var c in value)
            {
                if (char.IsDigit(c) || c == '.' || c == '-')
                    chars.Add(c);
            }

            var text = new string(chars.ToArray());
            return decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var result)
                ? Math.Abs(result)
                : 0m;
        }

        private static bool HasRealLoad(TelemetrySnapshot snap)
            => (snap.CargoWeightLbs.HasValue && snap.CargoWeightLbs.Value > 1000) ||
               (snap.TrailerWeightLbs.HasValue && snap.TrailerWeightLbs.Value > 5000) ||
               (snap.GrossWeightLbs.HasValue && snap.GrossWeightLbs.Value > 8000);

        private static double GetBestWeight(TelemetrySnapshot snap)
        {
            if (snap.CargoWeightLbs.HasValue && snap.CargoWeightLbs.Value > 0) return snap.CargoWeightLbs.Value;
            if (snap.TrailerWeightLbs.HasValue && snap.TrailerWeightLbs.Value > 0) return snap.TrailerWeightLbs.Value;
            if (snap.GrossWeightLbs.HasValue && snap.GrossWeightLbs.Value > 0) return snap.GrossWeightLbs.Value;
            return 0;
        }

        private static string GetBestCargoName(TelemetrySnapshot snap)
        {
            if (snap.CargoWeightLbs.HasValue && snap.CargoWeightLbs.Value > 0) return $"Cargo ({snap.CargoWeightLbs.Value:N0} lbs)";
            if (snap.TrailerWeightLbs.HasValue && snap.TrailerWeightLbs.Value > 0) return $"Trailer Load ({snap.TrailerWeightLbs.Value:N0} lbs)";
            if (snap.GrossWeightLbs.HasValue && snap.GrossWeightLbs.Value > 0) return $"Freight ({snap.GrossWeightLbs.Value:N0} lbs gross)";
            return "Freight";
        }

        private static string GetDriverDiscordUserId(TelemetrySnapshot snap)
        {
            if (!string.IsNullOrWhiteSpace(snap.DriverId) && !Same(snap.DriverId, "driver")) return snap.DriverId.Trim();
            try
            {
                var ident = DiscordIdentityStore.Load();
                if (!string.IsNullOrWhiteSpace(ident?.DiscordUserId)) return ident.DiscordUserId.Trim();
            }
            catch { }
            return "";
        }

        private static string FormatLocation(string? city, string? state, string fallback)
        {
            city = (city ?? "").Trim(); state = (state ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(city) && !string.IsNullOrWhiteSpace(state)) return $"{city}, {state}";
            if (!string.IsNullOrWhiteSpace(city)) return city;
            if (!string.IsNullOrWhiteSpace(state)) return state;
            return fallback ?? "";
        }

        private static string Read(object obj, string prop)
        {
            try { return obj.GetType().GetProperty(prop)?.GetValue(obj)?.ToString()?.Trim() ?? ""; }
            catch { return ""; }
        }

        private static string Clean(string? value, string fallback)
        {
            value = (value ?? "").Trim();
            return string.IsNullOrWhiteSpace(value) || Same(value, "Driver") ? fallback : value;
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var v in values)
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            return "";
        }

        private static bool Same(string? a, string? b)
            => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
