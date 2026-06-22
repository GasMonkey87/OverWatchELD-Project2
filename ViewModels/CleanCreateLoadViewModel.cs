using OverWatchELD.Services;
using OverWatchELD.Services.ATS;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace OverWatchELD.ViewModels
{
    public sealed class CleanCreateLoadViewModel : INotifyPropertyChanged
    {
        private readonly AtsCleanContentScannerService _scanner = new();
        private readonly AtsMileageCalculatorService _mileageCalculator = new();
        private bool _syncingCompatibility;

        public ObservableCollection<AtsCargoOption> CargoOptions { get; } = new();
        public ObservableCollection<AtsCargoOption> AllCargoOptions { get; } = new();
        public ObservableCollection<AtsTrailerOption> TrailerOptions { get; } = new();
        public ObservableCollection<AtsTrailerOption> AllTrailerOptions { get; } = new();
        public ObservableCollection<AtsLoadSourceOption> SourceOptions { get; } = new();
        public ObservableCollection<AtsCompanyOption> PickupCompanies { get; } = new();
        public ObservableCollection<AtsCompanyOption> DestinationCompanies { get; } = new();
        public ObservableCollection<DriverLoadOption> DriverOptions { get; } = new();
        public ObservableCollection<TruckLoadOption> TruckOptions { get; } = new();
        public ObservableCollection<AtsProfileSaveOption> AtsProfiles { get; } = new();
        public ObservableCollection<AtsProfileSaveOption> AtsSaves { get; } = new();
        public ObservableCollection<string> TechnicalLog { get; } = new();

        public ICollectionView PickupCompaniesView { get; }
        public ICollectionView DestinationCompaniesView { get; }


        private AtsLoadSourceOption? _selectedLoadSource;
        public AtsLoadSourceOption? SelectedLoadSource
        {
            get => _selectedLoadSource;
            set
            {
                if (Set(ref _selectedLoadSource, value) && !_syncingCompatibility)
                    ApplySelectedSourceFilter();
            }
        }

        private bool _isCompanyLoad;
        public bool IsCompanyLoad
        {
            get => _isCompanyLoad;
            set
            {
                if (Set(ref _isCompanyLoad, value))
                {
                    OnPropertyChanged(nameof(IsIndividualLoad));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool IsIndividualLoad
        {
            get => !IsCompanyLoad;
            set
            {
                if (value)
                    IsCompanyLoad = false;
            }
        }

        private AtsCargoOption? _selectedCargo;
        public AtsCargoOption? SelectedCargo
        {
            get => _selectedCargo;
            set
            {
                if (Set(ref _selectedCargo, value) && !_syncingCompatibility)
                    AutoFillFromCargo();
            }
        }

        private AtsTrailerOption? _selectedTrailer;
        public AtsTrailerOption? SelectedTrailer
        {
            get => _selectedTrailer;
            set
            {
                if (Set(ref _selectedTrailer, value) && !_syncingCompatibility)
                    AutoFillFromTrailer();
            }
        }

        private AtsCompanyOption? _selectedPickupCompany;
        public AtsCompanyOption? SelectedPickupCompany
        {
            get => _selectedPickupCompany;
            set { if (Set(ref _selectedPickupCompany, value)) AutoCalculateMileage(); }
        }

        private AtsCompanyOption? _selectedDestinationCompany;
        public AtsCompanyOption? SelectedDestinationCompany
        {
            get => _selectedDestinationCompany;
            set { if (Set(ref _selectedDestinationCompany, value)) AutoCalculateMileage(); }
        }

        private DriverLoadOption? _selectedDriver;
        public DriverLoadOption? SelectedDriver
        {
            get => _selectedDriver;
            set
            {
                if (Set(ref _selectedDriver, value))
                {
                    AssignedDriver = value?.Name ?? "Unassigned";
                    AssignedDriverDiscordId = value?.DiscordUserId ?? "";
                    AssignedDriverDiscordName = value?.DiscordName ?? "";
                    AutoSelectTruckForDriver(value);
                }
            }
        }

        private TruckLoadOption? _selectedTruck;
        public TruckLoadOption? SelectedTruck
        {
            get => _selectedTruck;
            set
            {
                if (Set(ref _selectedTruck, value))
                    AssignedTruck = value?.Name ?? "Any";
            }
        }
        private AtsProfileSaveOption? _selectedAtsProfile;
        public AtsProfileSaveOption? SelectedAtsProfile
        {
            get => _selectedAtsProfile;
            set
            {
                if (Set(ref _selectedAtsProfile, value))
                {
                    LoadSavesForSelectedProfile();
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        private AtsProfileSaveOption? _selectedAtsSave;
        public AtsProfileSaveOption? SelectedAtsSave
        {
            get => _selectedAtsSave;
            set
            {
                if (Set(ref _selectedAtsSave, value))
                {
                    SelectedAtsSavePath = value?.Path ?? "";
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        private string _selectedAtsSavePath = "";
        public string SelectedAtsSavePath { get => _selectedAtsSavePath; set => Set(ref _selectedAtsSavePath, value ?? ""); }


        private string _loadNumber = "LD-" + DateTime.Now.ToString("MMddHHmmss");
        public string LoadNumber { get => _loadNumber; set => Set(ref _loadNumber, value); }

        private int _weightLbs = 42000;
        public int WeightLbs { get => _weightLbs; set => Set(ref _weightLbs, value); }

        private int _miles = 500;
        public int Miles { get => _miles; set => Set(ref _miles, value); }

        private string _assignedDriver = "Unassigned";
        public string AssignedDriver { get => _assignedDriver; set => Set(ref _assignedDriver, value); }

        private string _assignedDriverDiscordId = "";
        public string AssignedDriverDiscordId { get => _assignedDriverDiscordId; set => Set(ref _assignedDriverDiscordId, value); }

        private string _assignedDriverDiscordName = "";
        public string AssignedDriverDiscordName { get => _assignedDriverDiscordName; set => Set(ref _assignedDriverDiscordName, value); }

        private string _assignedTruck = "Any";
        public string AssignedTruck { get => _assignedTruck; set => Set(ref _assignedTruck, value); }

        private string _contentSummary = "Loading ATS content...";
        public string ContentSummary { get => _contentSummary; set => Set(ref _contentSummary, value); }

        private string _lastExportStatus = "No ATS export has been verified yet.";
        public string LastExportStatus { get => _lastExportStatus; set => Set(ref _lastExportStatus, value); }

        private string _lastExportSavePath = "";
        public string LastExportSavePath { get => _lastExportSavePath; set { if (Set(ref _lastExportSavePath, value)) CommandManager.InvalidateRequerySuggested(); } }

        private string _lastExportSaveFolder = "";
        public string LastExportSaveFolder { get => _lastExportSaveFolder; set { if (Set(ref _lastExportSaveFolder, value)) CommandManager.InvalidateRequerySuggested(); } }

        private string _lastExportReloadSteps = "Export a load to ATS, then the reload steps will appear here.";
        public string LastExportReloadSteps { get => _lastExportReloadSteps; set { if (Set(ref _lastExportReloadSteps, value)) CommandManager.InvalidateRequerySuggested(); } }

        private bool _isBusy;
        public bool IsBusy { get => _isBusy; set => Set(ref _isBusy, value); }

        public ICommand RefreshModsCommand { get; }
        public ICommand RefreshAtsProfilesCommand { get; }
        public ICommand OpenSelectedAtsSaveFolderCommand { get; }
        public ICommand OpenModFolderCommand { get; }
        public ICommand CreateLoadCommand { get; }
        public ICommand ExportToAtsCommand { get; }
        public ICommand OpenLastExportSaveFolderCommand { get; }
        public ICommand CopyReloadStepsCommand { get; }

        public CleanCreateLoadViewModel()
        {
            PickupCompaniesView = CollectionViewSource.GetDefaultView(PickupCompanies);
            PickupCompaniesView.SortDescriptions.Add(new SortDescription(nameof(AtsCompanyOption.State), ListSortDirection.Ascending));
            PickupCompaniesView.SortDescriptions.Add(new SortDescription(nameof(AtsCompanyOption.City), ListSortDirection.Ascending));
            PickupCompaniesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(AtsCompanyOption.State)));

            DestinationCompaniesView = CollectionViewSource.GetDefaultView(DestinationCompanies);
            DestinationCompaniesView.SortDescriptions.Add(new SortDescription(nameof(AtsCompanyOption.State), ListSortDirection.Ascending));
            DestinationCompaniesView.SortDescriptions.Add(new SortDescription(nameof(AtsCompanyOption.City), ListSortDirection.Ascending));
            DestinationCompaniesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(AtsCompanyOption.State)));

            RefreshModsCommand = new RelayCommand(async _ => await RefreshAsync(), _ => !IsBusy);
            RefreshAtsProfilesCommand = new RelayCommand(_ => LoadAtsProfilesAndSaves(), _ => !IsBusy);
            OpenSelectedAtsSaveFolderCommand = new RelayCommand(_ => OpenSelectedAtsSaveFolder(), _ => Directory.Exists(SelectedAtsSave?.Path ?? ""));
            OpenModFolderCommand = new RelayCommand(_ => OpenAtsModFolder());
            CreateLoadCommand = new RelayCommand(_ => CreateLoad(), _ => CanCreateOrExport());
            ExportToAtsCommand = new RelayCommand(_ => ExportToAts(), _ => CanCreateOrExport());
            OpenLastExportSaveFolderCommand = new RelayCommand(_ => OpenLastExportSaveFolder(), _ => Directory.Exists(LastExportSaveFolder));
            CopyReloadStepsCommand = new RelayCommand(_ => CopyReloadSteps(), _ => !string.IsNullOrWhiteSpace(LastExportReloadSteps) && !LastExportReloadSteps.StartsWith("Export a load", StringComparison.OrdinalIgnoreCase));

            _ = RefreshAsync();
        }

        private bool CanCreateOrExport() => !IsBusy && SelectedCargo != null && SelectedTrailer != null && SelectedPickupCompany != null && SelectedDestinationCompany != null;

        private async Task RefreshAsync()
        {
            IsBusy = true;
            try
            {
                var scan = await _scanner.ScanAsync();

                CargoOptions.Clear(); AllCargoOptions.Clear(); TrailerOptions.Clear(); AllTrailerOptions.Clear(); SourceOptions.Clear(); PickupCompanies.Clear(); DestinationCompanies.Clear(); DriverOptions.Clear(); TruckOptions.Clear(); TechnicalLog.Clear();

                var cargoFromActiveMods = scan.Cargo
                    .Where(IsUsableCargo)
                    .GroupBy(x => $"{NormalizeSourceForCompare(x.SourceMod)}|{CleanAtsName(x.Name)}", StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .OrderBy(x => CleanAtsName(RemoveActiveInactivePrefix(x.SourceMod)))
                    .ThenBy(x => CleanAtsName(x.Name))
                    .ToList();

                foreach (var item in cargoFromActiveMods)
                {
                    var modName = CleanAtsName(RemoveActiveInactivePrefix(item.SourceMod));
                    var cargoName = CleanLoadItemDisplayName(item.Name);
                    item.Name = $"[{modName}] • {cargoName}";
                    AllCargoOptions.Add(item);
                }

                var trailersFromActiveMods = scan.Trailers
                    .Where(IsUsableTrailer)
                    .GroupBy(x => $"{NormalizeSourceForCompare(x.SourceMod)}|{CleanAtsName(x.Name)}", StringComparer.OrdinalIgnoreCase)
                    .Select(g =>
                    {
                        var item = g.First();
                        var modName = CleanAtsName(RemoveActiveInactivePrefix(item.SourceMod));
                        var trailerName = CleanTrailerDisplayName(item.Name);

                        if (string.IsNullOrWhiteSpace(trailerName))
                            return null;

                        item.Name = $"[{modName}] • {trailerName}";
                        return item;
                    })
                    .Where(x => x != null)
                    .OrderBy(x => CleanAtsName(RemoveActiveInactivePrefix(x!.SourceMod)))
                    .ThenBy(x => x!.Name)
                    .Cast<AtsTrailerOption>()
                    .ToList();

                foreach (var item in trailersFromActiveMods)
                    AllTrailerOptions.Add(item);

                EnsureFallbackLoadContent();
                BuildSourceOptions();
                SelectedLoadSource = SourceOptions.FirstOrDefault();
                ApplySelectedSourceFilter();

                foreach (var item in scan.Companies
                             .Where(x => !string.IsNullOrWhiteSpace(x.City) && !string.IsNullOrWhiteSpace(x.State))
                             .Where(x => AtsKnownCityService.IsKnownCity(x.City, x.State))
                             .GroupBy(x => $"{x.City}|{x.State}", StringComparer.OrdinalIgnoreCase)
                             .Select(g => g.First())
                             .OrderBy(x => x.State)
                             .ThenBy(x => x.City))
                {
                    PickupCompanies.Add(item);
                    DestinationCompanies.Add(item);
                }

                foreach (var line in scan.TechnicalLog)
                    TechnicalLog.Add(line);

                LoadRosterAndTruckOptions();
                LoadAtsProfilesAndSaves();
                PickupCompaniesView.Refresh(); DestinationCompaniesView.Refresh();

                SelectedCargo ??= CargoOptions.FirstOrDefault();
                SelectedPickupCompany = PickupCompanies.FirstOrDefault();
                SelectedDestinationCompany = DestinationCompanies.Skip(1).FirstOrDefault() ?? SelectedPickupCompany;
                SelectedDriver = DriverOptions.FirstOrDefault(x => x.Name != "Unassigned") ?? DriverOptions.FirstOrDefault();
                SelectedTruck = TruckOptions.FirstOrDefault();

                AutoCalculateMileage();
                TechnicalLog.Add($"Create Load ready: {CargoOptions.Count} cargo, {AllTrailerOptions.Count} trailers, {PickupCompanies.Count} ATS cities, {DriverOptions.Count} drivers.");
                ContentSummary = $"{CargoOptions.Count} cargos • {AllTrailerOptions.Count} trailers • {PickupCompanies.Count} ATS cities • {DriverOptions.Count} drivers ready";
            }
            catch (Exception ex)
            {
                ContentSummary = "ATS content scan failed — check technical log.";
                TechnicalLog.Add("ATS content scan failed: " + ex.Message);
            }
            finally
            {
                IsBusy = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void AutoFillFromCargo()
        {
            if (_syncingCompatibility)
                return;

            _syncingCompatibility = true;

            try
            {
                TrailerOptions.Clear();

                if (SelectedCargo == null)
                {
                    SelectedTrailer = null;
                    return;
                }

                WeightLbs = SelectedCargo.WeightLbs <= 0 ? 42000 : SelectedCargo.WeightLbs;

                var compatibleTrailers = GetTrailersForSelectedSource()
                    .Where(t => IsCargoTrailerCompatible(SelectedCargo, t))
                    .OrderBy(t => t.Name)
                    .ToList();

                if (compatibleTrailers.Count == 0)
                    TechnicalLog.Add($"No compatible trailer found for cargo '{SelectedCargo.Name}' in selected source. Select a different mod folder/source.");

                foreach (var trailer in compatibleTrailers)
                    TrailerOptions.Add(trailer);

                if (SelectedTrailer == null ||
                    !TrailerOptions.Any(t => SameToken(t.Token, SelectedTrailer.Token)))
                {
                    SelectedTrailer = TrailerOptions.FirstOrDefault();
                }

                AutoCalculateMileage();
            }
            finally
            {
                _syncingCompatibility = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void AutoFillFromTrailer()
        {
            if (_syncingCompatibility)
                return;

            _syncingCompatibility = true;

            try
            {
                if (SelectedTrailer == null)
                {
                    RebuildCargoOptions(GetCargoForSelectedSource());
                    return;
                }

                var compatibleCargo = GetCargoForSelectedSource()
                    .Where(c => IsCargoTrailerCompatible(c, SelectedTrailer))
                    .OrderBy(c => CleanAtsName(RemoveActiveInactivePrefix(c.SourceMod)))
                    .ThenBy(c => CleanAtsName(c.Name))
                    .ToList();

                if (compatibleCargo.Count == 0)
                    TechnicalLog.Add($"No compatible cargo found for trailer '{SelectedTrailer.Name}' in selected source. Select a different mod folder/source.");

                var previousCargoToken = SelectedCargo?.Token ?? "";
                RebuildCargoOptions(compatibleCargo);

                if (SelectedCargo == null ||
                    !CargoOptions.Any(c => SameToken(c.Token, previousCargoToken)))
                {
                    SelectedCargo ??= CargoOptions.FirstOrDefault();
                }

                if (SelectedCargo != null)
                    WeightLbs = SelectedCargo.WeightLbs <= 0 ? 42000 : SelectedCargo.WeightLbs;

                AutoCalculateMileage();
            }
            finally
            {
                _syncingCompatibility = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }


        private void BuildSourceOptions()
        {
            SourceOptions.Clear();

            var sources = AllCargoOptions.Select(x => x.SourceMod)
                .Concat(AllTrailerOptions.Select(x => x.SourceMod))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .GroupBy(x => NormalizeSourceForCompare(x), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(x => CleanAtsName(RemoveActiveInactivePrefix(x)))
                .ToList();

            foreach (var source in sources)
            {
                var clean = CleanAtsName(RemoveActiveInactivePrefix(source));
                var key = NormalizeSourceForCompare(source);

                SourceOptions.Add(new AtsLoadSourceOption
                {
                    SourceKey = key,
                    SourceMod = source,
                    DisplayName = $"[{clean}]"
                });
            }
        }

        private void ApplySelectedSourceFilter()
        {
            if (_syncingCompatibility)
                return;

            _syncingCompatibility = true;

            try
            {
                CargoOptions.Clear();
                TrailerOptions.Clear();

                foreach (var cargo in GetCargoForSelectedSource())
                    CargoOptions.Add(cargo);

                foreach (var trailer in GetTrailersForSelectedSource())
                    TrailerOptions.Add(trailer);

                SelectedCargo = CargoOptions.FirstOrDefault();
                SelectedTrailer = TrailerOptions.FirstOrDefault();

                if (SelectedCargo != null)
                    WeightLbs = SelectedCargo.WeightLbs <= 0 ? 42000 : SelectedCargo.WeightLbs;

                TechnicalLog.Add($"Selected source {(SelectedLoadSource?.DisplayName ?? "[Unknown]")}: {CargoOptions.Count} cargo, {TrailerOptions.Count} trailers.");
            }
            finally
            {
                _syncingCompatibility = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private List<AtsCargoOption> GetCargoForSelectedSource()
        {
            var sourceKey = SelectedLoadSource?.SourceKey ?? "";

            return AllCargoOptions
                .Where(x => string.IsNullOrWhiteSpace(sourceKey) || SameSource(x.SourceMod, sourceKey))
                .OrderBy(x => x.Name)
                .ToList();
        }

        private List<AtsTrailerOption> GetTrailersForSelectedSource()
        {
            var sourceKey = SelectedLoadSource?.SourceKey ?? "";

            return AllTrailerOptions
                .Where(x => string.IsNullOrWhiteSpace(sourceKey) || SameSource(x.SourceMod, sourceKey))
                .OrderBy(x => x.Name)
                .ToList();
        }

        private static bool SameSource(string? sourceMod, string sourceKey)
        {
            return string.Equals(NormalizeSourceForCompare(sourceMod), sourceKey, StringComparison.OrdinalIgnoreCase);
        }

        private void RebuildCargoOptions(System.Collections.Generic.IEnumerable<AtsCargoOption> cargo)
        {
            var selectedToken = SelectedCargo?.Token ?? "";

            CargoOptions.Clear();

            foreach (var item in cargo)
                CargoOptions.Add(item);

            SelectedCargo = CargoOptions.FirstOrDefault(c => SameToken(c.Token, selectedToken))
                            ?? CargoOptions.FirstOrDefault();
        }

        private static bool IsCargoTrailerCompatible(AtsCargoOption cargo, AtsTrailerOption trailer)
        {
            if (cargo == null || trailer == null)
                return false;

            var cargoSource = NormalizeSourceForCompare(cargo.SourceMod);
            var trailerSource = NormalizeSourceForCompare(trailer.SourceMod);

            if (!string.Equals(cargoSource, trailerSource, StringComparison.OrdinalIgnoreCase))
                return false;

            var cargoToken = NormalizeSourceForCompare(cargo.Token + " " + cargo.Name + " " + cargo.SourceMod);
            var trailerToken = NormalizeSourceForCompare(trailer.Token + " " + trailer.Name + " " + trailer.SourceMod);

            // Same mod/source is required. Token overlap improves exact matching when the mod exposes related cargo/trailer names.
            return TokenOverlap(cargoToken, trailerToken) ||
                   IsBaseOrFallbackSource(cargo.SourceMod) ||
                   IsBaseOrFallbackSource(trailer.SourceMod) ||
                   string.Equals(cargoSource, trailerSource, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TokenOverlap(string? a, string? b)
        {
            a ??= "";
            b ??= "";

            var aTokens = a.Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Where(x => x.Length >= 3)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var bTokens = b.Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Where(x => x.Length >= 3)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (aTokens.Count == 0 || bTokens.Count == 0)
                return false;

            var overlap = aTokens.Count(x =>
                bTokens.Contains(x, StringComparer.OrdinalIgnoreCase));

            return overlap >= 2 ||
                   (overlap >= 1 && (aTokens.Count == 1 || bTokens.Count == 1));
        }

        private static bool SameToken(string? a, string? b)
        {
            return string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
        }

        private void EnsureFallbackLoadContent()
        {
            if (CargoOptions.Count == 0)
            {
                CargoOptions.Add(new AtsCargoOption { Token = "general_goods", Name = "General Goods", SourceMod = "ATS Fallback", WeightLbs = 42000 });
                CargoOptions.Add(new AtsCargoOption { Token = "machinery", Name = "Machinery", SourceMod = "ATS Fallback", WeightLbs = 46000 });
                CargoOptions.Add(new AtsCargoOption { Token = "food_products", Name = "Food Products", SourceMod = "ATS Fallback", WeightLbs = 38000 });
                CargoOptions.Add(new AtsCargoOption { Token = "construction_materials", Name = "Construction Materials", SourceMod = "ATS Fallback", WeightLbs = 48000 });
                TechnicalLog.Add("No active mod cargo passed the filter, so fallback cargo was added to keep Create Load usable.");
            }

            if (AllTrailerOptions.Count == 0)
            {
                AllTrailerOptions.Add(new AtsTrailerOption { Token = "dryvan", Name = "ATS Fallback • Dry Van", SourceMod = "ATS Fallback" });
                AllTrailerOptions.Add(new AtsTrailerOption { Token = "reefer", Name = "ATS Fallback • Reefer", SourceMod = "ATS Fallback" });
                AllTrailerOptions.Add(new AtsTrailerOption { Token = "flatbed", Name = "ATS Fallback • Flatbed", SourceMod = "ATS Fallback" });
                AllTrailerOptions.Add(new AtsTrailerOption { Token = "lowboy", Name = "ATS Fallback • Lowboy", SourceMod = "ATS Fallback" });
                AllTrailerOptions.Add(new AtsTrailerOption { Token = "tanker", Name = "ATS Fallback • Tanker", SourceMod = "ATS Fallback" });
                TechnicalLog.Add("No active mod trailers passed the filter, so fallback trailers were added to keep Create Load usable.");
            }
        }

        private static bool IsUsableCargo(AtsCargoOption x)
        {
            if (x == null || string.IsNullOrWhiteSpace(x.Name) || string.IsNullOrWhiteSpace(x.SourceMod))
                return false;

            var source = x.SourceMod;
            var name = x.Name;

            if (LooksLikeNonLoadMod(source) || LooksLikeNonLoadMod(name))
                return false;

            return !LooksLikeNonLoadItem(name);
        }

        private static bool IsUsableTrailer(AtsTrailerOption x)
        {
            if (x == null || string.IsNullOrWhiteSpace(x.Name) || string.IsNullOrWhiteSpace(x.SourceMod))
                return false;

            var source = x.SourceMod;
            var name = x.Name;

            if (LooksLikeNonLoadMod(source) || LooksLikeNonLoadMod(name))
                return false;

            return !LooksLikeNonLoadItem(name);
        }


        private static string CleanLoadItemDisplayName(string? value)
        {
            value = CleanAtsName(RemoveActiveInactivePrefix(value));

            if (string.IsNullOrWhiteSpace(value))
                return "";

            if (value.StartsWith("[", StringComparison.Ordinal) && value.Contains("]"))
            {
                var close = value.IndexOf(']');
                if (close >= 0 && close + 1 < value.Length)
                    value = value.Substring(close + 1).Trim();
            }

            if (value.StartsWith("•", StringComparison.Ordinal))
                value = value.Substring(1).Trim();

            return value.Trim();
        }

        private static string CleanTrailerDisplayName(string? value)
        {
            value = CleanAtsName(value);

            if (string.IsNullOrWhiteSpace(value))
                return "";

            if (value.Contains("======", StringComparison.OrdinalIgnoreCase))
                return "";

            if (value.StartsWith("Trailer", StringComparison.OrdinalIgnoreCase) &&
                value.Skip("Trailer".Length).All(ch => char.IsDigit(ch) || char.IsWhiteSpace(ch)))
                return "";

            if (value.Equals("Sidekit", StringComparison.OrdinalIgnoreCase))
                return "";

            if (value.Equals("Phantom Scr", StringComparison.OrdinalIgnoreCase))
                return "";

            if (value.Length <= 2)
                return "";

            return value.Trim();
        }

        private static bool LooksLikeNonLoadMod(string? value)
        {
            value ??= "";

            return value.Contains("truck", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("interior", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("parts", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("tuning", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("sound", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("graphics", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("weather", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("traffic", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("ai traffic", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeNonLoadItem(string? value)
        {
            value ??= "";

            return value.Contains("paint", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("paint_job", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("paintjob", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("skin", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("accessory", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("hookup", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("mirror", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("wheel", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("rim", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("light", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("bumper", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("cab", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("chassis", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("engine", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("transmission", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsInactiveSource(string? source)
        {
            source ??= "";
            return source.Contains("Inactive", StringComparison.OrdinalIgnoreCase) || source.Contains("⚪", StringComparison.OrdinalIgnoreCase) || source.Contains("🔴", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBaseOrFallbackSource(string? source)
        {
            source ??= "";
            return source.Contains("Base", StringComparison.OrdinalIgnoreCase) || source.Contains("Fallback", StringComparison.OrdinalIgnoreCase) || source.Contains("Vanilla", StringComparison.OrdinalIgnoreCase) || source.Contains("Seed", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeSourceForCompare(string? value)
        {
            value = RemoveActiveInactivePrefix(value).ToLowerInvariant().Replace(".scs", "").Replace(".zip", "");
            value = new string(value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
            while (value.Contains("__", StringComparison.Ordinal)) value = value.Replace("__", "_");
            return value.Trim('_');
        }

        private static string RemoveActiveInactivePrefix(string? value)
        {
            value = (value ?? "").Trim()
                .Replace("🟢", "")
                .Replace("⚪", "")
                .Replace("🔴", "")
                .Trim();

            value = value.Replace("Installed Not Verified [scr]", "", StringComparison.OrdinalIgnoreCase).Trim();
            value = value.Replace("Installed Not Verified", "", StringComparison.OrdinalIgnoreCase).Trim();
            value = value.Replace("Active Steam Workshop", "Steam Workshop", StringComparison.OrdinalIgnoreCase).Trim();
            value = value.Replace("Inactive Steam Workshop", "Steam Workshop", StringComparison.OrdinalIgnoreCase).Trim();
            value = value.Replace("Active Local", "Local", StringComparison.OrdinalIgnoreCase).Trim();
            value = value.Replace("Inactive Local", "Local", StringComparison.OrdinalIgnoreCase).Trim();
            value = value.Replace("Active -", "", StringComparison.OrdinalIgnoreCase).Trim();
            value = value.Replace("Inactive -", "", StringComparison.OrdinalIgnoreCase).Trim();

            while (value.StartsWith("-", StringComparison.Ordinal) || value.StartsWith("•", StringComparison.Ordinal))
                value = value.Substring(1).Trim();

            return value;
        }

        private static string CleanAtsName(string? value)
        {
            value = RemoveActiveInactivePrefix(value).Replace("@@", "").Replace("_", " ").Replace(".", " ").Replace("-", " ").Replace("/", " ").Trim();
            while (value.Contains("  ", StringComparison.Ordinal)) value = value.Replace("  ", " ");
            if (string.IsNullOrWhiteSpace(value)) return "Unknown";
            return string.Join(" ", value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w.Length <= 1 ? w.ToUpperInvariant() : char.ToUpperInvariant(w[0]) + w.Substring(1).ToLowerInvariant()));
        }

        private void LoadRosterAndTruckOptions()
        {
            AddDriver("Unassigned", "Unassigned", "", "", "");
            AddTruck("Any");

            try
            {
                foreach (var profile in DriverProfileMasterStore.LoadAll())
                {
                    var name = FirstNonBlank(
                        profile.DisplayName,
                        profile.DiscordName,
                        profile.DiscordUserId);
                    AddDriver(name, name, profile.DiscordUserId, profile.DiscordName, profile.Role);

                    foreach (var truck in profile.ConnectedTrucks ?? new System.Collections.Generic.List<DriverTruckLink>())
                        AddTruck(FirstNonBlank(truck.TruckNumber, truck.TruckName, truck.Plate));
                }
            }
            catch (Exception ex) { TechnicalLog.Add("Driver profile scan skipped: " + ex.Message); }

            try
            {
                AddDriver(EldCurrentUserService.SafeDisplayName(), EldCurrentUserService.SafeDisplayName(), EldCurrentUserService.DiscordUserId, EldCurrentUserService.SafeDisplayName(), "");
            }
            catch { }

            try
            {
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var roots = new[] { Path.Combine(docs, "OverWatchELD"), Path.Combine(docs, "OverWatch ELD"), Path.Combine(appData, "OverWatchELD"), Path.Combine(appData, "OverWatch ELD"), AppDomain.CurrentDomain.BaseDirectory };
                var files = roots.Where(Directory.Exists).SelectMany(r => Directory.EnumerateFiles(r, "*.json", SearchOption.AllDirectories)).Where(f => { var n = Path.GetFileName(f).ToLowerInvariant(); return n.Contains("roster") || n.Contains("driver") || n.Contains("fleet") || n.Contains("truck") || n.Contains("profile"); }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                foreach (var file in files)
                {
                    var text = File.ReadAllText(file);
                    ExtractJsonLikeValues(text, new[] { "driverName", "displayName", "discordUsername", "discordName", "username", "name", "assignedDriver" }, AddDriver);
                    ExtractJsonLikeValues(text, new[] { "truckName", "assignedTruck", "unitNumber", "truckNumber", "vehicleName" }, AddTruck);
                }
            }
            catch (Exception ex) { TechnicalLog.Add("Roster JSON scan skipped: " + ex.Message); }

            CleanDriverTruckLists();
        }

        private static void ExtractJsonLikeValues(string text, string[] keys, Action<string> add)
        {
            foreach (var key in keys)
            {
                var pattern = "\"" + key + "\"";
                var index = 0;
                while ((index = text.IndexOf(pattern, index, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    var colon = text.IndexOf(':', index + pattern.Length); if (colon < 0) break;
                    var firstQuote = text.IndexOf('"', colon + 1); if (firstQuote < 0) { index = colon + 1; continue; }
                    var secondQuote = text.IndexOf('"', firstQuote + 1); if (secondQuote < 0) { index = firstQuote + 1; continue; }
                    var value = text.Substring(firstQuote + 1, secondQuote - firstQuote - 1).Trim();
                    if (!string.IsNullOrWhiteSpace(value) && !value.Equals("null", StringComparison.OrdinalIgnoreCase) && !value.Equals("Any", StringComparison.OrdinalIgnoreCase) && !value.Equals("Unassigned", StringComparison.OrdinalIgnoreCase) && !value.Contains("http", StringComparison.OrdinalIgnoreCase) && value.Length <= 80) add(value);
                    index = secondQuote + 1;
                }
            }
        }

        private void AddDriver(string name) => AddDriver(name, name, "", name, "");

        private void AddDriver(string name, string displayName, string discordUserId, string discordName, string role)
        {
            name = FirstNonBlank(name, displayName, discordName);

            if (IsMostlyNumeric(name) && !string.IsNullOrWhiteSpace(discordName))
                name = discordName.Trim();

            displayName = FirstNonBlank(
                displayName,
                name,
                discordName,
                "Unknown Driver");

            if (IsMostlyNumeric(displayName) && !string.IsNullOrWhiteSpace(discordName))
                displayName = discordName.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;
            if (DriverOptions.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrWhiteSpace(discordUserId) && string.Equals(x.DiscordUserId, discordUserId, StringComparison.OrdinalIgnoreCase)))) return;
            DriverOptions.Add(new DriverLoadOption { Name = name.Trim(), DisplayName = displayName.Trim(), DiscordUserId = discordUserId?.Trim() ?? "", DiscordName = discordName?.Trim() ?? "", Role = role?.Trim() ?? "" });
        }

        private void AddTruck(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            if (name.Equals("Any", StringComparison.OrdinalIgnoreCase) || name.Equals("Unassigned", StringComparison.OrdinalIgnoreCase)) return;
            if (!TruckOptions.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) TruckOptions.Add(new TruckLoadOption { Name = name.Trim(), DisplayName = name.Trim() });
        }

        private void CleanDriverTruckLists()
        {
            var drivers = DriverOptions.Where(x => !string.IsNullOrWhiteSpace(x.Name)).GroupBy(x => FirstNonBlank(x.DiscordUserId, x.Name), StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderBy(x => x.Name == "Unassigned" ? 0 : 1).ThenBy(x => x.DisplayName).ToList();
            DriverOptions.Clear(); foreach (var d in drivers) DriverOptions.Add(d);
            var trucks = TruckOptions.Where(x => !string.IsNullOrWhiteSpace(x.Name)).GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderBy(x => x.Name == "Any" ? 0 : 1).ThenBy(x => x.Name).ToList();
            TruckOptions.Clear(); TruckOptions.Add(new TruckLoadOption { Name = "Any", DisplayName = "Any" }); foreach (var t in trucks.Where(x => x.Name != "Any")) TruckOptions.Add(t);
        }

        private void AutoSelectTruckForDriver(DriverLoadOption? driver)
        {
            if (driver == null || driver.Name == "Unassigned") { SelectedTruck = TruckOptions.FirstOrDefault(); return; }
            var match = TruckOptions.FirstOrDefault(t => t.Name.Contains(driver.Name, StringComparison.OrdinalIgnoreCase) || driver.Name.Contains(t.Name, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrWhiteSpace(driver.DiscordName) && t.Name.Contains(driver.DiscordName, StringComparison.OrdinalIgnoreCase)));
            SelectedTruck = match ?? TruckOptions.FirstOrDefault();
        }

        private void AutoCalculateMileage()
        {
            if (SelectedPickupCompany == null || SelectedDestinationCompany == null) return;
            var calculated = _mileageCalculator.CalculateMiles(SelectedPickupCompany, SelectedDestinationCompany);
            if (calculated > 0) Miles = calculated;
        }

        private void CreateLoad()
        {
            if (!ValidateLoad("create")) return;

            if (IsCompanyLoad)
            {
                SaveCompanyLoadRequest();
                return;
            }

            TechnicalLog.Add($"Created individual load {LoadNumber}: {SelectedCargo?.Name} using {SelectedTrailer?.Name}, Driver={AssignedDriver}, Truck={AssignedTruck}, {Miles:n0} miles.");
            IndividualLoadHistoryStore.AddFromCreateLoad(
                LoadNumber,
                SelectedCargo?.Name ?? SelectedCargo?.Token ?? "",
                SelectedTrailer?.Name ?? SelectedTrailer?.Token ?? "",
                SelectedPickupCompany?.Name ?? "",
                SelectedPickupCompany?.City ?? "",
                SelectedPickupCompany?.State ?? "",
                SelectedDestinationCompany?.Name ?? "",
                SelectedDestinationCompany?.City ?? "",
                SelectedDestinationCompany?.State ?? "",
                Miles,
                WeightLbs,
                AssignedDriver,
                AssignedTruck,
                SelectedLoadSource?.DisplayName ?? "",
                "Created");
            ContentSummary = $"Individual load {LoadNumber} created for {AssignedDriver}.";
        }

        private bool ValidateLoad(string actionName)
        {
            if (SelectedCargo == null) { ContentSummary = $"Cannot {actionName}: select cargo."; TechnicalLog.Add(ContentSummary); return false; }
            if (SelectedTrailer == null) { ContentSummary = $"Cannot {actionName}: select trailer."; TechnicalLog.Add(ContentSummary); return false; }
            if (SelectedPickupCompany == null || SelectedDestinationCompany == null) { ContentSummary = $"Cannot {actionName}: select pickup and destination."; TechnicalLog.Add(ContentSummary); return false; }
            if (string.IsNullOrWhiteSpace(AssignedDriver)) AssignedDriver = "Unassigned";
            if (string.IsNullOrWhiteSpace(AssignedTruck)) AssignedTruck = "Any";
            return true;
        }

        private void ExportToAts()
        {
            try
            {
                if (!ValidateLoad("export")) return;
                var selectedProfile = SelectedAtsProfile?.DisplayName ?? "Auto-detect";
                var selectedSave = SelectedAtsSave?.DisplayName ?? "Auto-detect";

                TechnicalLog.Add($"Export to ATS requested: {LoadNumber}, Cargo={SelectedCargo?.Token}, Trailer={SelectedTrailer?.Token}, Driver={AssignedDriver}, Truck={AssignedTruck}, Miles={Miles}, Profile={selectedProfile}, Save={selectedSave}");

                var bridge = new AtsCleanLoadExportBridgeService();
                var result = bridge.ExportLoad(LoadNumber, SelectedCargo!, SelectedTrailer!, SelectedPickupCompany!, SelectedDestinationCompany!, Miles, WeightLbs, AssignedDriver, AssignedTruck);

                var selectedSaveApplied = TryApplyBridgeExportToSelectedSave(result);

                if (result.Success && result.VerificationSuccess) { ApplyReloadAssistant(result, true); TechnicalLog.Add("✅ Export Verified: " + result.Message); TechnicalLog.Add("Save: " + result.SavePath); if (selectedSaveApplied) TechnicalLog.Add("✅ Copied export into selected ATS save: " + SelectedAtsSavePath); TechnicalLog.Add("Injected unit: " + result.InjectedUnitId); ContentSummary = selectedSaveApplied ? "✅ Export Verified — applied to selected ATS profile/save." : "✅ Export Verified — use the ATS Reload Assistant below."; }
                else if (result.Success) { ApplyReloadAssistant(result, false); TechnicalLog.Add("⚠ Export completed, but verification warning: " + result.VerificationMessage); TechnicalLog.Add("Save: " + result.SavePath); if (selectedSaveApplied) TechnicalLog.Add("✅ Copied export into selected ATS save: " + SelectedAtsSavePath); ContentSummary = selectedSaveApplied ? "⚠ Export written to selected ATS save — reload that save and verify load board." : "⚠ ATS export completed — reload ATS save and verify load board."; }
                else { LastExportStatus = "❌ Export failed — reload assistant unavailable."; LastExportSavePath = ""; LastExportSaveFolder = ""; LastExportReloadSteps = "Export failed. Fix the error in the technical log, then export again."; TechnicalLog.Add("❌ ATS export failed/not verified: " + result.Message); ContentSummary = "❌ ATS export failed/not verified — check technical log."; }
                IndividualLoadHistoryStore.AddFromExportResult(
                    LoadNumber,
                    SelectedCargo?.Name ?? SelectedCargo?.Token ?? "",
                    SelectedTrailer?.Name ?? SelectedTrailer?.Token ?? "",
                    SelectedPickupCompany?.Name ?? "",
                    SelectedPickupCompany?.City ?? "",
                    SelectedPickupCompany?.State ?? "",
                    SelectedDestinationCompany?.Name ?? "",
                    SelectedDestinationCompany?.City ?? "",
                    SelectedDestinationCompany?.State ?? "",
                    Miles,
                    WeightLbs,
                    AssignedDriver,
                    AssignedTruck,
                    SelectedLoadSource?.DisplayName ?? "",
                    result.Success ? "Exported" : "Export Failed",
                    result.SavePath,
                    result.Message);

                foreach (var warning in result.Warnings) if (!string.IsNullOrWhiteSpace(warning)) TechnicalLog.Add("• " + warning);
            }
            catch (Exception ex) { TechnicalLog.Add("ATS export failed: " + ex.Message); ContentSummary = "❌ ATS export failed — " + ex.Message; }
            finally { CommandManager.InvalidateRequerySuggested(); }
        }


        private void SaveCompanyLoadRequest()
        {
            try
            {
                var request = new CompanyLoadRequest
                {
                    LoadNumber = string.IsNullOrWhiteSpace(LoadNumber) ? DispatchService.NextLoadNumber() : LoadNumber.Trim(),
                    PickupCompany = SelectedPickupCompany?.Name ?? "",
                    PickupCity = SelectedPickupCompany?.City ?? "",
                    PickupState = SelectedPickupCompany?.State ?? "",
                    DropOffCompany = SelectedDestinationCompany?.Name ?? "",
                    DropOffCity = SelectedDestinationCompany?.City ?? "",
                    DropOffState = SelectedDestinationCompany?.State ?? "",
                    Cargo = SelectedCargo?.Name ?? SelectedCargo?.Token ?? "",
                    Trailer = SelectedTrailer?.Name ?? SelectedTrailer?.Token ?? "",
                    WeightLbs = WeightLbs <= 0 ? 42000 : WeightLbs,
                    Miles = Miles <= 0 ? 500 : Miles,
                    AssignedDriver = string.IsNullOrWhiteSpace(AssignedDriver) ? "Unassigned" : AssignedDriver,
                    AssignedTruck = string.IsNullOrWhiteSpace(AssignedTruck) ? "Any" : AssignedTruck,
                    CreatedBy = EldCurrentUserService.SafeDisplayName(),
                    Notes = $"Created from Create Load. Source: {SelectedLoadSource?.DisplayName ?? "[Unknown]"}",
                    Status = "Pending"
                };

                CompanyLoadRequestStore.AddOrUpdate(request);

                TechnicalLog.Add($"Company load {request.LoadNumber} saved for {request.AssignedDriver}.");
                ContentSummary = $"Company load {request.LoadNumber} saved for managers/dispatch.";
            }
            catch (Exception ex)
            {
                TechnicalLog.Add("Company load save failed: " + ex.Message);
                ContentSummary = "Company load save failed — check technical log.";
            }
        }

        private void ApplyReloadAssistant(AtsCleanLoadExportResult result, bool verified)
        {
            var savePath = result.SavePath ?? "";
            LastExportSavePath = savePath;
            LastExportSaveFolder = string.IsNullOrWhiteSpace(savePath) ? "" : Path.GetDirectoryName(savePath) ?? "";
            LastExportStatus = verified ? "✅ Export Verified — reload the selected ATS save to show the load." : "⚠ Export written — reload the selected ATS save, then verify the load board.";
            LastExportReloadSteps = BuildReloadSteps(result, IsAtsRunning());
        }

        private static string BuildReloadSteps(AtsCleanLoadExportResult result, bool atsRunning)
        {
            var lines = new System.Collections.Generic.List<string> { "ATS Reload Steps", "", "1. Make sure ATS is at the main menu, not driving in the world.", "2. In ATS, open Load Game.", "3. Select the same profile/save that OverWatch ELD exported into.", "4. Load or reload that save.", "5. Open Freight Market / Cargo Market.", "6. Look for the OverWatch ELD load.", "" };
            if (!string.IsNullOrWhiteSpace(result.SavePath)) lines.Add("Exported save: " + result.SavePath);
            if (!string.IsNullOrWhiteSpace(result.InjectedUnitId)) lines.Add("Injected job unit: " + result.InjectedUnitId);
            if (!string.IsNullOrWhiteSpace(result.VerificationMessage)) lines.Add("Verification: " + result.VerificationMessage);
            if (atsRunning) { lines.Add(""); lines.Add("Warning: ATS appears to be running. The load may not appear until you reload the save from ATS."); }
            return string.Join(Environment.NewLine, lines);
        }

        private static bool IsAtsRunning()
        {
            try { return Process.GetProcessesByName("amtrucks").Length > 0 || Process.GetProcessesByName("amtrucks64").Length > 0 || Process.GetProcessesByName("American Truck Simulator").Length > 0; }
            catch { return false; }
        }

        private void OpenLastExportSaveFolder()
        {
            try { if (!Directory.Exists(LastExportSaveFolder)) { TechnicalLog.Add("ATS save folder is not available yet."); return; } Process.Start(new ProcessStartInfo(LastExportSaveFolder) { UseShellExecute = true }); }
            catch (Exception ex) { TechnicalLog.Add("Open ATS save folder failed: " + ex.Message); }
        }

        private void CopyReloadSteps()
        {
            try { if (string.IsNullOrWhiteSpace(LastExportReloadSteps)) return; Clipboard.SetText(LastExportReloadSteps); TechnicalLog.Add("ATS reload steps copied to clipboard."); ContentSummary = "ATS reload steps copied."; }
            catch (Exception ex) { TechnicalLog.Add("Copy reload steps failed: " + ex.Message); }
        }

        private void LoadAtsProfilesAndSaves()
        {
            try
            {
                var previousProfilePath = SelectedAtsProfile?.Path ?? "";
                var previousSavePath = SelectedAtsSave?.Path ?? "";

                AtsProfiles.Clear();
                AtsSaves.Clear();
                SelectedAtsProfile = null;
                SelectedAtsSave = null;
                SelectedAtsSavePath = "";

                foreach (var profile in FindAtsProfiles())
                    AtsProfiles.Add(profile);

                if (AtsProfiles.Count == 0)
                {
                    TechnicalLog.Add("No ATS profiles found. Expected Documents\\American Truck Simulator\\profiles or steam_profiles.");
                    return;
                }

                SelectedAtsProfile = AtsProfiles.FirstOrDefault(x => SamePath(x.Path, previousProfilePath))
                                     ?? AtsProfiles.FirstOrDefault(x => x.IsLikelyRecent)
                                     ?? AtsProfiles.FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(previousSavePath))
                    SelectedAtsSave = AtsSaves.FirstOrDefault(x => SamePath(x.Path, previousSavePath)) ?? SelectedAtsSave;

                TechnicalLog.Add($"ATS profiles ready: {AtsProfiles.Count} profile(s), {AtsSaves.Count} save(s) for selected profile.");
            }
            catch (Exception ex)
            {
                TechnicalLog.Add("ATS profile/save scan failed: " + ex.Message);
            }
            finally
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void LoadSavesForSelectedProfile()
        {
            try
            {
                var previousSavePath = SelectedAtsSave?.Path ?? "";
                AtsSaves.Clear();
                SelectedAtsSave = null;
                SelectedAtsSavePath = "";

                var profilePath = SelectedAtsProfile?.Path ?? "";
                if (string.IsNullOrWhiteSpace(profilePath) || !Directory.Exists(profilePath))
                    return;

                var saveRoot = Path.Combine(profilePath, "save");
                if (!Directory.Exists(saveRoot))
                {
                    TechnicalLog.Add("Selected ATS profile has no save folder yet: " + profilePath);
                    return;
                }

                foreach (var save in FindAtsSaves(saveRoot))
                    AtsSaves.Add(save);

                SelectedAtsSave = AtsSaves.FirstOrDefault(x => SamePath(x.Path, previousSavePath))
                                  ?? AtsSaves.FirstOrDefault(x => x.IsLikelyRecent)
                                  ?? AtsSaves.FirstOrDefault();
            }
            catch (Exception ex)
            {
                TechnicalLog.Add("ATS save scan failed: " + ex.Message);
            }
            finally
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private static IEnumerable<AtsProfileSaveOption> FindAtsProfiles()
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var atsRoot = Path.Combine(docs, "American Truck Simulator");

            var roots = new[]
            {
                Path.Combine(atsRoot, "profiles"),
                Path.Combine(atsRoot, "steam_profiles")
            };

            var items = new List<AtsProfileSaveOption>();

            foreach (var root in roots)
            {
                if (!Directory.Exists(root))
                    continue;

                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    try
                    {
                        var saveRoot = Path.Combine(dir, "save");
                        var saveCount = Directory.Exists(saveRoot)
                            ? Directory.EnumerateDirectories(saveRoot).Count()
                            : 0;

                        var lastWrite = Directory.GetLastWriteTime(dir);
                        var label = DecodeAtsProfileName(Path.GetFileName(dir));
                        var kind = Path.GetFileName(root).Equals("steam_profiles", StringComparison.OrdinalIgnoreCase)
                            ? "Steam"
                            : "Local";

                        items.Add(new AtsProfileSaveOption
                        {
                            DisplayName = $"{label} ({kind}) • {saveCount} save(s)",
                            Name = label,
                            Path = dir,
                            LastWriteUtc = lastWrite.ToUniversalTime(),
                            IsLikelyRecent = (DateTime.Now - lastWrite).TotalDays <= 14
                        });
                    }
                    catch
                    {
                    }
                }
            }

            return items
                .OrderByDescending(x => x.LastWriteUtc)
                .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IEnumerable<AtsProfileSaveOption> FindAtsSaves(string saveRoot)
        {
            var items = new List<AtsProfileSaveOption>();

            if (!Directory.Exists(saveRoot))
                return items;

            foreach (var dir in Directory.EnumerateDirectories(saveRoot))
            {
                try
                {
                    var gameFile = Path.Combine(dir, "game.sii");
                    var infoFile = Path.Combine(dir, "info.sii");
                    var lastWrite = File.Exists(gameFile)
                        ? File.GetLastWriteTime(gameFile)
                        : Directory.GetLastWriteTime(dir);

                    var folder = Path.GetFileName(dir);
                    var display = folder switch
                    {
                        "autosave" => "Autosave",
                        "quicksave" => "Quick Save",
                        "quick_save" => "Quick Save",
                        _ => folder
                    };

                    items.Add(new AtsProfileSaveOption
                    {
                        DisplayName = $"{display} • {lastWrite:g}",
                        Name = display,
                        Path = dir,
                        LastWriteUtc = lastWrite.ToUniversalTime(),
                        IsLikelyRecent = (DateTime.Now - lastWrite).TotalHours <= 24,
                        HasGameFile = File.Exists(gameFile),
                        GameFilePath = gameFile,
                        InfoFilePath = infoFile
                    });
                }
                catch
                {
                }
            }

            return items
                .OrderByDescending(x => x.LastWriteUtc)
                .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private bool TryApplyBridgeExportToSelectedSave(AtsCleanLoadExportResult result)
        {
            try
            {
                if (result == null || !result.Success)
                    return false;

                var selectedSaveFolder = SelectedAtsSave?.Path ?? "";
                if (string.IsNullOrWhiteSpace(selectedSaveFolder) || !Directory.Exists(selectedSaveFolder))
                    return false;

                var sourceSavePath = result.SavePath ?? "";
                if (string.IsNullOrWhiteSpace(sourceSavePath))
                    return false;

                var sourceFile = File.Exists(sourceSavePath)
                    ? sourceSavePath
                    : Path.Combine(sourceSavePath, "game.sii");

                if (!File.Exists(sourceFile))
                    return false;

                var destFile = Path.Combine(selectedSaveFolder, Path.GetFileName(sourceFile));

                if (SamePath(sourceFile, destFile))
                    return true;

                if (File.Exists(destFile))
                {
                    var backup = destFile + ".overwatcheld_backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    File.Copy(destFile, backup, overwrite: false);
                    TechnicalLog.Add("Selected save backup created: " + backup);
                }

                File.Copy(sourceFile, destFile, overwrite: true);
                LastExportSavePath = destFile;
                LastExportSaveFolder = selectedSaveFolder;

                return true;
            }
            catch (Exception ex)
            {
                TechnicalLog.Add("Selected profile/save apply failed: " + ex.Message);
                return false;
            }
        }

        private void OpenSelectedAtsSaveFolder()
        {
            try
            {
                var path = SelectedAtsSave?.Path ?? "";
                if (!Directory.Exists(path))
                {
                    TechnicalLog.Add("No selected ATS save folder to open.");
                    return;
                }

                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                TechnicalLog.Add("Open selected ATS save folder failed: " + ex.Message);
            }
        }

        private static bool SamePath(string? a, string? b)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                    return false;

                return string.Equals(
                    Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string DecodeAtsProfileName(string? folderName)
        {
            folderName = (folderName ?? "").Trim();

            if (string.IsNullOrWhiteSpace(folderName))
                return "Unknown Profile";

            try
            {
                if (folderName.Length % 2 == 0 && folderName.All(Uri.IsHexDigit))
                {
                    var bytes = new byte[folderName.Length / 2];
                    for (int i = 0; i < bytes.Length; i++)
                        bytes[i] = Convert.ToByte(folderName.Substring(i * 2, 2), 16);

                    var decoded = System.Text.Encoding.UTF8.GetString(bytes).Trim('\0', ' ', '\r', '\n', '\t');
                    if (!string.IsNullOrWhiteSpace(decoded) && decoded.All(ch => !char.IsControl(ch)))
                        return decoded;
                }
            }
            catch
            {
            }

            return folderName;
        }

        private static void OpenAtsModFolder()
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var path = Path.Combine(docs, "American Truck Simulator", "mod");
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }

        private static bool IsMostlyNumeric(string? value)
        {
            value = (value ?? "").Trim();
            if (value.Length < 6)
                return false;

            var digits = value.Count(char.IsDigit);
            return digits >= Math.Max(6, value.Length * 0.75);
        }

        private static string FirstNonBlank(params string?[] values)
        {
            foreach (var value in values) if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            return "";
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            return true;
        }

        private sealed class RelayCommand : ICommand
        {
            private readonly Action<object?> _execute;
            private readonly Predicate<object?>? _canExecute;
            public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null) { _execute = execute; _canExecute = canExecute; }
            public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
            public void Execute(object? parameter) => _execute(parameter);
            public event EventHandler? CanExecuteChanged { add { CommandManager.RequerySuggested += value; } remove { CommandManager.RequerySuggested -= value; } }
        }
    }


    public sealed class AtsLoadSourceOption
    {
        public string SourceKey { get; set; } = "";
        public string SourceMod { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public override string ToString() => DisplayName;
    }

    public sealed class AtsProfileSaveOption
    {
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Path { get; set; } = "";
        public DateTime LastWriteUtc { get; set; }
        public bool IsLikelyRecent { get; set; }
        public bool HasGameFile { get; set; }
        public string GameFilePath { get; set; } = "";
        public string InfoFilePath { get; set; } = "";
        public override string ToString() => DisplayName;
    }

    public sealed class DriverLoadOption
    {
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string DiscordUserId { get; set; } = "";
        public string DiscordName { get; set; } = "";
        public string Role { get; set; } = "";
        public override string ToString() => DisplayName;
    }

    public sealed class TruckLoadOption
    {
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public override string ToString() => DisplayName;
    }
}
