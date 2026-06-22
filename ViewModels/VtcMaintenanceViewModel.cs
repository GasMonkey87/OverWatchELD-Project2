using OverWatchELD.Models;
using OverWatchELD.Services;
using OverWatchELD.Services.Fleet;
using OverWatchELD.Stores;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace OverWatchELD.ViewModels
{
    public class VtcMaintenanceViewModel : INotifyPropertyChanged
    {
        private readonly Random _random = new();
        private readonly MaintenanceRequestTicketStore _ticketStore = new();
        private readonly MaintenanceRequestDiscordPoster _discordPoster = new();
        private VtcMaintenanceState _state = new();

        public ObservableCollection<VtcMaintenanceTruck> Trucks { get; } = new();
        public ObservableCollection<VtcMaintenanceAlert> Alerts { get; } = new();
        public ObservableCollection<MaintenanceRequestTicket> MaintenanceRequests { get; } = new();

        public ObservableCollection<string> CommonIssues { get; } = new()
        {
            "Flat Tire", "Overheating", "Oil Leak", "Low Coolant", "Brake Air Leak", "Dead Battery",
            "Check Engine Light", "Transmission Slip", "Fuel Leak", "Trailer Light Failure", "ABS Warning",
            "Blown Air Line", "Bad Alternator", "Low Tire Pressure", "Radiator Leak"
        };

        private string _selectedIssue = "Flat Tire";
        public string SelectedIssue
        {
            get => _selectedIssue;
            set { _selectedIssue = string.IsNullOrWhiteSpace(value) ? "Flat Tire" : value; OnPropertyChanged(); }
        }

        private VtcMaintenanceTruck? _selectedTruck;
        public VtcMaintenanceTruck? SelectedTruck
        {
            get => _selectedTruck;
            set
            {
                _selectedTruck = value;
                OnPropertyChanged();
                RefreshStats();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private MaintenanceRequestTicket? _selectedMaintenanceRequest;
        public MaintenanceRequestTicket? SelectedMaintenanceRequest
        {
            get => _selectedMaintenanceRequest;
            set { _selectedMaintenanceRequest = value; OnPropertyChanged(); }
        }

        private string _requestNotes = "";
        public string RequestNotes
        {
            get => _requestNotes;
            set { _requestNotes = value ?? ""; OnPropertyChanged(); }
        }

        public bool RequestDotInspection { get => _requestDotInspection; set { _requestDotInspection = value; OnPropertyChanged(); } }
        private bool _requestDotInspection;

        public bool RequestDamageRepair { get => _requestDamageRepair; set { _requestDamageRepair = value; OnPropertyChanged(); } }
        private bool _requestDamageRepair;

        public bool RequestOtherMaintenance { get => _requestOtherMaintenance; set { _requestOtherMaintenance = value; OnPropertyChanged(); } }
        private bool _requestOtherMaintenance;

        public bool RequestMalfunctionRepair { get => _requestMalfunctionRepair; set { _requestMalfunctionRepair = value; OnPropertyChanged(); } }
        private bool _requestMalfunctionRepair;

        private string _lastRequestPostStatus = "";
        public string LastRequestPostStatus
        {
            get => _lastRequestPostStatus;
            set { _lastRequestPostStatus = value ?? ""; OnPropertyChanged(); }
        }

        public bool RandomMalfunctionsEnabled
        {
            get => _state.RandomMalfunctionsEnabled;
            set
            {
                _state.RandomMalfunctionsEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RandomMalfunctionInfoText));
                Save();
            }
        }

        public VtcMaintenanceTruck? CurrentDriverTruck =>
            SelectedTruck ?? Trucks.FirstOrDefault(t => !t.OutOfService) ?? Trucks.FirstOrDefault();

        public string CurrentDriverTruckDisplay => CurrentDriverTruck == null
            ? "No truck assigned yet."
            : $"Unit {CurrentDriverTruck.UnitNumber} • {CurrentDriverTruck.TruckName} • {CurrentDriverTruck.Location}";

        public string CurrentDriverIssueDisplay => CurrentDriverTruck == null || string.IsNullOrWhiteSpace(CurrentDriverTruck.CurrentIssue)
            ? "No active malfunction."
            : $"{CurrentDriverTruck.CurrentIssueSeverity}: {CurrentDriverTruck.CurrentIssue}";

        public string RandomMalfunctionInfoText => RandomMalfunctionsEnabled
            ? "Random vehicle malfunctions are ENABLED."
            : "Random vehicle malfunctions are OFF.";

        public double RandomMalfunctionProgressValue
        {
            get
            {
                var truck = CurrentDriverTruck;
                if (truck == null || truck.RandomMalfunctionTargetOdometerMiles <= truck.RandomMalfunctionStartOdometerMiles) return 0;
                var total = truck.RandomMalfunctionTargetOdometerMiles - truck.RandomMalfunctionStartOdometerMiles;
                var done = truck.OdometerMiles - truck.RandomMalfunctionStartOdometerMiles;
                return Math.Clamp((done / total) * 100, 0, 100);
            }
        }

        public string RandomMalfunctionProgressText
        {
            get
            {
                var truck = CurrentDriverTruck;
                if (truck == null || truck.RandomMalfunctionTargetOdometerMiles <= 0) return "Random malfunction range: 500–700 miles";
                var remaining = Math.Max(0, truck.RandomMalfunctionTargetOdometerMiles - truck.OdometerMiles);
                var driven = Math.Max(0, truck.OdometerMiles - truck.RandomMalfunctionStartOdometerMiles);
                var target = Math.Max(500, truck.RandomMalfunctionTargetOdometerMiles - truck.RandomMalfunctionStartOdometerMiles);
                return $"Next possible random malfunction in: {remaining:0} miles    ({driven:0} / {target:0} miles)";
            }
        }

        public int TotalTrucks => Trucks.Count;
        public int ActiveTrucks => Trucks.Count(t => !t.OutOfService);
        public int OutOfServiceTrucks => Trucks.Count(t => t.OutOfService);
        public int OpenRequestCount => MaintenanceRequests.Count(r => Same(r.Status, "Open"));
        public int FixedRequestCount => MaintenanceRequests.Count(r => Same(r.Status, "Fixed"));
        public int ServiceDueTrucks => Trucks.Count(IsServiceDue);
        public int DotExpiringSoon => Trucks.Count(IsDotExpiringSoon);
        public int CriticalDamage => Trucks.Count(t => t.ConditionPercent <= 65);
        public double AverageCondition => Trucks.Count == 0 ? 100 : Math.Round(Trucks.Average(t => t.ConditionPercent), 1);
        public double AverageFuel => Trucks.Count == 0 ? 0 : Math.Round(Trucks.Average(t => t.FuelPercent), 1);

        public int FleetReadiness
        {
            get
            {
                var score = 100;
                score -= OutOfServiceTrucks * 15;
                score -= ServiceDueTrucks * 8;
                score -= DotExpiringSoon * 6;
                score -= CriticalDamage * 12;
                score -= OpenRequestCount * 3;
                return Math.Clamp(score, 0, 100);
            }
        }

        public string SelectedTruckStatus
        {
            get
            {
                if (SelectedTruck == null) return "No truck selected";
                if (SelectedTruck.OutOfService) return "Out Of Service";
                if (!string.IsNullOrWhiteSpace(SelectedTruck.CurrentIssue)) return "Malfunction Active";
                if (SelectedTruck.ConditionPercent <= 65) return "Critical";
                if (IsServiceDue(SelectedTruck) || IsDotExpiringSoon(SelectedTruck)) return "Attention Soon";
                return "Healthy";
            }
        }

        public string SelectedTruckStatusBrush => SelectedTruckStatus switch
        {
            "Out Of Service" => "#EF4444",
            "Critical" => "#EF4444",
            "Malfunction Active" => "#F97316",
            "Attention Soon" => "#F59E0B",
            "Healthy" => "#22C55E",
            _ => "#94A3B8"
        };

        public ICommand AddTruckCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand MarkServicedCommand { get; }
        public ICommand AddInspectionCommand { get; }
        public ICommand TakeOutOfServiceCommand { get; }
        public ICommand ReturnToActiveCommand { get; }
        public ICommand AddDamageReportCommand { get; }
        public ICommand ResolveDamageCommand { get; }
        public ICommand ApplySelectedIssueCommand { get; }
        public ICommand TriggerRandomMalfunctionCommand { get; }
        public ICommand ClearCurrentIssueCommand { get; }
        public ICommand SubmitMaintenanceRequestCommand { get; }
        public ICommand RepairMalfunctionsAndSubmitTicketCommand { get; }

        public VtcMaintenanceViewModel()
        {
            AddTruckCommand = new RelayCommand(_ => AddTruck());
            SaveCommand = new RelayCommand(_ => Save());
            RefreshCommand = new RelayCommand(_ => Load());
            MarkServicedCommand = new RelayCommand(_ => MarkServiced(), _ => SelectedTruck != null);
            AddInspectionCommand = new RelayCommand(_ => AddInspection(), _ => SelectedTruck != null);
            TakeOutOfServiceCommand = new RelayCommand(_ => SetOutOfService(true), _ => SelectedTruck != null);
            ReturnToActiveCommand = new RelayCommand(_ => SetOutOfService(false), _ => SelectedTruck != null);
            AddDamageReportCommand = new RelayCommand(_ => AddDamageReport(), _ => SelectedTruck != null);
            ResolveDamageCommand = new RelayCommand(_ => ResolveDamage(), _ => SelectedTruck != null);
            ApplySelectedIssueCommand = new RelayCommand(_ => ApplySelectedIssue(), _ => SelectedTruck != null);
            TriggerRandomMalfunctionCommand = new RelayCommand(_ => TriggerRandomMalfunction(), _ => SelectedTruck != null);
            ClearCurrentIssueCommand = new RelayCommand(_ => ClearCurrentIssue(), _ => SelectedTruck != null);
            SubmitMaintenanceRequestCommand = new RelayCommand(async _ => await SubmitMaintenanceRequestAsync(false), _ => CurrentDriverTruck != null);
            RepairMalfunctionsAndSubmitTicketCommand = new RelayCommand(async _ => await SubmitMaintenanceRequestAsync(true), _ => CurrentDriverTruck != null);

            Load();
        }

        public void Load()
        {
            _state = VtcMaintenanceStore.Load() ?? new VtcMaintenanceState();
            LoadFleetTrucksIntoMaintenance();
            LoadMaintenanceRequests();
            RebuildAlerts();
            RefreshStats();
        }

        private void LoadFleetTrucksIntoMaintenance()
        {
            var fleetStore = new FleetCommandStore();
            var fleetTrucks = fleetStore.LoadAll()
                .Where(t => !string.IsNullOrWhiteSpace(t.Id) ||
                            !string.IsNullOrWhiteSpace(t.TruckNumber) ||
                            !string.IsNullOrWhiteSpace(t.TruckName) ||
                            !string.IsNullOrWhiteSpace(t.PlateNumber))
                .OrderBy(t => t.TruckNumber)
                .ThenBy(t => t.TruckName)
                .ToList();

            var rebuilt = fleetTrucks.Select(f =>
            {
                var existing = _state.Trucks.FirstOrDefault(t =>
                    Same(t.TruckId, f.Id) ||
                    Same(t.UnitNumber, f.TruckNumber) ||
                    Same(t.TruckName, f.TruckName) ||
                    Same(t.PlateNumber, f.PlateNumber));

                var truck = existing ?? new VtcMaintenanceTruck();

                truck.TruckId = f.Id;
                truck.UnitNumber = f.TruckNumber;
                truck.TruckName = f.TruckName;
                truck.PlateNumber = f.PlateNumber;
                truck.AssignedDriver = f.AssignedDriver;
                truck.Location = f.Location;
                truck.FuelPercent = f.FuelPercent;
                truck.ConditionPercent = f.HealthPercent > 0 ? f.HealthPercent : truck.ConditionPercent;
                truck.OdometerMiles = f.OdometerMiles;
                truck.LastServiceUtc ??= f.LastServiceDate ?? DateTime.UtcNow;
                truck.LastInspectionUtc ??= f.LastInspectionDate ?? DateTime.UtcNow;
                truck.DotExpirationUtc ??= f.InspectionDueDate ?? DateTime.UtcNow.AddMonths(12);
                truck.ServiceHistory ??= new();
                truck.DamageReports ??= new();

                EnsureRandomTarget(truck);
                return truck;
            }).ToList();

            _state.Trucks = rebuilt;

            Trucks.Clear();
            foreach (var truck in _state.Trucks.OrderBy(t => t.UnitNumber))
                Trucks.Add(truck);

            if (SelectedTruck == null && Trucks.Count > 0)
                SelectedTruck = Trucks[0];
        }

        public void LoadMaintenanceRequests()
        {
            MaintenanceRequests.Clear();

            foreach (var r in _ticketStore.LoadAll()
                         .OrderByDescending(x => Same(x.Status, "Open"))
                         .ThenByDescending(x => x.CreatedUtc))
            {
                if (string.IsNullOrWhiteSpace(r.RequestNumber))
                    r.RequestNumber = $"MR-{r.CreatedUtc.ToLocalTime():yyyyMMdd-HHmmss}";

                MaintenanceRequests.Add(r);
            }

            RefreshStats();
        }

        private async Task SubmitMaintenanceRequestAsync(bool repairMalfunctionsButton)
        {
            var truck = CurrentDriverTruck;
            if (truck == null)
                return;

            var ticket = new MaintenanceRequestTicket
            {
                RequestNumber = $"MR-{DateTime.Now:yyyyMMdd-HHmmss}",
                TruckId = truck.TruckId ?? "",
                UnitNumber = truck.UnitNumber ?? "",
                TruckName = truck.TruckName ?? "",
                PlateNumber = truck.PlateNumber ?? "",
                DriverName = FirstNonBlank(truck.AssignedDriver, EldDriverIdentityResolver.DriverName()),
                Location = truck.Location ?? "",
                OdometerMiles = truck.OdometerMiles,
                FuelPercent = truck.FuelPercent,
                ConditionPercent = truck.ConditionPercent,
                CurrentIssue = truck.CurrentIssue ?? "",
                CurrentIssueSeverity = truck.CurrentIssueSeverity ?? "",
                OutOfService = truck.OutOfService,
                DotInspectionRequested = RequestDotInspection,
                DamageRepairRequested = RequestDamageRepair,
                OtherMaintenanceRequested = RequestOtherMaintenance,
                MalfunctionRepairRequested = RequestMalfunctionRepair || repairMalfunctionsButton,
                Notes = RequestNotes ?? "",
                Status = "Open",
                CreatedUtc = DateTime.UtcNow
            };

            if (!ticket.DotInspectionRequested &&
                !ticket.DamageRepairRequested &&
                !ticket.OtherMaintenanceRequested &&
                !ticket.MalfunctionRepairRequested)
            {
                ticket.OtherMaintenanceRequested = true;
                if (string.IsNullOrWhiteSpace(ticket.Notes))
                    ticket.Notes = "General maintenance request submitted.";
            }

            ticket = _ticketStore.Add(ticket);

            MessageBox.Show(
    $"Ticket created:\n{ticket.RequestNumber}\n\nLoaded count: {_ticketStore.LoadAll().Count}",
    "Maintenance Debug");

            if (string.IsNullOrWhiteSpace(ticket.RequestNumber))
                ticket.RequestNumber = $"MR-{DateTime.Now:yyyyMMdd-HHmmss}";

            MaintenanceRequests.Insert(0, ticket);
            SelectedMaintenanceRequest = ticket;

            if (repairMalfunctionsButton)
            {
                truck.CurrentIssue = "";
                truck.CurrentIssueSeverity = "";
                truck.OutOfService = false;

                foreach (var report in truck.DamageReports.Where(x => !x.Resolved))
                    report.Resolved = true;

                truck.ServiceHistory.Add(new VtcServiceRecord
                {
                    ServiceType = "Malfunction Cleared",
                    Notes = $"Repair malfunction button used while submitting ticket {ticket.RequestNumber}.",
                    OdometerMiles = truck.OdometerMiles,
                    CompletedBy = EldDriverIdentityResolver.DriverName()
                });

                RebuildAlerts();
                Save();
            }

            var posted = await _discordPoster.PostAsync(ticket);

            LastRequestPostStatus = posted
                ? $"Ticket {ticket.RequestNumber} submitted and posted to Discord."
                : $"Ticket {ticket.RequestNumber} submitted locally. Discord post failed.";

            RequestNotes = "";
            RequestDotInspection = false;
            RequestDamageRepair = false;
            RequestOtherMaintenance = false;
            RequestMalfunctionRepair = false;

            RefreshStats();
            CommandManager.InvalidateRequerySuggested();
        }

        public void Save()
        {
            _state.Trucks = Trucks.ToList();
            _state.Alerts = Alerts.ToList();
            VtcMaintenanceStore.Save(_state);
            RefreshStats();
            CommandManager.InvalidateRequerySuggested();
        }

        private void AddTruck()
        {
            var truck = new VtcMaintenanceTruck
            {
                UnitNumber = (Trucks.Count + 1).ToString("000"),
                TruckName = "New Truck",
                AssignedDriver = "Unassigned",
                Location = "Unknown",
                FuelPercent = 100,
                ConditionPercent = 100,
                OdometerMiles = 0,
                LastServiceUtc = DateTime.UtcNow,
                LastInspectionUtc = DateTime.UtcNow,
                DotExpirationUtc = DateTime.UtcNow.AddMonths(12)
            };

            EnsureRandomTarget(truck);
            Trucks.Add(truck);
            SelectedTruck = truck;
            Save();
        }

        private void MarkServiced()
        {
            if (SelectedTruck == null) return;

            SelectedTruck.LastServiceUtc = DateTime.UtcNow;
            SelectedTruck.CurrentIssue = "";
            SelectedTruck.CurrentIssueSeverity = "";
            SelectedTruck.OutOfService = false;
            SelectedTruck.ConditionPercent = Math.Max(SelectedTruck.ConditionPercent, 90);

            foreach (var report in SelectedTruck.DamageReports.Where(d => !d.Resolved))
                report.Resolved = true;

            SelectedTruck.ServiceHistory.Add(new VtcServiceRecord
            {
                ServiceType = "Scheduled Service",
                Notes = "Service marked complete from VTC Maintenance.",
                OdometerMiles = SelectedTruck.OdometerMiles,
                CompletedBy = EldDriverIdentityResolver.DriverName()
            });

            ResetRandomTarget(SelectedTruck);
            RebuildAlerts();
            Save();
        }

        private void AddInspection()
        {
            if (SelectedTruck == null) return;

            SelectedTruck.LastInspectionUtc = DateTime.UtcNow;
            SelectedTruck.DotExpirationUtc = DateTime.UtcNow.AddMonths(12);
            SelectedTruck.ServiceHistory.Add(new VtcServiceRecord
            {
                ServiceType = "DOT Inspection",
                Notes = "DOT inspection completed.",
                OdometerMiles = SelectedTruck.OdometerMiles,
                CompletedBy = EldDriverIdentityResolver.DriverName()
            });

            RebuildAlerts();
            Save();
        }

        private void SetOutOfService(bool outOfService)
        {
            if (SelectedTruck == null) return;

            SelectedTruck.OutOfService = outOfService;
            SelectedTruck.ServiceHistory.Add(new VtcServiceRecord
            {
                ServiceType = outOfService ? "Removed From Service" : "Returned To Active",
                Notes = outOfService ? "Truck removed from active fleet." : "Truck returned to active fleet.",
                OdometerMiles = SelectedTruck.OdometerMiles,
                CompletedBy = EldDriverIdentityResolver.DriverName()
            });

            RebuildAlerts();
            Save();
        }

        private void AddDamageReport()
        {
            if (SelectedTruck == null) return;

            SelectedTruck.DamageReports.Add(new VtcDamageReport
            {
                Severity = SelectedTruck.ConditionPercent <= 65 ? "Critical" : "Warning",
                ReportedBy = EldDriverIdentityResolver.DriverName(),
                Notes = "Damage/maintenance concern reported from VTC Maintenance."
            });

            RebuildAlerts();
            Save();
        }

        private void ResolveDamage()
        {
            if (SelectedTruck == null) return;

            foreach (var report in SelectedTruck.DamageReports.Where(d => !d.Resolved))
                report.Resolved = true;

            SelectedTruck.CurrentIssue = "";
            SelectedTruck.CurrentIssueSeverity = "";
            SelectedTruck.OutOfService = false;
            SelectedTruck.ConditionPercent = Math.Max(SelectedTruck.ConditionPercent, 90);

            SelectedTruck.ServiceHistory.Add(new VtcServiceRecord
            {
                ServiceType = "Damage Resolved",
                Notes = "Open damage reports resolved.",
                OdometerMiles = SelectedTruck.OdometerMiles,
                CompletedBy = EldDriverIdentityResolver.DriverName()
            });

            ResetRandomTarget(SelectedTruck);
            RebuildAlerts();
            Save();
        }

        private void ApplySelectedIssue()
        {
            if (SelectedTruck == null || string.IsNullOrWhiteSpace(SelectedIssue)) return;
            ApplyIssueToTruck(SelectedTruck, SelectedIssue, GetSeverityForIssue(SelectedIssue), false);
        }

        private void TriggerRandomMalfunction()
        {
            if (SelectedTruck == null || CommonIssues.Count == 0) return;
            var issue = CommonIssues[_random.Next(CommonIssues.Count)];
            ApplyIssueToTruck(SelectedTruck, issue, GetSeverityForIssue(issue), true);
        }

        private void ClearCurrentIssue()
        {
            if (SelectedTruck == null) return;

            SelectedTruck.CurrentIssue = "";
            SelectedTruck.CurrentIssueSeverity = "";

            foreach (var report in SelectedTruck.DamageReports.Where(x => !x.Resolved))
                report.Resolved = true;

            SelectedTruck.ServiceHistory.Add(new VtcServiceRecord
            {
                ServiceType = "Malfunction Cleared",
                Notes = "Current truck malfunction cleared.",
                OdometerMiles = SelectedTruck.OdometerMiles,
                CompletedBy = EldDriverIdentityResolver.DriverName()
            });

            ResetRandomTarget(SelectedTruck);
            RebuildAlerts();
            Save();
        }

        private void ApplyIssueToTruck(VtcMaintenanceTruck truck, string issue, string severity, bool simulated)
        {
            truck.CurrentIssue = issue;
            truck.CurrentIssueSeverity = severity;

            if (severity.Equals("Critical", StringComparison.OrdinalIgnoreCase))
            {
                truck.ConditionPercent = Math.Min(truck.ConditionPercent, 65);
                truck.OutOfService = true;
            }
            else
            {
                truck.ConditionPercent = Math.Min(truck.ConditionPercent, 82);
            }

            truck.DamageReports.Add(new VtcDamageReport
            {
                Severity = severity,
                ReportedBy = simulated ? "OverWatch ELD Simulator" : EldDriverIdentityResolver.DriverName(),
                Notes = simulated ? $"Simulated road malfunction: {issue}" : issue
            });

            truck.ServiceHistory.Add(new VtcServiceRecord
            {
                ServiceType = simulated ? "Simulated Vehicle Malfunction" : "Vehicle Malfunction",
                Notes = $"{severity}: {issue}",
                OdometerMiles = truck.OdometerMiles,
                CompletedBy = simulated ? "OverWatch ELD Simulator" : EldDriverIdentityResolver.DriverName()
            });

            ResetRandomTarget(truck);
            RebuildAlerts();

            try { MaintenanceMalfunctionAlertService.Raise(truck.UnitNumber, truck.TruckName, issue, severity); } catch { }

            Save();
        }

        private void EnsureRandomTarget(VtcMaintenanceTruck truck)
        {
            if (truck.RandomMalfunctionTargetOdometerMiles <= truck.OdometerMiles)
                ResetRandomTarget(truck);
        }

        private void ResetRandomTarget(VtcMaintenanceTruck truck)
        {
            var milesUntilNext = _random.Next(500, 701);
            truck.RandomMalfunctionStartOdometerMiles = truck.OdometerMiles;
            truck.RandomMalfunctionTargetOdometerMiles = truck.OdometerMiles + milesUntilNext;
        }

        private static string GetSeverityForIssue(string issue)
        {
            issue = (issue ?? "").Trim();

            if (issue.Contains("Brake", StringComparison.OrdinalIgnoreCase) ||
                issue.Contains("Fuel Leak", StringComparison.OrdinalIgnoreCase) ||
                issue.Contains("Overheating", StringComparison.OrdinalIgnoreCase) ||
                issue.Contains("Blown Air Line", StringComparison.OrdinalIgnoreCase) ||
                issue.Contains("Transmission", StringComparison.OrdinalIgnoreCase))
                return "Critical";

            return "Warning";
        }

        private void RebuildAlerts()
        {
            Alerts.Clear();

            if (_state.RandomMalfunctionsEnabled)
                Alerts.Add(new VtcMaintenanceAlert
                {
                    Severity = "Info",
                    Message = "Random malfunction simulator enabled."
                });

            foreach (var truck in Trucks)
            {
                // DO NOT show maintenance requests in alerts
                if (string.Equals(truck.CurrentIssueSeverity, "Request", StringComparison.OrdinalIgnoreCase))
                {
                    truck.CurrentIssue = "";
                    truck.CurrentIssueSeverity = "";
                    continue;
                }

                if (truck.OutOfService)
                    Alerts.Add(new VtcMaintenanceAlert
                    {
                        TruckId = truck.TruckId,
                        UnitNumber = truck.UnitNumber,
                        Severity = "Critical",
                        Message = $"Truck {truck.UnitNumber} is out of service."
                    });

                if (!string.IsNullOrWhiteSpace(truck.CurrentIssue))
                    Alerts.Add(new VtcMaintenanceAlert
                    {
                        TruckId = truck.TruckId,
                        UnitNumber = truck.UnitNumber,
                        Severity = truck.CurrentIssueSeverity,
                        Message = $"{truck.UnitNumber}: {truck.CurrentIssue}"
                    });

                if (IsServiceDue(truck))
                    Alerts.Add(new VtcMaintenanceAlert
                    {
                        TruckId = truck.TruckId,
                        UnitNumber = truck.UnitNumber,
                        Severity = "Warning",
                        Message = $"Truck {truck.UnitNumber} service is due."
                    });

                if (IsDotExpiringSoon(truck))
                    Alerts.Add(new VtcMaintenanceAlert
                    {
                        TruckId = truck.TruckId,
                        UnitNumber = truck.UnitNumber,
                        Severity = "Warning",
                        Message = $"Truck {truck.UnitNumber} DOT inspection is due/expiring soon."
                    });
            }
        }

        private static bool IsServiceDue(VtcMaintenanceTruck truck) =>
            truck.LastServiceUtc == null || truck.LastServiceUtc.Value <= DateTime.UtcNow.AddDays(-30);

        private static bool IsDotExpiringSoon(VtcMaintenanceTruck truck) =>
            truck.DotExpirationUtc == null || truck.DotExpirationUtc.Value <= DateTime.UtcNow.AddDays(30);

        private void RefreshStats()
        {
            OnPropertyChanged(nameof(TotalTrucks));
            OnPropertyChanged(nameof(ActiveTrucks));
            OnPropertyChanged(nameof(OutOfServiceTrucks));
            OnPropertyChanged(nameof(ServiceDueTrucks));
            OnPropertyChanged(nameof(DotExpiringSoon));
            OnPropertyChanged(nameof(CriticalDamage));
            OnPropertyChanged(nameof(AverageCondition));
            OnPropertyChanged(nameof(AverageFuel));
            OnPropertyChanged(nameof(FleetReadiness));
            OnPropertyChanged(nameof(OpenRequestCount));
            OnPropertyChanged(nameof(FixedRequestCount));
            OnPropertyChanged(nameof(SelectedTruckStatus));
            OnPropertyChanged(nameof(SelectedTruckStatusBrush));
            OnPropertyChanged(nameof(RandomMalfunctionProgressValue));
            OnPropertyChanged(nameof(RandomMalfunctionProgressText));
            OnPropertyChanged(nameof(CurrentDriverTruckDisplay));
            OnPropertyChanged(nameof(CurrentDriverIssueDisplay));
        }

        private static string FirstNonBlank(params string?[] values)
        {
            foreach (var v in values)
                if (!string.IsNullOrWhiteSpace(v))
                    return v.Trim();

            return "";
        }

        private static bool Same(string? a, string? b) =>
            !string.IsNullOrWhiteSpace(a) &&
            !string.IsNullOrWhiteSpace(b) &&
            string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}