using OverWatchELD.Models.Fleet;
using OverWatchELD.Services;
using OverWatchELD.Services.Fleet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace OverWatchELD.Views
{
    public sealed class AtsModSourceListItem
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string FullPath { get; set; } = "";
        public string Kind { get; set; } = "";

        public override string ToString() => DisplayName;
    }

    public sealed class LoadSourceOption
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";

        public override string ToString() => DisplayName;
    }

    public sealed class LoadChoiceItem
    {
        public string Token { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string SourceId { get; set; } = "";
        public string SourceLabel { get; set; } = "";
        public string CityToken { get; set; } = "";
        public List<string> AllowedTrailerTokens { get; set; } = new();

        public string CleanSourceLabel =>
            CreateLoadWindow.CleanSourceLabel(SourceLabel);

        public string FullLabel =>
            string.IsNullOrWhiteSpace(CleanSourceLabel)
                ? DisplayName
                : $"{DisplayName} — {CleanSourceLabel}";

        public override string ToString() => FullLabel;
    }

    public partial class CreateLoadWindow : Window
    {
        private readonly DispatchJob? _editingJob;
        private readonly FleetCommandStore _fleetStore = new();
        private readonly FleetDriverDirectoryService _driverDirectory = new();

        private AtsModScanResult? _atsMods;

        private List<LoadChoiceItem> _allCargoChoices = new();
        private List<LoadChoiceItem> _allTrailerChoices = new();
        private List<LoadChoiceItem> _allCompanyChoices = new();

        public DispatchJob? SavedJob { get; private set; }

        public CreateLoadWindow(DispatchJob? job)
        {
            InitializeComponent();
            _editingJob = job;

            AtsDataService.EnsureLoaded();
            LoadAtsModDefinitions();
            LoadModSourcePicker();
            RefreshModStatusUi();

            LoadTruckList();
            LoadStateLists();
            LoadAllCitiesInitially();

            AutoFleetSyncCheckBox.IsChecked = true;

            OriginStateBox.SelectionChanged += OriginStateBox_SelectionChanged;
            DestinationStateBox.SelectionChanged += DestinationStateBox_SelectionChanged;
            OriginCityBox.SelectionChanged += OriginCityBox_SelectionChanged;
            DestinationCityBox.SelectionChanged += LocationChanged_RecalcMiles;
            CargoSourceBox.SelectionChanged += CargoSourceBox_SelectionChanged;
            CargoBox.SelectionChanged += CargoBox_SelectionChanged;
            TrailerSourceBox.SelectionChanged += TrailerSourceBox_SelectionChanged;
            CompanySourceBox.SelectionChanged += CompanySourceBox_SelectionChanged;
            ConvoyLoadCheckBox.Checked += ConvoyLoadCheckBox_Changed;
            ConvoyLoadCheckBox.Unchecked += ConvoyLoadCheckBox_Changed;

            BuildModAwareChoices();
            LoadSourceFilters();
            ApplyCargoFilter();
            ApplyTrailerFilter(null);
            ApplyCompanyFilter();

            if (_editingJob != null)
            {
                LoadNumberBox.Text = _editingJob.LoadNumber;

                SelectComboValue(OriginStateBox, _editingJob.OriginState, true);
                FillOriginCities(GetComboValue(OriginStateBox));
                SelectComboValue(OriginCityBox, _editingJob.OriginCity, true);

                SelectComboValue(DestinationStateBox, _editingJob.DestinationState, true);
                FillDestinationCities(GetComboValue(DestinationStateBox));
                SelectComboValue(DestinationCityBox, _editingJob.DestinationCity, true);

                RestoreSelectionByTokenOrName(CargoBox, _editingJob.Cargo);
                SyncSourceFilterFromSelection(CargoSourceBox, CargoBox);

                ApplyTrailerFilter(GetSelectedToken(CargoBox));
                RestoreSelectionByTokenOrName(TrailerBox, _editingJob.Trailer);
                SyncSourceFilterFromSelection(TrailerSourceBox, TrailerBox);

                ApplyCompanyFilter();
                RestoreSelectionByTokenOrName(CompanyBox, _editingJob.Company);
                SyncSourceFilterFromSelection(CompanySourceBox, CompanyBox);

                NotesBox.Text = _editingJob.Notes;
                MilesBox.Text = _editingJob.Miles.ToString();

                SelectStatus(_editingJob.Status);

                SelectComboValue(TruckBox,
                    FirstNonEmpty(
                        TryGetStringProperty(_editingJob, "AssignedTruck"),
                        TryGetStringProperty(_editingJob, "AssignedTruckId"),
                        TryGetStringProperty(_editingJob, "TruckId"),
                        TryGetStringProperty(_editingJob, "TruckNumber")),
                    true);

                PickupDateBox.SelectedDate = TryGetDateProperty(_editingJob, "PickupDate")
                    ?? TryGetDateProperty(_editingJob, "PickupDateUtc");

                DeliveryDateBox.SelectedDate = TryGetDateProperty(_editingJob, "DeliveryDeadline")
                    ?? TryGetDateProperty(_editingJob, "DeliveryDate")
                    ?? TryGetDateProperty(_editingJob, "DeliveryDeadlineUtc");

                WeightBox.Text = FirstNonEmpty(
                    TryGetStringProperty(_editingJob, "CargoWeight"),
                    TryGetStringProperty(_editingJob, "CargoWeightLbs"),
                    TryGetStringProperty(_editingJob, "Weight"));

                PayoutBox.Text = FirstNonEmpty(
                    TryGetStringProperty(_editingJob, "Payout"),
                    TryGetStringProperty(_editingJob, "Revenue"),
                    TryGetStringProperty(_editingJob, "Pay"));

                SelectComboValue(PriorityBox,
                    FirstNonEmpty(TryGetStringProperty(_editingJob, "Priority"), "Normal"),
                    true);

                var convoyEnabled =
                    TryGetBoolProperty(_editingJob, "IsConvoyLoad") ??
                    TryGetBoolProperty(_editingJob, "ConvoyLoad") ??
                    false;

                ConvoyLoadCheckBox.IsChecked = convoyEnabled;
                ConvoyNameBox.Text = TryGetStringProperty(_editingJob, "ConvoyName") ?? "";
                AutoFleetSyncCheckBox.IsChecked = TryGetBoolProperty(_editingJob, "AutoFleetSync") ?? true;
            }
            else
            {
                LoadNumberBox.Text = DispatchService.NextLoadNumber();

                SelectComboValue(OriginStateBox, "Any", false);
                SelectComboValue(DestinationStateBox, "Any", false);
                SelectComboValue(OriginCityBox, "Any", false);
                SelectComboValue(DestinationCityBox, "Any", false);

                if (CargoBox.Items.Count > 0)
                    CargoBox.SelectedIndex = 0;

                ApplyTrailerFilter(GetSelectedToken(CargoBox));

                if (TrailerBox.Items.Count > 0)
                    TrailerBox.SelectedIndex = 0;

                if (CompanyBox.Items.Count > 0)
                    CompanyBox.SelectedIndex = 0;

                SelectComboValue(TruckBox, "Any", false);
                SelectComboValue(PriorityBox, "Normal", false);

                SelectStatus("Available");
                PickupDateBox.SelectedDate = DateTime.Today;
                DeliveryDateBox.SelectedDate = DateTime.Today.AddDays(1);
                ConvoyLoadCheckBox.IsChecked = false;
                ConvoyNameBox.Text = "";
                RecalcMiles();
            }

            Loaded += CreateLoadWindow_Loaded;
            UpdateConvoyState();
        }

        private async void CreateLoadWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadDriverListAsync();

            if (_editingJob != null)
                SelectComboValue(DriverBox, _editingJob.AssignedDriver, true);
            else
                SelectComboValue(DriverBox, "Unassigned", false);
        }

        private async Task LoadDriverListAsync()
        {
            try
            {
                var cfg = VtcConfigService.Load();
                var drivers = await _driverDirectory.LoadDriversAsync(cfg.BotApiBaseUrl ?? "");

                var discordNames = drivers
                    .Select(d => FirstNonEmpty(
                        d.DisplayName,
                        d.DriverName,
                        d.Username,
                        d.DiscordUserId,
                        d.DriverId))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim());

                var localNames = DispatchService.Drivers
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim());

                var profileNames = DriverDropdownService.LoadDriverNames(includeUnassigned: false);

                var names = discordNames
                    .Concat(localNames)
                    .Concat(profileNames)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (!names.Any(x => string.Equals(x, "Unassigned", StringComparison.OrdinalIgnoreCase)))
                    names.Insert(0, "Unassigned");

                DriverBox.ItemsSource = names;
            }
            catch
            {
                var fallback = DispatchService.Drivers
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Concat(DriverDropdownService.LoadDriverNames(includeUnassigned: false))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (!fallback.Any(x => string.Equals(x, "Unassigned", StringComparison.OrdinalIgnoreCase)))
                    fallback.Insert(0, "Unassigned");

                DriverBox.ItemsSource = fallback;
            }
        }

        private void LoadAtsModDefinitions()
        {
            try
            {
                _atsMods = AtsModScannerService.ScanDefault();
            }
            catch
            {
                _atsMods = null;
            }
        }

        private void BuildModAwareChoices()
        {
            _allCargoChoices.Clear();
            _allTrailerChoices.Clear();
            _allCompanyChoices.Clear();

            if (_atsMods != null)
            {
                _allCargoChoices = _atsMods.Cargoes
                    .GroupBy(x => (x.Token ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .Select(x => new LoadChoiceItem
                    {
                        Token = (x.Token ?? "").Trim(),
                        DisplayName = FirstNonEmpty(x.DisplayName, x.Token),
                        SourceId = (x.SourceId ?? "").Trim(),
                        SourceLabel = FirstNonEmpty(x.SourceLabel, "Fallback"),
                        AllowedTrailerTokens = x.AllowedTrailerTokens?
                            .Where(t => !string.IsNullOrWhiteSpace(t))
                            .Select(t => t.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList()
                            ?? new List<string>()
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x.Token))
                    .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                _allTrailerChoices = _atsMods.Trailers
                    .GroupBy(x => (x.Token ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .Select(x => new LoadChoiceItem
                    {
                        Token = (x.Token ?? "").Trim(),
                        DisplayName = FirstNonEmpty(x.DisplayName, x.Token),
                        SourceId = (x.SourceId ?? "").Trim(),
                        SourceLabel = FirstNonEmpty(x.SourceLabel, "Fallback")
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x.Token))
                    .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                _allCompanyChoices = _atsMods.Companies
                    .GroupBy(x => (x.Token ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .Select(x => new LoadChoiceItem
                    {
                        Token = (x.Token ?? "").Trim(),
                        DisplayName = FirstNonEmpty(x.DisplayName, x.Token),
                        SourceId = (x.SourceId ?? "").Trim(),
                        SourceLabel = FirstNonEmpty(x.SourceLabel, "Fallback"),
                        CityToken = (x.CityToken ?? "").Trim()
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x.Token))
                    .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (_allCargoChoices.Count == 0)
            {
                _allCargoChoices = AtsDataService.Cargos
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new LoadChoiceItem
                    {
                        Token = x.Trim(),
                        DisplayName = x.Trim(),
                        SourceId = "vanilla",
                        SourceLabel = "Vanilla ATS"
                    })
                    .ToList();
            }

            if (_allTrailerChoices.Count == 0)
            {
                _allTrailerChoices = AtsDataService.GetAllowedTrailers(null)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new LoadChoiceItem
                    {
                        Token = x.Trim(),
                        DisplayName = x.Trim(),
                        SourceId = "vanilla",
                        SourceLabel = "Vanilla ATS"
                    })
                    .ToList();
            }

            if (_allCompanyChoices.Count == 0)
            {
                _allCompanyChoices = AtsDataService.Companies
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new LoadChoiceItem
                    {
                        Token = x.Trim(),
                        DisplayName = x.Trim(),
                        SourceId = "vanilla",
                        SourceLabel = "Vanilla ATS"
                    })
                    .ToList();
            }
        }

        private void LoadSourceFilters()
        {
            var cargoSources = BuildSourceOptions(_allCargoChoices);
            var trailerSources = BuildSourceOptions(_allTrailerChoices);
            var companySources = BuildSourceOptions(_allCompanyChoices);

            CargoSourceBox.ItemsSource = cargoSources;
            TrailerSourceBox.ItemsSource = trailerSources;
            CompanySourceBox.ItemsSource = companySources;

            if (CargoSourceBox.Items.Count > 0) CargoSourceBox.SelectedIndex = 0;
            if (TrailerSourceBox.Items.Count > 0) TrailerSourceBox.SelectedIndex = 0;
            if (CompanySourceBox.Items.Count > 0) CompanySourceBox.SelectedIndex = 0;
        }

        private static List<LoadSourceOption> BuildSourceOptions(IEnumerable<LoadChoiceItem> items)
        {
            var results = items
                .Select(x => new LoadSourceOption
                {
                    Id = string.IsNullOrWhiteSpace(x.SourceId) ? x.SourceLabel : x.SourceId,
                    DisplayName = CleanSourceLabel(x.SourceLabel)
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.DisplayName))
                .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            results.Insert(0, new LoadSourceOption
            {
                Id = "",
                DisplayName = "All Mods / Sources"
            });

            return results;
        }

        internal static string CleanSourceLabel(string? label)
        {
            var text = (label ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text))
                return "Unknown Source";

            if (text.Equals("Fallback", StringComparison.OrdinalIgnoreCase))
                return "Fallback";

            if (text.Equals("Vanilla ATS", StringComparison.OrdinalIgnoreCase))
                return "Vanilla ATS";

            if (text.Equals("ATS Data Service", StringComparison.OrdinalIgnoreCase))
                return "ATS Data Service";

            var name = Path.GetFileNameWithoutExtension(text);
            name = System.Text.RegularExpressions.Regex.Replace(
                name,
                @"[_\- ]?v?\d+(\.\d+)*",
                "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            name = name.Replace("_", " ").Trim();

            return string.IsNullOrWhiteSpace(name) ? text : name;
        }

        private void ApplyCargoFilter()
        {
            var sourceId = GetSelectedSourceId(CargoSourceBox);

            var filtered = _allCargoChoices
                .Where(x => string.IsNullOrWhiteSpace(sourceId) || string.Equals(x.SourceId, sourceId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            CargoBox.ItemsSource = filtered;

            if (filtered.Count > 0)
                CargoBox.SelectedIndex = 0;
        }

        private void ApplyTrailerFilter(string? cargoToken)
        {
            var sourceId = GetSelectedSourceId(TrailerSourceBox);
            var cargo = _allCargoChoices.FirstOrDefault(x =>
                string.Equals(x.Token, cargoToken, StringComparison.OrdinalIgnoreCase));

            IEnumerable<LoadChoiceItem> filtered = _allTrailerChoices;

            if (!string.IsNullOrWhiteSpace(sourceId))
                filtered = filtered.Where(x => string.Equals(x.SourceId, sourceId, StringComparison.OrdinalIgnoreCase));

            var list = filtered
                .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (cargo != null && cargo.AllowedTrailerTokens.Count > 0)
            {
                var narrowed = list
                    .Where(x =>
                        cargo.AllowedTrailerTokens.Contains(x.Token, StringComparer.OrdinalIgnoreCase) ||
                        cargo.AllowedTrailerTokens.Contains(x.DisplayName, StringComparer.OrdinalIgnoreCase))
                    .ToList();

                if (narrowed.Count > 0)
                    list = narrowed;
            }

            TrailerBox.ItemsSource = list;

            if (list.Count > 0)
                TrailerBox.SelectedIndex = 0;
        }

        private void ApplyCompanyFilter()
        {
            var sourceId = GetSelectedSourceId(CompanySourceBox);

            var city = NormalizeAny(GetComboValue(OriginCityBox));
            var state = NormalizeAny(GetComboValue(OriginStateBox));

            IEnumerable<LoadChoiceItem> filtered = _allCompanyChoices;

            if (!string.IsNullOrWhiteSpace(sourceId))
                filtered = filtered.Where(x => string.Equals(x.SourceId, sourceId, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(city) || !string.IsNullOrWhiteSpace(state))
            {
                var cityStateCompanies = AtsDataService.GetCompaniesByCityState(
                    string.IsNullOrWhiteSpace(city) ? null : city,
                    string.IsNullOrWhiteSpace(state) ? null : state)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (cityStateCompanies.Count > 0)
                {
                    filtered = filtered.Where(x =>
                        cityStateCompanies.Contains(x.Token) ||
                        cityStateCompanies.Contains(x.DisplayName));
                }
            }

            var list = filtered
                .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            CompanyBox.ItemsSource = list;

            if (list.Count > 0)
                CompanyBox.SelectedIndex = 0;
        }

        private void RefreshModStatusUi()
        {
            try
            {
                var mods = _atsMods ?? AtsModScannerService.ScanDefault();
                var selectedIds = AtsModScannerService.LoadSelectedSourceIds();

                var selectedCount = selectedIds.Count;
                var sourceCount = mods?.Sources?.Count ?? 0;
                var cargoCount = mods?.Cargoes?.Count ?? 0;
                var companyCount = mods?.Companies?.Count ?? 0;
                var trailerCount = mods?.Trailers?.Count ?? 0;

                ModStatusText.Text =
                    $"Selected Mods: {(selectedCount == 0 ? "All discovered sources" : selectedCount.ToString())}\n" +
                    $"Sources Loaded: {sourceCount}\n" +
                    $"Cargoes: {cargoCount}  |  Companies: {companyCount}  |  Trailers: {trailerCount}";

                var warnings = mods?.Warnings ?? new List<string>();
                ModWarningsBox.Text = warnings.Count == 0
                    ? "No scanner warnings."
                    : string.Join(Environment.NewLine, warnings);
            }
            catch (Exception ex)
            {
                ModStatusText.Text = "Failed to read ATS mod scanner status.";
                ModWarningsBox.Text = ex.Message;
            }
        }

        private void LoadModSourcePicker()
        {
            try
            {
                if (ModSourcesList == null)
                    return;

                var available = AtsModScannerService.GetAvailableSources()
                    .Select(x => new AtsModSourceListItem
                    {
                        Id = x.Id,
                        DisplayName = $"{CleanSourceLabel(x.DisplayName)} [{x.Kind}]",
                        FullPath = x.FullPath,
                        Kind = x.Kind.ToString()
                    })
                    .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                ModSourcesList.ItemsSource = available;

                var selectedIds = AtsModScannerService.LoadSelectedSourceIds()
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                ModSourcesList.SelectedItems.Clear();

                foreach (var item in available)
                {
                    if (selectedIds.Count == 0 || selectedIds.Contains(item.Id))
                        ModSourcesList.SelectedItems.Add(item);
                }
            }
            catch
            {
                if (ModSourcesList != null)
                    ModSourcesList.ItemsSource = null;
            }
        }

        private void ApplySelectedModsAndReload()
        {
            try
            {
                if (ModSourcesList == null)
                    return;

                var selectedIds = ModSourcesList.SelectedItems
                    .OfType<AtsModSourceListItem>()
                    .Select(x => x.Id)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                AtsModScannerService.SaveSelectedSourceIds(selectedIds);
                AtsModScannerService.Invalidate();

                _atsMods = AtsModScannerService.ScanDefault();

                BuildModAwareChoices();
                LoadSourceFilters();
                ApplyCargoFilter();
                ApplyTrailerFilter(GetSelectedToken(CargoBox));
                ApplyCompanyFilter();
                RefreshModStatusUi();

                MessageBox.Show(
                    $"Applied {selectedIds.Count} selected ATS mod source(s).",
                    "ATS Mods",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Failed to apply ATS mod selection.\n\n" + ex.Message,
                    "ATS Mods",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void RefreshModsButton_Click(object sender, RoutedEventArgs e)
        {
            AtsModScannerService.Invalidate();
            LoadModSourcePicker();
            _atsMods = AtsModScannerService.ScanDefault();
            BuildModAwareChoices();
            LoadSourceFilters();
            ApplyCargoFilter();
            ApplyTrailerFilter(GetSelectedToken(CargoBox));
            ApplyCompanyFilter();
            RefreshModStatusUi();
        }

        private void UseAllModsButton_Click(object sender, RoutedEventArgs e)
        {
            ModSourcesList?.SelectAll();
        }

        private void ClearModsButton_Click(object sender, RoutedEventArgs e)
        {
            ModSourcesList?.UnselectAll();
        }

        private void ApplyModsButton_Click(object sender, RoutedEventArgs e)
        {
            ApplySelectedModsAndReload();
        }

        private void OpenModsFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var modFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "American Truck Simulator",
                    "mod");

                if (!Directory.Exists(modFolder))
                    Directory.CreateDirectory(modFolder);

                Process.Start(new ProcessStartInfo
                {
                    FileName = modFolder,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Failed to open ATS mod folder.\n\n" + ex.Message,
                    "ATS Mods",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void OpenScannerFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var scannerFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "OverWatchELD");

                if (!Directory.Exists(scannerFolder))
                    Directory.CreateDirectory(scannerFolder);

                Process.Start(new ProcessStartInfo
                {
                    FileName = scannerFolder,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Failed to open OverWatchELD scanner folder.\n\n" + ex.Message,
                    "ATS Mods",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void LoadTruckList()
        {
            var trucks = _fleetStore.LoadAll()
                .Select(x => x.TruckNumber)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            trucks.Insert(0, "Any");
            TruckBox.ItemsSource = trucks;
        }

        private void LoadStateLists()
        {
            var states = AtsDataService.States.ToList();
            states.Insert(0, "Any");

            OriginStateBox.ItemsSource = states.ToList();
            DestinationStateBox.ItemsSource = states.ToList();
        }

        private void LoadAllCitiesInitially()
        {
            var allCities = AtsDataService.GetCitiesByState(null).ToList();
            allCities.Insert(0, "Any");

            OriginCityBox.ItemsSource = allCities.ToList();
            DestinationCityBox.ItemsSource = allCities.ToList();
        }

        private void OriginStateBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FillOriginCities(GetComboValue(OriginStateBox));
            ApplyCompanyFilter();
            RecalcMiles();
        }

        private void DestinationStateBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FillDestinationCities(GetComboValue(DestinationStateBox));
            RecalcMiles();
        }

        private void OriginCityBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyCompanyFilter();
            RecalcMiles();
        }

        private void LocationChanged_RecalcMiles(object sender, SelectionChangedEventArgs e)
        {
            RecalcMiles();
        }

        private void CargoSourceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var previousCargo = GetSelectedToken(CargoBox);
            ApplyCargoFilter();
            RestoreSelectionByTokenOrName(CargoBox, previousCargo);
            ApplyTrailerFilter(GetSelectedToken(CargoBox));
        }

        private void CargoBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyTrailerFilter(GetSelectedToken(CargoBox));
        }

        private void TrailerSourceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var previousTrailer = GetSelectedToken(TrailerBox);
            ApplyTrailerFilter(GetSelectedToken(CargoBox));
            RestoreSelectionByTokenOrName(TrailerBox, previousTrailer);
        }

        private void CompanySourceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var previousCompany = GetSelectedToken(CompanyBox);
            ApplyCompanyFilter();
            RestoreSelectionByTokenOrName(CompanyBox, previousCompany);
        }

        private void ConvoyLoadCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            UpdateConvoyState();
        }

        private void UpdateConvoyState()
        {
            var enabled = ConvoyLoadCheckBox.IsChecked == true;
            ConvoyNameBox.IsEnabled = enabled;

            if (!enabled && string.IsNullOrWhiteSpace(ConvoyNameBox.Text))
                ConvoyNameBox.Text = "";
        }

        private void FillOriginCities(string? state)
        {
            if (IsAny(state))
            {
                LoadAllCitiesInitially();
                SelectComboValue(OriginCityBox, "Any", false);
                return;
            }

            var cities = AtsDataService.GetCitiesByState(state).ToList();
            cities.Insert(0, "Any");
            OriginCityBox.ItemsSource = cities;

            if (_editingJob != null && cities.Contains(_editingJob.OriginCity))
                SelectComboValue(OriginCityBox, _editingJob.OriginCity, false);
            else
                SelectComboValue(OriginCityBox, "Any", false);
        }

        private void FillDestinationCities(string? state)
        {
            if (IsAny(state))
            {
                var allCities = AtsDataService.GetCitiesByState(null).ToList();
                allCities.Insert(0, "Any");
                DestinationCityBox.ItemsSource = allCities;
                SelectComboValue(DestinationCityBox, "Any", false);
                return;
            }

            var cities = AtsDataService.GetCitiesByState(state).ToList();
            cities.Insert(0, "Any");
            DestinationCityBox.ItemsSource = cities;

            if (_editingJob != null && cities.Contains(_editingJob.DestinationCity))
                SelectComboValue(DestinationCityBox, _editingJob.DestinationCity, false);
            else
                SelectComboValue(DestinationCityBox, "Any", false);
        }

        private static string GetSelectedSourceId(ComboBox combo)
        {
            return (combo.SelectedItem as LoadSourceOption)?.Id ?? "";
        }

        private static string GetSelectedToken(ComboBox combo)
        {
            return (combo.SelectedItem as LoadChoiceItem)?.Token
                ?? GetComboValue(combo)
                ?? "";
        }

        private static void RestoreSelectionByTokenOrName(ComboBox combo, string? tokenOrName)
        {
            var wanted = (tokenOrName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(wanted))
            {
                if (combo.Items.Count > 0)
                    combo.SelectedIndex = 0;
                return;
            }

            foreach (var item in combo.Items)
            {
                if (item is LoadChoiceItem choice)
                {
                    if (string.Equals(choice.Token, wanted, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(choice.DisplayName, wanted, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(choice.FullLabel, wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        combo.SelectedItem = item;
                        return;
                    }
                }
                else if (item is ComboBoxItem cbi)
                {
                    if (string.Equals(cbi.Content?.ToString(), wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        combo.SelectedItem = item;
                        return;
                    }
                }
                else if (string.Equals(item?.ToString(), wanted, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = item;
                    return;
                }
            }

            if (combo.Items.Count > 0)
                combo.SelectedIndex = 0;
        }

        private static void SyncSourceFilterFromSelection(ComboBox sourceCombo, ComboBox itemCombo)
        {
            if (itemCombo.SelectedItem is not LoadChoiceItem selected)
                return;

            foreach (var item in sourceCombo.Items)
            {
                if (item is LoadSourceOption src &&
                    string.Equals(src.Id, selected.SourceId, StringComparison.OrdinalIgnoreCase))
                {
                    sourceCombo.SelectedItem = item;
                    return;
                }
            }
        }

        private void RecalcMiles()
        {
            var originCity = GetComboValue(OriginCityBox);
            var originState = GetComboValue(OriginStateBox);
            var destCity = GetComboValue(DestinationCityBox);
            var destState = GetComboValue(DestinationStateBox);

            if (IsAny(originCity) || IsAny(originState) || IsAny(destCity) || IsAny(destState))
            {
                MilesBox.Text = "0";
                return;
            }

            var miles = AtsDataService.CalculateMiles(
                originCity,
                originState,
                destCity,
                destState);

            MilesBox.Text = miles.ToString();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(MilesBox.Text.Trim(), out var miles))
                miles = 0;

            var job = _editingJob ?? new DispatchJob();

            var normalizedLoadNumber = (LoadNumberBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(normalizedLoadNumber))
                normalizedLoadNumber = DispatchService.NextLoadNumber();

            var cargoToken = GetSelectedToken(CargoBox);
            var trailerToken = GetSelectedToken(TrailerBox);
            var companyToken = GetSelectedToken(CompanyBox);

            job.LoadNumber = normalizedLoadNumber;
            job.Company = NormalizeAny(companyToken);
            job.OriginCity = NormalizeAny(GetComboValue(OriginCityBox));
            job.OriginState = NormalizeAny(GetComboValue(OriginStateBox));
            job.DestinationCity = NormalizeAny(GetComboValue(DestinationCityBox));
            job.DestinationState = NormalizeAny(GetComboValue(DestinationStateBox));
            job.Miles = miles;
            TrySetProperty(job, "ActualDrivenMiles", 0d);
            TrySetProperty(job, "StartOdometerMiles", null);
            job.Cargo = cargoToken;
            job.Trailer = trailerToken;
            job.AssignedDriver = GetComboValue(DriverBox) ?? "Unassigned";
            job.Status = GetComboValue(StatusBox) ?? "Available";
            job.Notes = NotesBox.Text.Trim();
            job.UpdatedUtc = DateTime.UtcNow;

            TrySetProperty(job, "BolNumber", normalizedLoadNumber);
            TrySetProperty(job, "BOLNumber", normalizedLoadNumber);
            TrySetProperty(job, "BillOfLadingNumber", normalizedLoadNumber);
            TrySetProperty(job, "BillOfLadingNo", normalizedLoadNumber);
            TrySetProperty(job, "CurrentLoadNumber", normalizedLoadNumber);
            TrySetProperty(job, "LoadId", normalizedLoadNumber);

            var truckNumber = NormalizeAny(GetComboValue(TruckBox));
            var pickupDate = PickupDateBox.SelectedDate;
            var deliveryDate = DeliveryDateBox.SelectedDate;
            var weightText = WeightBox.Text.Trim();
            var payoutText = PayoutBox.Text.Trim();

            if (decimal.TryParse(
                payoutText.Replace("$", "").Replace(",", "").Trim(),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var payoutUsd))
            {
                job.Payout = payoutUsd;
                TrySetProperty(job, "RevenueUsd", payoutUsd);
            }
            else if (decimal.TryParse(
                payoutText.Replace("$", "").Replace(",", "").Trim(),
                NumberStyles.Any,
                CultureInfo.CurrentCulture,
                out payoutUsd))
            {
                job.Payout = payoutUsd;
                TrySetProperty(job, "RevenueUsd", payoutUsd);
            }

            var trailerOwner = DetermineTrailerOwnerForSave(cargoToken, trailerToken);
            var priority = GetComboValue(PriorityBox) ?? "Normal";
            var convoyLoad = ConvoyLoadCheckBox.IsChecked == true;
            var convoyName = ConvoyNameBox.Text.Trim();
            var autoFleetSync = AutoFleetSyncCheckBox.IsChecked == true;

            TrySetProperty(job, "AssignedTruck", truckNumber);
            TrySetProperty(job, "AssignedTruckId", truckNumber);
            TrySetProperty(job, "TruckId", truckNumber);
            TrySetProperty(job, "TruckNumber", truckNumber);

            TrySetProperty(job, "PickupDate", pickupDate);
            TrySetProperty(job, "PickupDateUtc", pickupDate?.ToUniversalTime());
            TrySetProperty(job, "DeliveryDeadline", deliveryDate);
            TrySetProperty(job, "DeliveryDate", deliveryDate);
            TrySetProperty(job, "DeliveryDeadlineUtc", deliveryDate?.ToUniversalTime());

            TrySetNumericProperty(job, "CargoWeight", weightText);
            TrySetNumericProperty(job, "CargoWeightLbs", weightText);
            TrySetNumericProperty(job, "Weight", weightText);

            TrySetNumericProperty(job, "Payout", payoutText);
            TrySetNumericProperty(job, "Revenue", payoutText);
            TrySetNumericProperty(job, "Pay", payoutText);

            TrySetProperty(job, "TrailerOwner", trailerOwner);
            TrySetProperty(job, "Priority", priority);
            TrySetProperty(job, "IsConvoyLoad", convoyLoad);
            TrySetProperty(job, "ConvoyLoad", convoyLoad);
            TrySetProperty(job, "ConvoyName", convoyName);
            TrySetProperty(job, "AutoFleetSync", autoFleetSync);

            if (_editingJob == null)
            {
                if (string.IsNullOrWhiteSpace(job.PostedBy))
                    job.PostedBy = "Dispatcher";

                if (job.PostedUtc == default)
                    job.PostedUtc = DateTime.UtcNow;

                if (string.IsNullOrWhiteSpace(job.DispatchMode))
                    job.DispatchMode = "Open";

                DispatchService.AddJob(job);
            }
            else
            {
                DispatchService.UpdateJob(job);
            }

            if (autoFleetSync)
                SyncFleetAssignment(truckNumber, job.AssignedDriver, normalizedLoadNumber, job.Status);

            var atsValidation = AtsLoadValidationService.Validate(
                job.Cargo,
                job.Company,
                job.Trailer,
                _atsMods);

            if (!atsValidation.IsValid)
            {
                MessageBox.Show(
                    string.Join(Environment.NewLine, atsValidation.Errors),
                    "ATS Load Validation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            AtsJobExportService.ExportPendingJob(job);

            var injected = AtsMarketInjectionService.QueueSingleJob(job);
            if (!injected)
            {
                MessageBox.Show(
                    "Load saved to ELD, but ATS injection was blocked or failed.\n\nCheck Documents\\OverWatchELD\\ats_injector.log and ats_injection.settings.json.",
                    "ATS Injection",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            SavedJob = job;
            DialogResult = true;
            Close();
        }

        private void SyncFleetAssignment(string truckNumber, string driverName, string loadNumber, string status)
        {
            if (string.IsNullOrWhiteSpace(loadNumber))
                return;

            try
            {
                FleetCommandTruck? truck = null;

                if (!string.IsNullOrWhiteSpace(truckNumber) &&
                    !truckNumber.Equals("Any", StringComparison.OrdinalIgnoreCase))
                {
                    truck = _fleetStore.GetByTruckNumber(truckNumber);
                }

                if (truck == null &&
                    !string.IsNullOrWhiteSpace(driverName) &&
                    !driverName.Equals("Unassigned", StringComparison.OrdinalIgnoreCase))
                {
                    var all = _fleetStore.LoadAll();

                    truck = all.FirstOrDefault(x =>
                        string.Equals((x.AssignedDriver ?? "").Trim(), driverName.Trim(), StringComparison.OrdinalIgnoreCase) &&
                        (
                            string.Equals((x.Status ?? "").Trim(), "Active", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals((x.Status ?? "").Trim(), "Driving", StringComparison.OrdinalIgnoreCase) ||
                            x.IsDriving
                        ));

                    if (truck == null)
                    {
                        truck = all.FirstOrDefault(x =>
                            string.Equals((x.AssignedDriver ?? "").Trim(), driverName.Trim(), StringComparison.OrdinalIgnoreCase));
                    }
                }

                if (truck == null)
                    return;

                truck.AssignedDriver = string.IsNullOrWhiteSpace(driverName) ? truck.AssignedDriver : driverName;
                truck.CurrentLoadNumber = loadNumber;
                truck.Status = string.IsNullOrWhiteSpace(status)
                    ? "Assigned Load"
                    : status.Equals("Available", StringComparison.OrdinalIgnoreCase)
                        ? "Assigned Load"
                        : status;
                truck.UpdatedUtc = DateTimeOffset.UtcNow;

                _fleetStore.Save(truck);
            }
            catch
            {
            }
        }

        private static string DetermineTrailerOwnerForSave(string? cargo, string? trailer)
        {
            var c = NormalizeLoose(cargo);
            var t = NormalizeLoose(trailer);

            if (ContainsAny(t, "lowboy", "lowbed", "dropdeck", "flatbed", "log", "grainhopper", "grainhopp"))
                return "Driver";

            if (ContainsAny(c, "bulldozer", "excavator", "crawler", "yardtruck", "mobilecrane", "transformer", "scrapmetal", "lumber", "logs", "rooftiles", "mortar", "silica"))
                return "Driver";

            return "Company";
        }

        private static bool ContainsAny(string haystack, params string[] needles)
        {
            foreach (var needle in needles)
            {
                if (haystack.Contains(NormalizeLoose(needle), StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string NormalizeLoose(string? value)
        {
            return (value ?? "")
                .Trim()
                .Replace("_", "")
                .Replace("-", "")
                .Replace(" ", "")
                .ToLowerInvariant();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void SelectStatus(string status)
        {
            SelectComboValue(StatusBox, status, false);
        }

        private static string? GetComboValue(ComboBox combo)
        {
            if (combo.SelectedItem is ComboBoxItem cbi)
                return cbi.Content?.ToString();

            if (combo.SelectedItem is LoadChoiceItem choice)
                return choice.DisplayName;

            if (combo.SelectedItem is LoadSourceOption src)
                return src.DisplayName;

            return combo.SelectedItem?.ToString() ?? combo.Text;
        }

        private static bool IsAny(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ||
                   value.Equals("Any", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeAny(string? value)
        {
            return IsAny(value) ? "" : value!.Trim();
        }

        private static void SelectComboValue(ComboBox combo, string? value, bool allowPlainTextFallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (combo.Items.Count > 0)
                    combo.SelectedIndex = 0;
                return;
            }

            foreach (var item in combo.Items)
            {
                var itemText = item is ComboBoxItem cbi
                    ? cbi.Content?.ToString()
                    : item?.ToString();

                if (string.Equals(itemText, value, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = item;
                    return;
                }
            }

            if (allowPlainTextFallback && combo.IsEditable)
                combo.Text = value;
            else if (combo.Items.Count > 0)
                combo.SelectedIndex = 0;
        }

        private static string? TryGetStringProperty(object obj, string propertyName)
        {
            var prop = obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null) return null;

            var value = prop.GetValue(obj);
            return value?.ToString();
        }

        private static DateTime? TryGetDateProperty(object obj, string propertyName)
        {
            var prop = obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null) return null;

            var value = prop.GetValue(obj);
            if (value == null) return null;

            if (value is DateTime dt)
                return dt;

            if (value is DateTimeOffset dto)
                return dto.LocalDateTime;

            if (DateTime.TryParse(value.ToString(), out var parsed))
                return parsed;

            return null;
        }

        private static bool? TryGetBoolProperty(object obj, string propertyName)
        {
            var prop = obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null) return null;

            var value = prop.GetValue(obj);
            if (value == null) return null;

            if (value is bool b)
                return b;

            if (bool.TryParse(value.ToString(), out var parsed))
                return parsed;

            return null;
        }

        private static void TrySetProperty(object obj, string propertyName, object? value)
        {
            var prop = obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null || !prop.CanWrite)
                return;

            try
            {
                if (value == null)
                {
                    prop.SetValue(obj, null);
                    return;
                }

                var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

                if (targetType == typeof(string))
                {
                    prop.SetValue(obj, value.ToString());
                    return;
                }

                if (targetType == typeof(bool))
                {
                    if (value is bool b)
                        prop.SetValue(obj, b);
                    else if (bool.TryParse(value.ToString(), out var parsed))
                        prop.SetValue(obj, parsed);
                    return;
                }

                if (targetType == typeof(DateTime))
                {
                    if (value is DateTime dt)
                        prop.SetValue(obj, dt);
                    else if (value is DateTimeOffset dto)
                        prop.SetValue(obj, dto.DateTime);
                    else if (DateTime.TryParse(value.ToString(), out var parsed))
                        prop.SetValue(obj, parsed);
                    return;
                }

                if (targetType == typeof(DateTimeOffset))
                {
                    if (value is DateTimeOffset dto)
                        prop.SetValue(obj, dto);
                    else if (value is DateTime dt2)
                        prop.SetValue(obj, new DateTimeOffset(dt2));
                    else if (DateTimeOffset.TryParse(value.ToString(), out var parsed))
                        prop.SetValue(obj, parsed);
                    return;
                }

                if (targetType.IsEnum)
                {
                    var enumValue = Enum.Parse(targetType, value.ToString() ?? "", true);
                    prop.SetValue(obj, enumValue);
                    return;
                }

                prop.SetValue(obj, Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture));
            }
            catch
            {
            }
        }

        private static void TrySetNumericProperty(object obj, string propertyName, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            var prop = obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null || !prop.CanWrite)
                return;

            var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

            try
            {
                if (targetType == typeof(int) && int.TryParse(value, out var i))
                    prop.SetValue(obj, i);
                else if (targetType == typeof(long) && long.TryParse(value, out var l))
                    prop.SetValue(obj, l);
                else if (targetType == typeof(double) && double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                    prop.SetValue(obj, d);
                else if (targetType == typeof(decimal) && decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var m))
                    prop.SetValue(obj, m);
                else if (targetType == typeof(float) && float.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var f))
                    prop.SetValue(obj, f);
                else if (targetType == typeof(string))
                    prop.SetValue(obj, value);
            }
            catch
            {
            }
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                var s = (value ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(s))
                    return s;
            }

            return "";
        }
    }
}