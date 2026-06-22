namespace OverWatchELD.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;
public sealed class VtcGarage : INotifyPropertyChanged
{
    public string Id { get; set; } = "";
    public string CityToken { get; set; } = "";
    public string CityName { get; set; } = "";
    public string State { get; set; } = "";
    public static int CapacityForSize(string? size)
    {
        return (size ?? "").Trim().ToLowerInvariant() switch
        {
            "large" => 7,
            "medium" => 5,
            "med" => 5,
            _ => 3
        };
    }

    public bool IsHomeGarage { get; set; }

    public decimal PurchasePrice { get; set; }
    public decimal DailyUpkeep { get; set; }
    public int RepairBays { get; set; }
    public bool HasFuelStation { get; set; }
    public decimal GarageIncomeBonusPercent { get; set; }

    public string EconomyDisplay =>
        IsOwned
            ? $"Upkeep ${DailyUpkeep:N0}/day • Bonus +{GarageIncomeBonusPercent:N0}%"
            : $"Buy ${PurchasePrice:N0}";

    public void ApplyEconomyDefaults()
    {
        TruckCapacity = CapacityForSize(Size);

        switch ((Size ?? "Small").Trim().ToLowerInvariant())
        {
            case "large":
                PurchasePrice = 1000000;
                DailyUpkeep = 2500;
                RepairBays = 3;
                GarageIncomeBonusPercent = 5;
                break;

            case "medium":
            case "med":
                PurchasePrice = 450000;
                DailyUpkeep = 1200;
                RepairBays = 2;
                GarageIncomeBonusPercent = 3;
                break;

            default:
                PurchasePrice = 180000;
                DailyUpkeep = 500;
                RepairBays = 1;
                GarageIncomeBonusPercent = 1;
                break;
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;

    public void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    public string Size { get; set; } = "Small"; // Small, Medium, Large

    public int TruckCapacity { get; set; } = 3;

    public bool IsOwned { get; set; }
    public double? MapX { get; set; }
    public double? MapY { get; set; }

    public List<string> AssignedTruckNumbers { get; set; } = new();

    public string SlotDisplay =>
        $"{AssignedTruckNumbers.Count}/{TruckCapacity}";
}