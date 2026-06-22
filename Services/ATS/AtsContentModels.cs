using System;
using System.Collections.ObjectModel;

namespace OverWatchELD.Services.ATS
{
    public sealed class AtsContentPack
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "All Installed Mods";
        public bool IsAll { get; set; }
    }

    public sealed class AtsCargoOption
    {
        public string Token { get; set; } = "";
        public string Name { get; set; } = "";
        public string SourceMod { get; set; } = "Vanilla ATS";
        public int WeightLbs { get; set; } = 42000;

        public string DisplayName =>
            string.IsNullOrWhiteSpace(SourceMod)
                ? Name
                : $"{Name} — {SourceMod}";
    }

    public sealed class AtsTrailerOption
    {
        public string Token { get; set; } = "";
        public string Name { get; set; } = "Auto Compatible Trailer";
        public string SourceMod { get; set; } = "Vanilla ATS";
        public string DisplayName =>
            string.IsNullOrWhiteSpace(SourceMod)
                ? Name
                : $"{Name} — {SourceMod}";
    }

    public sealed class AtsCompanyOption
    {
        public string Token { get; set; } = "";
        public string Name { get; set; } = "";
        public string City { get; set; } = "";
        public string State { get; set; } = "";
        public string SourceMod { get; set; } = "Vanilla ATS";

        public string DisplayName
        {
            get
            {
                var place = string.Join(" ", new[] { City, State }.Where(x => !string.IsNullOrWhiteSpace(x)));
                return string.IsNullOrWhiteSpace(place) ? Name : $"{Name} - {place}";
            }
        }
    }

    public sealed class AtsContentScanResult
    {
        public ObservableCollection<AtsContentPack> ContentPacks { get; } = new();
        public ObservableCollection<AtsCargoOption> Cargo { get; } = new();
        public ObservableCollection<AtsTrailerOption> Trailers { get; } = new();
        public ObservableCollection<AtsCompanyOption> Companies { get; } = new();
        public ObservableCollection<string> TechnicalLog { get; } = new();

        public int CargoCount => Cargo.Count;
        public int TrailerCount => Trailers.Count;
        public int CompanyCount => Companies.Count;
    }
}
