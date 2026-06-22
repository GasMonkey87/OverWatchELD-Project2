using System;
using System.Linq;
using OverWatchELD.Services.Fleet;

namespace OverWatchELD.Services.LoadBoard
{
    public static class LoadBoardFleetLinkService
    {
        public static void ApplyLoadNumber(string? driverName, string? driverDiscordId, string loadNumber)
        {
            ApplyToTruck(driverName, driverDiscordId, loadNumber);
            ApplyToTrailerByReflection(driverName, driverDiscordId, loadNumber);
        }

        public static void ClearLoadNumber(string? driverName, string? driverDiscordId, string loadNumber)
        {
            ApplyToTruck(driverName, driverDiscordId, "", loadNumber);
            ApplyToTrailerByReflection(driverName, driverDiscordId, "", loadNumber);
        }

        private static void ApplyToTruck(string? driverName, string? driverDiscordId, string newLoadNumber, string? onlyIfLoadNumber = null)
        {
            try
            {
                var store = new FleetCommandStore();
                var all = store.LoadAll();
                var truck = all.FirstOrDefault(t =>
                    (!string.IsNullOrWhiteSpace(driverDiscordId) && Same(Read(t, "DriverDiscordId"), driverDiscordId)) ||
                    (!string.IsNullOrWhiteSpace(driverName) && Same(Read(t, "AssignedDriver"), driverName)) ||
                    Same(Read(t, "Status"), "Active"));

                if (truck == null) return;
                if (!string.IsNullOrWhiteSpace(onlyIfLoadNumber) && !Same(Read(truck, "CurrentLoadNumber"), onlyIfLoadNumber)) return;

                var prop = truck.GetType().GetProperty("CurrentLoadNumber");
                if (prop != null && prop.CanWrite)
                    prop.SetValue(truck, newLoadNumber ?? "");

                var updated = truck.GetType().GetProperty("UpdatedUtc");
                if (updated != null && updated.CanWrite)
                    updated.SetValue(truck, DateTimeOffset.UtcNow);

                store.Save(truck);
            }
            catch { }
        }

        private static void ApplyToTrailerByReflection(string? driverName, string? driverDiscordId, string newLoadNumber, string? onlyIfLoadNumber = null)
        {
            try
            {
                var asm = typeof(LoadBoardFleetLinkService).Assembly;
                var storeType = asm.GetType("OverWatchELD.Services.Fleet.TrailerFleetStore")
                    ?? asm.GetType("OverWatchELD.Services.Fleet.FleetTrailerStore")
                    ?? asm.GetType("OverWatchELD.Services.Fleet.TrailerCommandStore");

                if (storeType == null) return;

                var store = Activator.CreateInstance(storeType);
                if (store == null) return;

                var loadAll = storeType.GetMethod("LoadAll");
                var save = storeType.GetMethod("Save") ?? storeType.GetMethod("Upsert");
                if (loadAll == null || save == null) return;

                if (loadAll.Invoke(store, null) is not System.Collections.IEnumerable rows) return;

                foreach (var trailer in rows)
                {
                    if (trailer == null) continue;

                    var match =
                        (!string.IsNullOrWhiteSpace(driverDiscordId) && Same(Read(trailer, "DriverDiscordId"), driverDiscordId)) ||
                        (!string.IsNullOrWhiteSpace(driverName) && (Same(Read(trailer, "AssignedDriver"), driverName) || Same(Read(trailer, "DriverName"), driverName))) ||
                        Same(Read(trailer, "Status"), "Active");

                    if (!match) continue;
                    if (!string.IsNullOrWhiteSpace(onlyIfLoadNumber) && !Same(Read(trailer, "CurrentLoadNumber"), onlyIfLoadNumber) && !Same(Read(trailer, "LoadNumber"), onlyIfLoadNumber)) continue;

                    SetIfWritable(trailer, "CurrentLoadNumber", newLoadNumber ?? "");
                    SetIfWritable(trailer, "LoadNumber", newLoadNumber ?? "");
                    SetIfWritable(trailer, "UpdatedUtc", DateTimeOffset.UtcNow);
                    save.Invoke(store, new[] { trailer });
                    break;
                }
            }
            catch { }
        }

        private static void SetIfWritable(object obj, string propName, object value)
        {
            var prop = obj.GetType().GetProperty(propName);
            if (prop != null && prop.CanWrite)
                prop.SetValue(obj, value);
        }

        private static string Read(object obj, string propName)
        {
            try { return obj.GetType().GetProperty(propName)?.GetValue(obj)?.ToString()?.Trim() ?? ""; }
            catch { return ""; }
        }

        private static bool Same(string? a, string? b)
            => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
