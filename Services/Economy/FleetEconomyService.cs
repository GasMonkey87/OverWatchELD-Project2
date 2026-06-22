using OverWatchELD.Models.Economy;
using System;
using System.Linq;
using System.Reflection;

namespace OverWatchELD.Services.Economy
{
    public static class FleetEconomyService
    {
        public static EconomySummary GetSummary() => EconomyStore.BuildSummary();

        public static EconomyTransaction AddIncome(
            decimal amount,
            string category,
            string description,
            string source = "",
            string driverName = "",
            string truckNumber = "",
            string truckName = "",
            string loadNumber = "",
            string garageId = "",
            string notes = "")
        {
            return EconomyStore.AddTransaction(new EconomyTransaction
            {
                Type = "Income",
                Category = category,
                Source = source,
                Amount = Math.Abs(amount),
                Description = description,
                DriverName = driverName,
                TruckNumber = truckNumber,
                TruckName = truckName,
                LoadNumber = loadNumber,
                GarageId = garageId,
                Notes = notes
            });
        }

        public static EconomyTransaction AddExpense(
            decimal amount,
            string category,
            string description,
            string source = "",
            string driverName = "",
            string truckNumber = "",
            string truckName = "",
            string loadNumber = "",
            string garageId = "",
            string notes = "")
        {
            return EconomyStore.AddTransaction(new EconomyTransaction
            {
                Type = "Expense",
                Category = category,
                Source = source,
                Amount = -Math.Abs(amount),
                Description = description,
                DriverName = driverName,
                TruckNumber = truckNumber,
                TruckName = truckName,
                LoadNumber = loadNumber,
                GarageId = garageId,
                Notes = notes
            });
        }

        public static bool TryPostDeliveredLoadPayout(object? job)
        {
            if (job == null)
                return false;

            try
            {
                var loadNumber = FirstNonEmpty(Read(job, "LoadNumber"), Read(job, "Id"));

                if (string.IsNullOrWhiteSpace(loadNumber))
                    return false;

                if (EconomyStore.HasTransactionForLoad(loadNumber, "LoadPayout"))
                    return false;

                var status = Read(job, "Status");
                var deliveredUtc = Read(job, "DeliveredUtc");

                var isDelivered =
                    status.Equals("Delivered", StringComparison.OrdinalIgnoreCase) ||
                    status.Equals("BOL Complete", StringComparison.OrdinalIgnoreCase) ||
                    !string.IsNullOrWhiteSpace(deliveredUtc);

                if (!isDelivered)
                    return false;

                var revenue =
                    ReadDecimal(job, "RevenueUsd") ??
                    ReadDecimal(job, "Payout") ??
                    ReadDecimal(job, "Pay") ??
                    EstimateLoadPayout(job);

                if (revenue <= 0)
                    revenue = EstimateLoadPayout(job);

                var driver = FirstNonEmpty(Read(job, "AssignedDriver"), Read(job, "ClaimedBy"));
                var truck = FirstNonEmpty(Read(job, "AssignedTruck"), Read(job, "LastKnownTruckName"));
                var cargo = Read(job, "Cargo");
                var miles = ReadDouble(job, "ActualDrivenMiles") ?? ReadDouble(job, "Miles") ?? 0;

                var tx = new EconomyTransaction
                {
                    Type = "LoadPayout",
                    Category = "Dispatch",
                    Source = "Dispatch Tracker",
                    Amount = revenue,
                    LoadNumber = loadNumber,
                    DriverName = driver,
                    TruckName = truck,
                    Description = $"Delivered load {loadNumber} payout",
                    Notes = $"{cargo} • {miles:N0} miles"
                };

                EconomyStore.AddTransaction(tx);

                // Driver payroll expense, default 25% of load payout.
                var driverPay = Math.Round(revenue * 0.25m, 2);
                if (driverPay > 0 && !string.IsNullOrWhiteSpace(driver))
                {
                    EconomyStore.AddTransaction(new EconomyTransaction
                    {
                        Type = "DriverPayroll",
                        Category = "Payroll",
                        Source = "Dispatch Tracker",
                        Amount = -driverPay,
                        LoadNumber = loadNumber,
                        DriverName = driver,
                        TruckName = truck,
                        Description = $"Driver payroll for load {loadNumber}",
                        Notes = "Default driver payroll: 25% of load payout"
                    });
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void SyncDeliveredDispatchJobs()
        {
            try
            {
                foreach (var job in DispatchService.Jobs.ToList())
                    TryPostDeliveredLoadPayout(job);
            }
            catch
            {
            }
        }

        public static EconomyTransaction PostFuelExpense(
            decimal amount,
            string driverName = "",
            string truckNumber = "",
            string truckName = "",
            string notes = "")
        {
            return AddExpense(
                amount,
                "Fuel",
                "Fuel purchase",
                "Fleet",
                driverName,
                truckNumber,
                truckName,
                notes: notes);
        }

        public static EconomyTransaction PostMaintenanceExpense(
            decimal amount,
            string truckNumber = "",
            string truckName = "",
            string notes = "")
        {
            return AddExpense(
                amount,
                "Maintenance",
                "Maintenance / repair expense",
                "Maintenance",
                truckNumber: truckNumber,
                truckName: truckName,
                notes: notes);
        }

        public static EconomyTransaction PostGaragePurchase(
            decimal amount,
            string garageId,
            string city,
            string state)
        {
            return AddExpense(
                amount,
                "Garage",
                $"Garage purchase: {city}, {state}",
                "Garage Ownership",
                garageId: garageId);
        }

        public static EconomyTransaction PostGarageIncome(
            decimal amount,
            string garageId,
            string city,
            string state)
        {
            return AddIncome(
                amount,
                "Garage Income",
                $"Garage income: {city}, {state}",
                "Garage Ownership",
                garageId: garageId);
        }

        private static decimal EstimateLoadPayout(object job)
        {
            var miles = ReadDouble(job, "ActualDrivenMiles") ?? ReadDouble(job, "Miles") ?? 0;
            if (miles <= 0)
                return 1500m;

            return Math.Round((decimal)miles * 4.25m, 2);
        }

        private static string Read(object obj, string propertyName)
        {
            try
            {
                var prop = obj.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                return prop?.GetValue(obj)?.ToString()?.Trim() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static decimal? ReadDecimal(object obj, params string[] names)
        {
            foreach (var name in names)
            {
                var raw = Read(obj, name)
                    .Replace("$", "")
                    .Replace(",", "")
                    .Trim();

                if (decimal.TryParse(raw, out var value))
                    return value;
            }

            return null;
        }

        private static double? ReadDouble(object obj, params string[] names)
        {
            foreach (var name in names)
            {
                var raw = Read(obj, name)
                    .Replace(",", "")
                    .Trim();

                if (double.TryParse(raw, out var value))
                    return value;
            }

            return null;
        }

        private static string FirstNonEmpty(params string[] values)
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
