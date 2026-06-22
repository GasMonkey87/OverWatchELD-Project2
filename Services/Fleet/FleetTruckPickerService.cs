using System;
using System.Collections.Generic;
using System.Linq;
using OverWatchELD.Models.Fleet;

namespace OverWatchELD.Services.Fleet
{
    public sealed class FleetTruckPickerService
    {
        private readonly FleetCommandStore _fleetCommandStore = new();
        private readonly FleetTruckRepository _fleetTruckRepo = new();

        public List<string> LoadTruckNumbers(bool includeAny = false)
        {
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (var t in _fleetCommandStore.LoadAll())
                {
                    var truckNumber = (t.TruckNumber ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(truckNumber))
                        set.Add(truckNumber);
                }
            }
            catch
            {
            }

            try
            {
                foreach (var t in _fleetTruckRepo.LoadAll())
                {
                    var truckNumber = FirstNonEmpty(
                        ReadString(t, "TruckNumber"),
                        ReadString(t, "UnitNumber"),
                        ReadString(t, "Number"),
                        ReadString(t, "Id"));

                    if (!string.IsNullOrWhiteSpace(truckNumber))
                        set.Add(truckNumber.Trim());
                }
            }
            catch
            {
            }

            var list = set.ToList();

            if (includeAny)
                list.Insert(0, "Any");

            return list;
        }

        private static string ReadString(object obj, string propertyName)
        {
            try
            {
                var prop = obj.GetType().GetProperty(propertyName);
                var value = prop?.GetValue(obj);
                return value?.ToString()?.Trim() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }
    }
}
