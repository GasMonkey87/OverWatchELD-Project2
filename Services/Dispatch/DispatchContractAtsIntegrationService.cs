using OverWatchELD.Models.Dispatch;
using OverWatchELD.Services.LoadBoard;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services.Dispatch
{
    public static class DispatchContractAtsIntegrationService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public static DispatchContractAtsExportResult ExportNextContractLoadToAts(string contractId)
        {
            var result = new DispatchContractAtsExportResult();

            try
            {
                var contracts = DispatchContractStore.LoadContracts();
                var contract = contracts.FirstOrDefault(x =>
                    string.Equals(x.Id, contractId, StringComparison.OrdinalIgnoreCase));

                if (contract == null)
                {
                    result.Success = false;
                    result.Message = "Contract was not found.";
                    return result;
                }

                result.ContractId = contract.Id;
                result.ContractNumber = contract.ContractNumber;

                if (!contract.Status.Equals("Active", StringComparison.OrdinalIgnoreCase))
                {
                    result.Success = false;
                    result.Message = $"Contract is not active. Current status: {contract.Status}";
                    return result;
                }

                if (contract.CompletedLoads >= contract.RequiredLoads)
                {
                    result.Success = false;
                    result.Message = "Contract already has all required loads completed.";
                    return result;
                }

                var job = BuildDispatchJob(contract);
                result.LoadNumber = job.LoadNumber;
                result.DispatchJobId = job.Id;

                DispatchService.AddJob(job);
                DispatchService.SendToDriver(job);

                try
                {
                    LoadBoardStore.Upsert(ToLoadBoardLoad(job, contract));
                }
                catch (Exception ex)
                {
                    result.Warnings.Add("Load Board upsert failed: " + ex.Message);
                }

                try
                {
                    AtsJobExportService.ExportPendingJob(job);
                    WriteContractExportCopy(contract, job);
                }
                catch (Exception ex)
                {
                    result.Warnings.Add("ATS pending export JSON failed: " + ex.Message);
                }

                var inject = AtsSaveJobInjectionService.InjectJobIntoLatestSave(job);

                result.Success = inject.Success;
                result.Message = inject.Message;
                result.SavePath = inject.SavePath;
                result.BackupPath = inject.BackupPath;
                result.InjectedUnitId = inject.InjectedUnitId;
                result.Warnings.AddRange(inject.Warnings);

                if (inject.Success)
                {
                    job.Status = "Sent To Game";
                    job.UpdatedUtc = DateTime.UtcNow;
                    job.Notes = AppendLine(job.Notes, $"ATS injected from contract {contract.ContractNumber}. Unit: {inject.InjectedUnitId}");
                    DispatchService.UpdateJob(job);

                    DispatchContractService.AddContractEvent(
                        contract,
                        "AtsLoadExported",
                        $"ATS load exported/injected from contract. Load {job.LoadNumber}.",
                        job.LoadNumber);
                }
                else
                {
                    job.Status = "Export Pending";
                    job.UpdatedUtc = DateTime.UtcNow;
                    job.Notes = AppendLine(job.Notes, "ATS injection failed, but the load was saved to Dispatch and pending ATS export.");
                    DispatchService.UpdateJob(job);

                    DispatchContractService.AddContractEvent(
                        contract,
                        "AtsLoadExportFailed",
                        $"ATS injection failed for load {job.LoadNumber}: {inject.Message}",
                        job.LoadNumber);

                    result.Warnings.Add("Load was still created in Dispatch and saved to Documents\\OverWatchELD\\AtsJobExports.");
                    result.Warnings.Add("If ATS injection failed, verify ATS save format is editable text: uset g_save_format \"2\", then make a new manual save.");
                }

                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
                result.Warnings.Add(ex.ToString());
                return result;
            }
        }

        public static DispatchJob BuildDispatchJob(DispatchContract contract)
        {
            var loadSeq = contract.CompletedLoads + 1;
            var loadNumber = $"{contract.ContractNumber}-L{loadSeq:00}";

            var miles = contract.EstimatedMilesPerLoad > 0
                ? (int)Math.Round(contract.EstimatedMilesPerLoad)
                : 500;

            var payout = Math.Round((decimal)miles * contract.RatePerMile, 0);

            return new DispatchJob
            {
                Id = Guid.NewGuid().ToString("N"),
                LoadNumber = loadNumber,

                Company = CleanCompanyName(contract.CustomerName),
                OriginCity = CleanCity(contract.OriginCity, "Phoenix"),
                OriginState = CleanState(contract.OriginState, "AZ"),
                DestinationCity = CleanCity(contract.DestinationCity, "Dallas"),
                DestinationState = CleanState(contract.DestinationState, "TX"),

                Miles = miles,
                Cargo = CleanCargo(contract.Cargo),
                Trailer = CleanTrailer(contract.TrailerType),

                AssignedDriver = string.IsNullOrWhiteSpace(contract.AssignedDriver)
                    ? "Unassigned"
                    : contract.AssignedDriver.Trim(),

                AssignedTruck = FirstNonEmpty(contract.AssignedTruckNumber, contract.AssignedTruckName),

                Status = "Assigned",
                DispatchMode = string.IsNullOrWhiteSpace(contract.AssignedDriver) ? "Open" : "Assigned",
                ClaimedBy = string.IsNullOrWhiteSpace(contract.AssignedDriver) ? "" : contract.AssignedDriver.Trim(),
                ClaimedUtc = string.IsNullOrWhiteSpace(contract.AssignedDriver) ? null : DateTime.UtcNow,
                IsClaimLocked = !string.IsNullOrWhiteSpace(contract.AssignedDriver),

                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow,
                PostedUtc = DateTime.UtcNow,
                PickupDate = DateTime.Now.AddMinutes(15),
                DeliveryDeadline = DateTime.Now.AddHours(Math.Max(6, miles / 45.0 + 8.0)),

                Payout = payout,
                RevenueUsd = payout,
                CargoWeight = EstimateWeight(contract.Cargo, contract.TrailerType),
                ActualCargoWeightLbs = EstimateWeight(contract.Cargo, contract.TrailerType),

                Priority = contract.ContractType.Contains("Priority", StringComparison.OrdinalIgnoreCase) ? "High" : "Normal",
                TrailerOwner = "Company",
                AutoFleetSync = true,

                Notes =
                    $"Generated from Dispatch Contract {contract.ContractNumber}. " +
                    $"Customer: {contract.CustomerName}. " +
                    $"Contract load {loadSeq} of {contract.RequiredLoads}. " +
                    $"ContractId={contract.Id}"
            };
        }

        public static void SyncDeliveredContractLoads()
        {
            try
            {
                var contracts = DispatchContractStore.LoadContracts()
                    .Where(x => x.Status.Equals("Active", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var contract in contracts)
                {
                    var prefix = contract.ContractNumber + "-L";

                    var delivered = DispatchService.Jobs
                        .Where(x =>
                            x.Status.Equals("Delivered", StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(x.LoadNumber) &&
                            x.LoadNumber.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(x => x.DeliveredUtc ?? x.UpdatedUtc)
                        .ToList();

                    var targetCompleted = Math.Min(delivered.Count, contract.RequiredLoads);

                    while (contract.CompletedLoads < targetCompleted)
                    {
                        var job = delivered[contract.CompletedLoads];
                        DispatchContractService.RecordLoadDeliveredForContract(contract.Id, job);
                    }
                }
            }
            catch
            {
            }
        }

        private static LoadBoardLoad ToLoadBoardLoad(DispatchJob job, DispatchContract contract)
        {
            return new LoadBoardLoad
            {
                LoadNumber = job.LoadNumber,
                Status = "Available",
                DriverName = job.AssignedDriver,
                TruckNumber = contract.AssignedTruckNumber,
                TruckName = FirstNonEmpty(contract.AssignedTruckName, job.AssignedTruck),
                TrailerName = job.Trailer,
                Commodity = job.Cargo,
                WeightLbs = job.ActualCargoWeightLbs > 0 ? job.ActualCargoWeightLbs : job.CargoWeight,
                ShipperName = job.Company,
                ShipperCity = $"{job.OriginCity}, {job.OriginState}".Trim(' ', ','),
                ReceiverName = contract.CustomerName,
                ReceiverCity = $"{job.DestinationCity}, {job.DestinationState}".Trim(' ', ','),
                CurrentLocation = $"{job.OriginCity}, {job.OriginState}".Trim(' ', ','),
                CreatedUtc = DateTimeOffset.UtcNow,
                UpdatedUtc = DateTimeOffset.UtcNow
            };
        }

        private static void WriteContractExportCopy(DispatchContract contract, DispatchJob job)
        {
            try
            {
                var root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "OverWatchELD",
                    "AtsJobExports",
                    "Contracts");

                Directory.CreateDirectory(root);

                var path = Path.Combine(root, SafeFile(job.LoadNumber) + "_contract_ats_export.json");

                var payload = new
                {
                    contract,
                    job,
                    exportedUtc = DateTime.UtcNow
                };

                File.WriteAllText(path, JsonSerializer.Serialize(payload, JsonOptions));
            }
            catch
            {
            }
        }

        private static string CleanCompanyName(string? value)
        {
            var text = CleanText(value);
            if (string.IsNullOrWhiteSpace(text))
                return "Wallbert";

            // ATS mapper has better odds with known company names.
            var lower = text.ToLowerInvariant();

            if (lower.Contains("wallbert")) return "Wallbert";
            if (lower.Contains("sell")) return "SellGoods";
            if (lower.Contains("farm")) return "Bushnell Farms";
            if (lower.Contains("rail")) return "Rail Export";
            if (lower.Contains("namiq")) return "NAMIQ";
            if (lower.Contains("volt")) return "Voltison";
            if (lower.Contains("deep")) return "Deepgrove";

            return text;
        }

        private static string CleanCity(string? value, string fallback)
        {
            var text = CleanText(value);
            return string.IsNullOrWhiteSpace(text) ? fallback : text;
        }

        private static string CleanState(string? value, string fallback)
        {
            var text = CleanText(value);
            return string.IsNullOrWhiteSpace(text) ? fallback : text;
        }

        private static string CleanCargo(string? cargo)
        {
            var text = CleanText(cargo);
            if (string.IsNullOrWhiteSpace(text))
                return "General Goods";

            var lower = text.ToLowerInvariant();

            if (lower.Contains("farm") || lower.Contains("equipment")) return "Large Machinery";
            if (lower.Contains("retail") || lower.Contains("general")) return "General Goods";
            if (lower.Contains("food") || lower.Contains("refriger")) return "Refrigerated Goods";
            if (lower.Contains("fuel") || lower.Contains("diesel") || lower.Contains("gas")) return "Fuel";
            if (lower.Contains("vehicle") || lower.Contains("car")) return "Vehicles";
            if (lower.Contains("lumber") || lower.Contains("wood")) return "Lumber";

            return text;
        }

        private static string CleanTrailer(string? trailer)
        {
            var text = CleanText(trailer);
            if (string.IsNullOrWhiteSpace(text))
                return "Dry Van";

            var lower = text.ToLowerInvariant();

            if (lower.Contains("reefer")) return "Reefer";
            if (lower.Contains("flat")) return "Flatbed";
            if (lower.Contains("low")) return "Lowboy";
            if (lower.Contains("tank")) return "Tanker";
            if (lower.Contains("car")) return "Car Hauler";
            if (lower.Contains("dry")) return "Dry Van";

            return text;
        }

        private static int EstimateWeight(string? cargo, string? trailer)
        {
            var text = ((cargo ?? "") + " " + (trailer ?? "")).ToLowerInvariant();

            if (text.Contains("heavy") || text.Contains("machinery") || text.Contains("equipment") || text.Contains("lowboy"))
                return 68000;

            if (text.Contains("fuel") || text.Contains("tank"))
                return 52000;

            if (text.Contains("lumber") || text.Contains("flat"))
                return 32000;

            if (text.Contains("food") || text.Contains("reefer"))
                return 26000;

            if (text.Contains("vehicle") || text.Contains("car"))
                return 18000;

            return 24000;
        }

        private static string CleanText(string? value)
        {
            return (value ?? "")
                .Replace("@@", "")
                .Replace("_", " ")
                .Replace(".", " ")
                .Trim();
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

        private static string AppendLine(string? original, string line)
        {
            original = (original ?? "").Trim();

            if (string.IsNullOrWhiteSpace(original))
                return line;

            return original + Environment.NewLine + line;
        }

        private static string SafeFile(string? value)
        {
            var s = string.IsNullOrWhiteSpace(value) ? "contract-load" : value.Trim();

            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');

            return s;
        }
    }
}
