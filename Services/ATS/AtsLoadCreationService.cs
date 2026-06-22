using System;
using System.IO;
using System.Text.Json;

namespace OverWatchELD.Services.ATS
{
    /// <summary>
    /// Creates ELD load records from ATS mod scan candidates.
    /// This does not alter dashboard/clocks/Discord bridge logic.
    /// </summary>
    public sealed class AtsLoadCreationService
    {
        private readonly AtsUserModScannerService _scanner;

        public AtsLoadCreationService(AtsUserModScannerService scanner)
        {
            _scanner = scanner;
        }

        public IReadOnlyList<AtsModScanLoadCandidate> GetAvailableUserModLoads()
        {
            return _scanner.ScanUserMods();
        }

        public AtsCreatedLoad CreateFromCandidate(AtsModScanLoadCandidate candidate, string? pickupCity, string? deliveryCity)
        {
            var loadNumber = "OW-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");

            return new AtsCreatedLoad
            {
                LoadNumber = loadNumber,
                CargoName = candidate.CargoName,
                TrailerName = candidate.TrailerName,
                SourceModName = candidate.SourceModName,
                WeightLbs = candidate.WeightLbs <= 0 ? 42000 : candidate.WeightLbs,
                PickupCity = string.IsNullOrWhiteSpace(pickupCity) ? candidate.PickupCity : pickupCity!,
                DeliveryCity = string.IsNullOrWhiteSpace(deliveryCity) ? candidate.DeliveryCity : deliveryCity!,
                CompanyFrom = candidate.CompanyFrom,
                CompanyTo = candidate.CompanyTo,
                AtsTokens =
                {
                    ["cargo"] = candidate.CargoToken,
                    ["trailer"] = candidate.TrailerToken,
                    ["sourceMod"] = candidate.SourceModName
                }
            };
        }

        public string SaveCreatedLoadJson(AtsCreatedLoad load)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var folder = Path.Combine(appData, "OverWatchELD", "ats-created-loads");
            Directory.CreateDirectory(folder);

            var path = Path.Combine(folder, $"{Sanitize(load.LoadNumber)}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(load, new JsonSerializerOptions { WriteIndented = true }));
            return path;
        }

        private static string Sanitize(string value)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');
            return value;
        }
    }
}
