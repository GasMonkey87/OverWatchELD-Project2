using OverWatchELD.Models.Fleet;
using OverWatchELD.Services.Fleet;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace OverWatchELD.ViewModels
{
    public sealed class TrailerFleetViewModel : INotifyPropertyChanged
    {
        private readonly FleetTrailerStore _store = new();
        private string _searchText = "";
        private string _selectedFilter = "All";

        public ObservableCollection<FleetCommandTrailerRow> Trailers { get; } = new();
        public ObservableCollection<string> DriverOptions { get; } = new();
        public ObservableCollection<string> LoadOptions { get; } = new();
        public ObservableCollection<string> Filters { get; } = new()
        {
            "All", "Assigned Load", "Active", "Needs Service", "Needs Inspection", "Unassigned", "Out of Service", "Inactive"
        };

        public string SearchText
        {
            get => _searchText;
            set { _searchText = value ?? ""; OnPropertyChanged(); Refresh(); }
        }

        public string SelectedFilter
        {
            get => _selectedFilter;
            set { _selectedFilter = string.IsNullOrWhiteSpace(value) ? "All" : value; OnPropertyChanged(); Refresh(); }
        }

        private FleetCommandTrailerRow? _selectedTrailer;
        public FleetCommandTrailerRow? SelectedTrailer
        {
            get => _selectedTrailer;
            set { _selectedTrailer = value; OnPropertyChanged(); }
        }

        public string TotalFleetText { get => _totalFleetText; private set { _totalFleetText = value; OnPropertyChanged(); } }
        private string _totalFleetText = "0";
        public string AssignedText { get => _assignedText; private set { _assignedText = value; OnPropertyChanged(); } }
        private string _assignedText = "0";
        public string UnassignedText { get => _unassignedText; private set { _unassignedText = value; OnPropertyChanged(); } }
        private string _unassignedText = "0";
        public string NeedsServiceText { get => _needsServiceText; private set { _needsServiceText = value; OnPropertyChanged(); } }
        private string _needsServiceText = "0";
        public string NeedsInspectionText { get => _needsInspectionText; private set { _needsInspectionText = value; OnPropertyChanged(); } }
        private string _needsInspectionText = "0";
        public string ActiveDriversText { get => _activeDriversText; private set { _activeDriversText = value; OnPropertyChanged(); } }
        private string _activeDriversText = "0";

        public void Refresh()
        {
            var all = _store.LoadAll();
            UpdateOptions(all);
            UpdateStats(all);

            IEnumerable<FleetCommandTrailer> filtered = all;
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var q = SearchText.Trim();
                filtered = filtered.Where(x => Contains(x.TrailerNumber, q) || Contains(x.PlateNumber, q) ||
                    Contains(x.TrailerName, q) || Contains(x.Model, q) || Contains(x.TrailerType, q) ||
                    Contains(x.AssignedDriver, q) || Contains(x.CurrentLoadNumber, q) || Contains(x.Location, q) || Contains(x.Status, q));
            }

            filtered = ApplyFilter(filtered, SelectedFilter);

            var rows = filtered.Select(ToRow)
                .OrderByDescending(x => x.SortPriority)
                .ThenByDescending(x => x.LastUpdatedUtcSort)
                .ThenBy(x => x.TrailerNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Trailers.Clear();
            foreach (var row in rows) Trailers.Add(row);
        }

        public FleetCommandTrailer? LoadSelectedFull() => SelectedTrailer == null ? null : _store.GetById(SelectedTrailer.Id);
        public string GetNextAvailableTrailerNumber() => _store.GetNextAvailableTrailerNumber();

        public FleetCommandTrailer CreateOrGetTrailer(string trailerNumber)
        {
            var key = (trailerNumber ?? "").Trim();
            if (string.IsNullOrWhiteSpace(key)) key = _store.GetNextAvailableTrailerNumber();
            return _store.GetByTrailerNumber(key) ?? new FleetCommandTrailer
            {
                TrailerNumber = key,
                Status = "Unassigned",
                HealthPercent = 100,
                ServiceDueDate = DateTime.Today.AddDays(14),
                InspectionDueDate = DateTime.Today.AddDays(30)
            };
        }

        public void SaveTrailer(FleetCommandTrailer trailer)
        {
            if (trailer == null) return;
            trailer.Status = GetEffectiveStatus(trailer);
            _store.Save(trailer);
            Refresh();
        }

        public void AssignTrailer(FleetCommandTrailer trailer, string driverName, string loadNumber)
        {
            if (trailer == null) return;
            trailer.AssignedDriver = Clean(driverName);
            trailer.CurrentLoadNumber = Clean(loadNumber);
            trailer.Status = GetEffectiveStatus(trailer);
            trailer.UpdatedUtc = DateTimeOffset.UtcNow;
            _store.Save(trailer);
            Refresh();
        }

        public void UseSelectedTrailerForTracking()
        {
            var trailer = LoadSelectedFull();
            if (trailer == null) return;
            ActiveTrailerSelectionStore.SaveForCurrentDriver(new ActiveTrailerSelection
            {
                TrailerId = trailer.Id,
                TrailerNumber = trailer.TrailerNumber,
                TrailerName = trailer.TrailerName,
                Model = trailer.Model
            });
        }

        public void UnassignDriver()
        {
            var trailer = LoadSelectedFull();
            if (trailer == null) return;
            trailer.AssignedDriver = "";
            trailer.CurrentLoadNumber = "";
            trailer.Status = GetEffectiveStatus(trailer);
            _store.Save(trailer);
            Refresh();
        }

        public void MarkInspectionComplete()
        {
            var trailer = LoadSelectedFull();
            if (trailer == null) return;
            trailer.LastInspectionDate = DateTime.Today;
            trailer.InspectionDueDate = DateTime.Today.AddDays(30);
            trailer.Status = GetEffectiveStatus(trailer);
            _store.Save(trailer);
            Refresh();
        }

        public void MarkServiceComplete()
        {
            var trailer = LoadSelectedFull();
            if (trailer == null) return;
            trailer.LastServiceDate = DateTime.Today;
            trailer.ServiceDueDate = DateTime.Today.AddDays(14);
            trailer.HealthPercent = 100;
            trailer.Status = GetEffectiveStatus(trailer);
            _store.Save(trailer);
            Refresh();
        }

        public void DeleteSelected()
        {
            if (SelectedTrailer == null) return;
            _store.Delete(SelectedTrailer.Id);
            Refresh();
        }

        private void UpdateOptions(List<FleetCommandTrailer> all)
        {
            DriverOptions.Clear();
            DriverOptions.Add("Unassigned");
            foreach (var d in all.Select(x => x.AssignedDriver).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
                DriverOptions.Add(d.Trim());

            LoadOptions.Clear();
            LoadOptions.Add("");
            foreach (var l in all.Select(x => x.CurrentLoadNumber).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
                LoadOptions.Add(l.Trim());
        }

        private void UpdateStats(List<FleetCommandTrailer> all)
        {
            TotalFleetText = all.Count.ToString();
            AssignedText = all.Count(x => !string.IsNullOrWhiteSpace(x.AssignedDriver)).ToString();
            UnassignedText = all.Count(x => string.IsNullOrWhiteSpace(x.AssignedDriver)).ToString();
            NeedsServiceText = all.Count(IsNeedsService).ToString();
            NeedsInspectionText = all.Count(IsNeedsInspection).ToString();
            ActiveDriversText = all.Where(x => !string.IsNullOrWhiteSpace(x.AssignedDriver)).Select(x => x.AssignedDriver.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString();
        }

        private static IEnumerable<FleetCommandTrailer> ApplyFilter(IEnumerable<FleetCommandTrailer> rows, string filter)
        {
            return (filter ?? "All") switch
            {
                "Assigned Load" => rows.Where(x => !string.IsNullOrWhiteSpace(x.CurrentLoadNumber)),
                "Active" => rows.Where(x => x.IsActive),
                "Needs Service" => rows.Where(IsNeedsService),
                "Needs Inspection" => rows.Where(IsNeedsInspection),
                "Unassigned" => rows.Where(x => string.IsNullOrWhiteSpace(x.AssignedDriver)),
                "Out of Service" => rows.Where(x => string.Equals(x.Status, "Out of Service", StringComparison.OrdinalIgnoreCase)),
                "Inactive" => rows.Where(x => !x.IsActive),
                _ => rows
            };
        }

        private static bool IsNeedsService(FleetCommandTrailer t) => t.HealthPercent < 70 || (t.ServiceDueDate.HasValue && t.ServiceDueDate.Value.Date <= DateTime.Today);
        private static bool IsNeedsInspection(FleetCommandTrailer t) => t.InspectionDueDate.HasValue && t.InspectionDueDate.Value.Date <= DateTime.Today;

        private static FleetCommandTrailerRow ToRow(FleetCommandTrailer t)
        {
            var status = GetEffectiveStatus(t);
            return new FleetCommandTrailerRow
            {
                Id = t.Id,
                TrailerNumber = t.TrailerNumber,
                PlateNumber = t.PlateNumber,
                TrailerName = FirstNonEmpty(t.TrailerName, t.Model, t.TrailerNumber),
                Model = t.Model,
                TrailerType = t.TrailerType,
                AssignedDriver = string.IsNullOrWhiteSpace(t.AssignedDriver) ? "Unassigned" : t.AssignedDriver,
                CurrentLoadNumber = t.CurrentLoadNumber,
                Status = status,
                Location = t.Location,
                HealthText = $"{Math.Clamp(t.HealthPercent, 0, 100)}%",
                OdometerText = t.OdometerMiles > 0 ? t.OdometerMiles.ToString("N0") : "0",
                ServiceDueText = t.ServiceDueDate?.ToString("MM/dd/yyyy") ?? "",
                InspectionDueText = t.InspectionDueDate?.ToString("MM/dd/yyyy") ?? "",
                LastUpdatedText = t.UpdatedUtc.LocalDateTime.ToString("g"),
                LastUpdatedUtcSort = t.UpdatedUtc.UtcDateTime,
                SortPriority = GetSortPriority(status),
                RowBackground = GetRowBackground(status),
                RowBorderBrush = GetRowBorderBrush(status)
            };
        }

        private static string GetEffectiveStatus(FleetCommandTrailer t)
        {
            if (!t.IsActive) return "Inactive";
            if (string.Equals(t.Status, "Out of Service", StringComparison.OrdinalIgnoreCase)) return "Out of Service";
            if (IsNeedsService(t)) return "Needs Service";
            if (IsNeedsInspection(t)) return "Needs Inspection";
            if (!string.IsNullOrWhiteSpace(t.CurrentLoadNumber)) return "Assigned Load";
            if (!string.IsNullOrWhiteSpace(t.AssignedDriver)) return "Active";
            return "Unassigned";
        }

        private static int GetSortPriority(string status) => status switch
        {
            "Out of Service" => 100,
            "Needs Service" => 90,
            "Needs Inspection" => 80,
            "Assigned Load" => 60,
            "Active" => 50,
            _ => 0
        };

        private static Brush GetRowBackground(string status) => status switch
        {
            "Out of Service" => new SolidColorBrush(Color.FromRgb(69, 10, 10)),
            "Needs Service" => new SolidColorBrush(Color.FromRgb(69, 26, 3)),
            "Needs Inspection" => new SolidColorBrush(Color.FromRgb(66, 32, 6)),
            "Assigned Load" => new SolidColorBrush(Color.FromRgb(12, 38, 32)),
            "Active" => new SolidColorBrush(Color.FromRgb(8, 47, 73)),
            _ => new SolidColorBrush(Color.FromRgb(11, 18, 32))
        };

        private static Brush GetRowBorderBrush(string status) => status switch
        {
            "Out of Service" => Brushes.Red,
            "Needs Service" => Brushes.OrangeRed,
            "Needs Inspection" => Brushes.Orange,
            "Assigned Load" => Brushes.MediumSeaGreen,
            "Active" => Brushes.DeepSkyBlue,
            _ => new SolidColorBrush(Color.FromRgb(51, 65, 85))
        };

        private static bool Contains(string? value, string q) => (value ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        private static string Clean(string? v) => (v ?? "").Trim().Equals("Unassigned", StringComparison.OrdinalIgnoreCase) ? "" : (v ?? "").Trim();
        private static string FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class FleetCommandTrailerRow
    {
        public string Id { get; set; } = "";
        public string TrailerNumber { get; set; } = "";
        public string PlateNumber { get; set; } = "";
        public string TrailerName { get; set; } = "";
        public string Model { get; set; } = "";
        public string TrailerType { get; set; } = "";
        public string AssignedDriver { get; set; } = "";
        public string CurrentLoadNumber { get; set; } = "";
        public string Status { get; set; } = "";
        public string Location { get; set; } = "";
        public string HealthText { get; set; } = "";
        public string OdometerText { get; set; } = "";
        public string ServiceDueText { get; set; } = "";
        public string InspectionDueText { get; set; } = "";
        public string LastUpdatedText { get; set; } = "";
        public DateTime LastUpdatedUtcSort { get; set; }
        public int SortPriority { get; set; }
        public Brush RowBackground { get; set; } = Brushes.Transparent;
        public Brush RowBorderBrush { get; set; } = Brushes.Transparent;
    }
}
