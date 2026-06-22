using OverWatchELD.Models.Dispatch;
using OverWatchELD.Models.Economy;
using OverWatchELD.Services.Economy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OverWatchELD.Services.Dispatch
{
    public static class DispatchContractService
    {
        public static List<DispatchContract> LoadContracts()
        {
            AutoFailOverdueContracts();
            return DispatchContractStore.LoadContracts();
        }

        public static DispatchContract CreateContract(DispatchContract contract)
        {
            var rows = DispatchContractStore.LoadContracts();

            if (string.IsNullOrWhiteSpace(contract.Id))
                contract.Id = Guid.NewGuid().ToString("N");

            if (string.IsNullOrWhiteSpace(contract.ContractNumber))
                contract.ContractNumber = DispatchContractStore.NextContractNumber();

            if (contract.CreatedUtc == default)
                contract.CreatedUtc = DateTime.UtcNow;

            if (contract.StartUtc == default)
                contract.StartUtc = DateTime.UtcNow;

            if (contract.DueUtc == default)
                contract.DueUtc = DateTime.UtcNow.AddDays(7);

            contract.Status = string.IsNullOrWhiteSpace(contract.Status)
                ? "Active"
                : contract.Status;

            rows.Add(contract);
            DispatchContractStore.SaveContracts(rows);

            AddEvent(contract, "Created", $"Contract created for {contract.CustomerName}.");

            return contract;
        }

        public static void SaveContract(DispatchContract contract)
        {
            var rows = DispatchContractStore.LoadContracts();
            rows.RemoveAll(x => string.Equals(x.Id, contract.Id, StringComparison.OrdinalIgnoreCase));
            rows.Add(contract);
            DispatchContractStore.SaveContracts(rows);
        }

        public static void CancelContract(string id)
        {
            var rows = DispatchContractStore.LoadContracts();
            var contract = rows.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

            if (contract == null)
                return;

            contract.Status = "Cancelled";
            SaveContract(contract);
            AddEvent(contract, "Cancelled", "Contract cancelled.");
        }

        public static bool RecordLoadDeliveredForContract(string contractId, object? job = null)
        {
            var rows = DispatchContractStore.LoadContracts();
            var contract = rows.FirstOrDefault(x => string.Equals(x.Id, contractId, StringComparison.OrdinalIgnoreCase));

            if (contract == null)
                return false;

            if (!contract.Status.Equals("Active", StringComparison.OrdinalIgnoreCase))
                return false;

            var loadNumber = FirstNonEmpty(Read(job, "LoadNumber"), Read(job, "Id"));
            var miles = ReadDouble(job, "ActualDrivenMiles") ?? ReadDouble(job, "Miles") ?? contract.EstimatedMilesPerLoad;
            var revenue = Math.Round((decimal)miles * contract.RatePerMile, 2);

            contract.CompletedLoads++;

            EconomyStore.AddTransaction(new EconomyTransaction
            {
                Type = "ContractLoadRevenue",
                Category = "Dispatch Contract",
                Source = "Dispatch Contracts",
                Amount = revenue,
                LoadNumber = loadNumber,
                DriverName = FirstNonEmpty(Read(job, "AssignedDriver"), contract.AssignedDriver),
                TruckNumber = FirstNonEmpty(Read(job, "TruckNumber"), contract.AssignedTruckNumber),
                TruckName = FirstNonEmpty(Read(job, "AssignedTruck"), Read(job, "TruckName"), contract.AssignedTruckName),
                Description = $"Contract load revenue: {contract.ContractNumber}",
                Notes = $"{contract.CustomerName} • {contract.OriginCity}, {contract.OriginState} → {contract.DestinationCity}, {contract.DestinationState}"
            });

            AddEvent(contract, "LoadDelivered", $"Contract load delivered. Progress {contract.CompletedLoads}/{contract.RequiredLoads}.", loadNumber, revenue);

            if (contract.CompletedLoads >= contract.RequiredLoads)
                CompleteContract(contract);

            SaveContract(contract);
            return true;
        }

        public static void CompleteContract(DispatchContract contract)
        {
            contract.Status = "Completed";
            contract.CompletedUtc = DateTime.UtcNow;

            if (contract.BonusAmount > 0)
            {
                EconomyStore.AddTransaction(new EconomyTransaction
                {
                    Type = "ContractCompletionBonus",
                    Category = "Dispatch Contract",
                    Source = "Dispatch Contracts",
                    Amount = contract.BonusAmount,
                    DriverName = contract.AssignedDriver,
                    TruckNumber = contract.AssignedTruckNumber,
                    TruckName = contract.AssignedTruckName,
                    Description = $"Contract completion bonus: {contract.ContractNumber}",
                    Notes = contract.CustomerName
                });
            }

            AddEvent(contract, "Completed", $"Contract completed. Bonus posted: {contract.BonusAmount:C0}.", amount: contract.BonusAmount);
            SaveContract(contract);
        }

        public static void AutoFailOverdueContracts()
        {
            var rows = DispatchContractStore.LoadContracts();
            var changed = false;

            foreach (var contract in rows)
            {
                if (!contract.Status.Equals("Active", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (DateTime.UtcNow <= contract.DueUtc)
                    continue;

                contract.Status = "Failed";
                changed = true;

                if (contract.PenaltyAmount > 0)
                {
                    EconomyStore.AddTransaction(new EconomyTransaction
                    {
                        Type = "ContractFailurePenalty",
                        Category = "Dispatch Contract",
                        Source = "Dispatch Contracts",
                        Amount = -Math.Abs(contract.PenaltyAmount),
                        DriverName = contract.AssignedDriver,
                        TruckNumber = contract.AssignedTruckNumber,
                        TruckName = contract.AssignedTruckName,
                        Description = $"Contract failure penalty: {contract.ContractNumber}",
                        Notes = contract.CustomerName
                    });
                }

                AddEvent(contract, "Failed", $"Contract failed/overdue. Penalty posted: {contract.PenaltyAmount:C0}.", amount: -Math.Abs(contract.PenaltyAmount));
            }

            if (changed)
                DispatchContractStore.SaveContracts(rows);
        }

        public static List<DispatchContract> SeedSampleContracts()
        {
            var existing = DispatchContractStore.LoadContracts();

            if (existing.Count > 0)
                return existing;

            var samples = new List<DispatchContract>
            {
                new()
                {
                    ContractNumber = DispatchContractStore.NextContractNumber(),
                    CustomerName = "Wallbert Regional",
                    ContractType = "Dedicated Lane",
                    OriginCity = "Chicago",
                    OriginState = "IL",
                    DestinationCity = "St Louis",
                    DestinationState = "MO",
                    Cargo = "Retail Goods",
                    TrailerType = "Dry Van",
                    RequiredLoads = 5,
                    EstimatedMilesPerLoad = 300,
                    RatePerMile = 4.45m,
                    BonusAmount = 1500m,
                    PenaltyAmount = 800m,
                    DueUtc = DateTime.UtcNow.AddDays(7),
                    Status = "Active"
                },
                new()
                {
                    ContractNumber = "CT-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-002",
                    CustomerName = "Midwest Agriculture Co.",
                    ContractType = "Recurring Freight",
                    OriginCity = "Peoria",
                    OriginState = "IL",
                    DestinationCity = "Omaha",
                    DestinationState = "NE",
                    Cargo = "Farm Equipment",
                    TrailerType = "Flatbed",
                    RequiredLoads = 3,
                    EstimatedMilesPerLoad = 420,
                    RatePerMile = 4.90m,
                    BonusAmount = 1200m,
                    PenaltyAmount = 650m,
                    DueUtc = DateTime.UtcNow.AddDays(5),
                    Status = "Active"
                }
            };

            DispatchContractStore.SaveContracts(samples);

            foreach (var contract in samples)
                AddEvent(contract, "Created", "Sample contract seeded.");

            return samples;
        }

        public static List<DispatchContractEvent> LoadEvents()
        {
            return DispatchContractStore.LoadEvents();
        }

        public static void AddContractEvent(
            DispatchContract contract,
            string type,
            string message,
            string loadNumber = "",
            decimal amount = 0m)
        {
            AddEvent(contract, type, message, loadNumber, amount);
        }

        private static void AddEvent(
            DispatchContract contract,
            string type,
            string message,
            string loadNumber = "",
            decimal amount = 0m)
        {
            DispatchContractStore.AddEvent(new DispatchContractEvent
            {
                ContractId = contract.Id,
                ContractNumber = contract.ContractNumber,
                EventType = type,
                Message = message,
                LoadNumber = loadNumber,
                Amount = amount,
                CreatedUtc = DateTime.UtcNow
            });
        }

        private static string Read(object? obj, string propertyName)
        {
            if (obj == null)
                return "";

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

        private static double? ReadDouble(object? obj, params string[] names)
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
