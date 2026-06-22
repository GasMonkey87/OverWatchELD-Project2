using OverWatchELD.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace OverWatchELD.Services.ATS
{
    public sealed class AtsCleanLoadExportResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string SavePath { get; set; } = "";
        public string BackupPath { get; set; } = "";
        public string InjectedUnitId { get; set; } = "";
        public bool VerificationSuccess { get; set; }
        public string VerificationMessage { get; set; } = "";
        public DispatchJob? Job { get; set; }
        public List<string> Warnings { get; set; } = new();
    }

    /// <summary>
    /// Bridge from the Create Load screen to the ATS save injector.
    ///
    /// IMPORTANT:
    /// This version does NOT remap selected mod trailers/cargo to generic ATS fallback names.
    /// It preserves the exact selected cargo/trailer token when available, then falls back to
    /// the selected display name only if a token is missing.
    ///
    /// That keeps exports aligned with the active mod/profile content selected by the user.
    /// </summary>
    public sealed class AtsCleanLoadExportBridgeService
    {
        public AtsCleanLoadExportResult ExportLoad(
            string? loadNumber,
            AtsCargoOption? selectedCargo,
            AtsTrailerOption? selectedTrailer,
            AtsCompanyOption? pickupCompany,
            AtsCompanyOption? destinationCompany,
            int miles,
            int weightLbs,
            string? assignedDriver,
            string? assignedTruck)
        {
            var result = new AtsCleanLoadExportResult();

            try
            {
                var exact = ResolveExactAtsNames(selectedCargo, selectedTrailer);

                if (string.IsNullOrWhiteSpace(exact.CargoToken))
                {
                    result.Success = false;
                    result.Message = "Selected cargo is missing an ATS token. Refresh active mods/profile and select cargo again.";
                    result.Warnings.Add("Cargo export stopped because the selected cargo did not include a usable ATS token.");
                    return result;
                }

                if (string.IsNullOrWhiteSpace(exact.TrailerToken))
                {
                    result.Success = false;
                    result.Message = "Selected trailer is missing an ATS token. Refresh active mods/profile and select trailer again.";
                    result.Warnings.Add("Trailer export stopped because the selected trailer did not include a usable ATS token.");
                    return result;
                }

                var finalMiles = miles > 0 ? miles : 500;
                var finalWeight = weightLbs > 0
                    ? weightLbs
                    : selectedCargo?.WeightLbs > 0
                        ? selectedCargo.WeightLbs
                        : 42000;

                var job = new DispatchJob
                {
                    Id = Guid.NewGuid().ToString("N"),
                    LoadNumber = string.IsNullOrWhiteSpace(loadNumber)
                        ? DispatchService.NextLoadNumber()
                        : loadNumber.Trim(),

                    Company = CleanCompanyName(pickupCompany?.Name, "Wallbert"),
                    OriginCity = CleanCity(pickupCompany?.City, "Phoenix"),
                    OriginState = CleanState(pickupCompany?.State, "AZ"),
                    DestinationCity = CleanCity(destinationCompany?.City, "Dallas"),
                    DestinationState = CleanState(destinationCompany?.State, "TX"),

                    Miles = finalMiles,

                    // Preserve selected mod/content tokens instead of generic fallback trailer types.
                    Cargo = exact.CargoToken,
                    Trailer = exact.TrailerToken,

                    AssignedDriver = string.IsNullOrWhiteSpace(assignedDriver) ? "Unassigned" : assignedDriver.Trim(),
                    AssignedTruck = string.IsNullOrWhiteSpace(assignedTruck) ? "Any" : assignedTruck.Trim(),
                    Status = "Sent To Game",
                    CreatedUtc = DateTime.UtcNow,
                    UpdatedUtc = DateTime.UtcNow,
                    PostedUtc = DateTime.UtcNow,
                    PickupDate = DateTime.Now.AddMinutes(15),
                    DeliveryDeadline = DateTime.Now.AddHours(Math.Max(6, finalMiles / 45.0 + 8.0)),
                    ActualCargoWeightLbs = finalWeight,
                    CargoWeight = finalWeight,
                    Payout = CalculatePayout(finalMiles, finalWeight),
                    RevenueUsd = CalculatePayout(finalMiles, finalWeight),
                    Notes = BuildNotes(selectedCargo, selectedTrailer, exact)
                };

                result.Job = job;

                DispatchService.AddJob(job);
                AtsJobExportService.ExportPendingJob(job);
                WriteReadableExportCopy(job, selectedCargo, selectedTrailer, pickupCompany, destinationCompany, exact);

                var inject = AtsSaveJobInjectionService.InjectJobIntoLatestSave(job);

                result.SavePath = inject.SavePath;
                result.BackupPath = inject.BackupPath;
                result.InjectedUnitId = inject.InjectedUnitId;
                result.Warnings.AddRange(inject.Warnings);

                if (!inject.Success)
                {
                    result.Success = false;
                    result.Message = inject.Message;
                    result.VerificationSuccess = false;
                    result.VerificationMessage = "Verification skipped because ATS save injection failed.";
                    result.Warnings.Add("Export used exact selected cargo/trailer tokens. If ATS rejects the load, confirm that both cargo and trailer are active in the selected ATS profile.");
                    result.Warnings.Add("Most common save issue: ATS save is compressed/not editable text. In ATS config.cfg set uset g_save_format \"2\", then make a new manual save and export again.");
                    return result;
                }

                var verification = AtsJobExportService.VerifyJobInSavePath(job, inject.SavePath, inject.InjectedUnitId);
                result.VerificationSuccess = verification.Success;
                result.VerificationMessage = verification.Message;

                if (verification.Success)
                {
                    result.Success = true;
                    result.Message = "Export verified. Exact selected cargo/trailer were written to game.sii. Reload the ATS save for it to appear in the load board.";
                    result.Warnings.Add(verification.Message);
                }
                else
                {
                    result.Success = false;
                    result.Message = verification.Message;
                    result.Warnings.Add("ATS save injection returned success, but verification did not find the OverWatch load marker after re-reading game.sii.");
                    result.Warnings.Add("Selected exact cargo token: " + exact.CargoToken);
                    result.Warnings.Add("Selected exact trailer token: " + exact.TrailerToken);
                    result.Warnings.AddRange(verification.MissingMarkers);
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

        private static ExactAtsSelection ResolveExactAtsNames(
            AtsCargoOption? cargo,
            AtsTrailerOption? trailer)
        {
            var cargoToken = FirstNonBlank(cargo?.Token, cargo?.Name);
            var trailerToken = FirstNonBlank(trailer?.Token, trailer?.Name);

            return new ExactAtsSelection
            {
                CargoToken = CleanDefToken(cargoToken),
                TrailerToken = CleanDefToken(trailerToken),
                CargoDisplayName = CleanDisplayName(cargo?.Name),
                TrailerDisplayName = CleanDisplayName(trailer?.Name),
                CargoSourceMod = CleanSourceMod(cargo?.SourceMod),
                TrailerSourceMod = CleanSourceMod(trailer?.SourceMod),
                WeightLbs = cargo?.WeightLbs > 0 ? cargo.WeightLbs : 42000
            };
        }

        private static string CleanDefToken(string? value)
        {
            value = (value ?? "").Trim();

            if (string.IsNullOrWhiteSpace(value))
                return "";

            // Remove only UI-only wrappers. Do not convert to generic trailer/cargo names.
            value = value
                .Replace("🟢", "")
                .Replace("🟡", "")
                .Replace("🔴", "")
                .Replace("⚪", "")
                .Replace("@@", "")
                .Trim();

            if (value.StartsWith("[", StringComparison.Ordinal))
            {
                var close = value.IndexOf(']');
                if (close >= 0 && close + 1 < value.Length)
                    value = value.Substring(close + 1).Trim();
            }

            if (value.StartsWith("•", StringComparison.Ordinal))
                value = value.Substring(1).Trim();

            return value.Trim();
        }

        private static string CleanDisplayName(string? value)
        {
            value = (value ?? "").Trim();

            if (string.IsNullOrWhiteSpace(value))
                return "";

            value = value
                .Replace("🟢", "")
                .Replace("🟡", "")
                .Replace("🔴", "")
                .Replace("⚪", "")
                .Replace("Installed / Not Verified -", "", StringComparison.OrdinalIgnoreCase)
                .Replace("Installed Not Verified -", "", StringComparison.OrdinalIgnoreCase)
                .Replace("Verified -", "", StringComparison.OrdinalIgnoreCase)
                .Replace("Active -", "", StringComparison.OrdinalIgnoreCase)
                .Replace("@@", "")
                .Trim();

            return value;
        }

        private static string CleanSourceMod(string? value)
        {
            value = CleanDisplayName(value);

            if (string.IsNullOrWhiteSpace(value))
                return "";

            if (value.StartsWith("[", StringComparison.Ordinal) && value.Contains("]"))
                return value;

            return value.Trim();
        }

        private static string CleanCompanyName(string? value, string fallback)
        {
            value = CleanTokenText(value);
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            return value;
        }

        private static string CleanCity(string? value, string fallback)
        {
            value = CleanTokenText(value);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static string CleanState(string? value, string fallback)
        {
            value = CleanTokenText(value);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static string CleanTokenText(string? value)
        {
            return (value ?? "")
                .Replace("@@", "")
                .Replace("_", " ")
                .Replace(".", " ")
                .Trim();
        }

        private static decimal CalculatePayout(int miles, int weight)
        {
            var rate = weight >= 50000 ? 4.25m : 3.35m;
            return Math.Max(500, Math.Round((miles * rate) + 250, 0));
        }

        private static string BuildNotes(
            AtsCargoOption? cargo,
            AtsTrailerOption? trailer,
            ExactAtsSelection exact)
        {
            return "Created from OverWatch ELD Clean Load Creator. " +
                   "Exact active-profile cargo/trailer selected. " +
                   $"Cargo token: {exact.CargoToken}. " +
                   $"Cargo display: {exact.CargoDisplayName}. " +
                   $"Cargo source: {exact.CargoSourceMod}. " +
                   $"Trailer token: {exact.TrailerToken}. " +
                   $"Trailer display: {exact.TrailerDisplayName}. " +
                   $"Trailer source: {exact.TrailerSourceMod}.";
        }

        private static void WriteReadableExportCopy(
            DispatchJob job,
            AtsCargoOption? selectedCargo,
            AtsTrailerOption? selectedTrailer,
            AtsCompanyOption? pickupCompany,
            AtsCompanyOption? destinationCompany,
            ExactAtsSelection exact)
        {
            try
            {
                var root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "OverWatchELD",
                    "AtsJobExports");

                Directory.CreateDirectory(root);

                var path = Path.Combine(root, $"{SafeFile(job.LoadNumber)}_clean_export.json");
                var payload = new
                {
                    job,
                    selectedCargo,
                    selectedTrailer,
                    pickupCompany,
                    destinationCompany,
                    exactCargoToken = exact.CargoToken,
                    exactTrailerToken = exact.TrailerToken,
                    exactCargoDisplayName = exact.CargoDisplayName,
                    exactTrailerDisplayName = exact.TrailerDisplayName,
                    exactCargoSourceMod = exact.CargoSourceMod,
                    exactTrailerSourceMod = exact.TrailerSourceMod,
                    exportedUtc = DateTime.UtcNow
                };

                File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // non-fatal
            }
        }

        private static string SafeFile(string? value)
        {
            var s = string.IsNullOrWhiteSpace(value) ? "load" : value;
            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s;
        }

        private static string FirstNonBlank(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }

        private sealed class ExactAtsSelection
        {
            public string CargoToken { get; set; } = "";
            public string TrailerToken { get; set; } = "";
            public string CargoDisplayName { get; set; } = "";
            public string TrailerDisplayName { get; set; } = "";
            public string CargoSourceMod { get; set; } = "";
            public string TrailerSourceMod { get; set; } = "";
            public int WeightLbs { get; set; } = 42000;
        }
    }
}
