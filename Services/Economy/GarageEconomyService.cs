using System;
using System.Linq;

namespace OverWatchELD.Services.Economy
{
    public static class GarageEconomyService
    {
        public static void PostDailyGarageIncome()
        {
            try
            {
                var garages = VtcGarageStore.Load();

                foreach (var garage in garages.Where(g => g.IsOwned))
                {
                    var garageId = garage.Id ?? "";
                    var city = garage.CityName ?? "";
                    var state = garage.State ?? "";

                    var amount = CalculateDailyIncome(garage);
                    if (amount <= 0)
                        continue;

                    var key = $"GarageDaily:{garageId}:{DateTime.UtcNow:yyyyMMdd}";

                    if (EconomyStore.LoadTransactions().Any(x =>
                        string.Equals(x.Type, key, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    EconomyStore.AddTransaction(new Models.Economy.EconomyTransaction
                    {
                        Type = key,
                        Category = "Garage Income",
                        Source = "Garage Ownership",
                        Amount = amount,
                        GarageId = garageId,
                        Description = $"Daily garage income: {city}, {state}",
                        Notes = $"Size: {garage.Size} • Fuel Station: {(garage.HasFuelStation ? "Yes" : "No")}"
                    });
                }
            }
            catch
            {
            }
        }

        public static decimal CalculateDailyIncome(dynamic garage)
        {
            try
            {
                var size = ((string?)garage.Size ?? "Small").Trim().ToLowerInvariant();

                decimal baseIncome = size switch
                {
                    "large" => 2500m,
                    "medium" or "med" => 1400m,
                    _ => 650m
                };

                if (garage.HasFuelStation)
                    baseIncome += 450m;

                var assigned = garage.AssignedTruckNumbers?.Count ?? 0;
                baseIncome += assigned * 225m;

                return baseIncome;
            }
            catch
            {
                return 0m;
            }
        }
    }
}
