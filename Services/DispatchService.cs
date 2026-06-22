using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public static class DispatchService
    {
        public static ObservableCollection<DispatchJob> Jobs { get; } = new();
        public static ObservableCollection<string> Drivers { get; } = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private static string DataFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OverWatchELD");

        private static string JobsFilePath => Path.Combine(DataFolder, "dispatch_jobs.json");

        static DispatchService()
        {
            if (Drivers.Count == 0)
            {
                Drivers.Add("Unassigned");
                Drivers.Add("BamBam");
                Drivers.Add("User");
            }

            LoadJobs();
        }

        public static void AddJob(DispatchJob job)
        {
            if (job == null) return;

            if (string.IsNullOrWhiteSpace(job.Id))
                job.Id = Guid.NewGuid().ToString("N");

            if (job.CreatedUtc == default)
                job.CreatedUtc = DateTime.UtcNow;

            if (job.PostedUtc == default)
                job.PostedUtc = DateTime.UtcNow;

            if (string.IsNullOrWhiteSpace(job.DispatchMode))
                job.DispatchMode = "Open";

            job.UpdatedUtc = DateTime.UtcNow;
            job.IsOverdue = CalculateIsOverdue(job);

            if (!Jobs.Any(x => string.Equals(x.Id, job.Id, StringComparison.OrdinalIgnoreCase)))
                Jobs.Add(job);

            SaveJobs();
        }

        public static void UpdateJob(DispatchJob job)
        {
            if (job == null) return;

            var existing = Jobs.FirstOrDefault(x => string.Equals(x.Id, job.Id, StringComparison.OrdinalIgnoreCase));
            if (existing == null) return;

            existing.LoadNumber = job.LoadNumber;
            existing.Company = job.Company;
            existing.OriginCity = job.OriginCity;
            existing.OriginState = job.OriginState;
            existing.DestinationCity = job.DestinationCity;
            existing.DestinationState = job.DestinationState;
            existing.Miles = job.Miles;
            existing.Cargo = job.Cargo;
            existing.Trailer = job.Trailer;
            existing.AssignedDriver = job.AssignedDriver;
            existing.AssignedTruck = job.AssignedTruck;
            existing.Status = job.Status;
            existing.Notes = job.Notes;

            existing.PickupDate = job.PickupDate;
            existing.DeliveryDeadline = job.DeliveryDeadline;
            existing.Payout = job.Payout;
            existing.RevenueUsd = job.RevenueUsd;
            existing.RatePerMile = job.RatePerMile;
            existing.RevenueCapturedUtc = job.RevenueCapturedUtc;
            existing.RevenueSource = job.RevenueSource;
            existing.CargoWeight = job.CargoWeight;
            existing.ActualCargoWeightLbs = job.ActualCargoWeightLbs;
            existing.StartOdometerMiles = job.StartOdometerMiles;
            existing.ActualDrivenMiles = job.ActualDrivenMiles;

            existing.Priority = job.Priority;
            existing.TrailerOwner = job.TrailerOwner;
            existing.IsConvoyLoad = job.IsConvoyLoad;
            existing.ConvoyName = job.ConvoyName;
            existing.AutoFleetSync = job.AutoFleetSync;

            existing.AcceptedUtc = job.AcceptedUtc;
            existing.PickedUpUtc = job.PickedUpUtc;
            existing.InTransitUtc = job.InTransitUtc;
            existing.DeliveredUtc = job.DeliveredUtc;
            existing.CancelledUtc = job.CancelledUtc;
            existing.LastStatusChangeUtc = job.LastStatusChangeUtc;
            existing.LastKnownLocation = job.LastKnownLocation;

            existing.DispatchMode = job.DispatchMode;
            existing.ClaimedBy = job.ClaimedBy;
            existing.ClaimedUtc = job.ClaimedUtc;
            existing.PostedBy = job.PostedBy;
            existing.PostedUtc = job.PostedUtc;
            existing.IsClaimLocked = job.IsClaimLocked;

            existing.LastKnownTruckName = job.LastKnownTruckName;
            existing.LastKnownPlateNumber = job.LastKnownPlateNumber;
            existing.LastKnownOdometerMiles = job.LastKnownOdometerMiles;
            existing.DestinationReachedUtc = job.DestinationReachedUtc;
            existing.DeliveryDiscordSentUtcText = job.DeliveryDiscordSentUtcText;

            existing.UpdatedUtc = DateTime.UtcNow;
            existing.IsOverdue = CalculateIsOverdue(existing);

            SaveJobs();
        }

        public static void DeleteJob(DispatchJob job)
        {
            if (job == null) return;
            Jobs.Remove(job);
            SaveJobs();
        }

        public static string NextLoadNumber()
        {
            return $"LD-{DateTime.Now:MMddHHmmss}";
        }

        public static void SendToDriver(DispatchJob job)
        {
            if (job == null) return;

            if (!string.IsNullOrWhiteSpace(job.AssignedDriver) &&
                !job.AssignedDriver.Equals("Unassigned", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(job.Status) ||
                    job.Status.Equals("Available", StringComparison.OrdinalIgnoreCase))
                {
                    job.Status = "Assigned";
                }

                if (string.IsNullOrWhiteSpace(job.DispatchMode))
                    job.DispatchMode = "Assigned";

                if (string.IsNullOrWhiteSpace(job.ClaimedBy))
                {
                    job.ClaimedBy = job.AssignedDriver;
                    job.ClaimedUtc ??= DateTime.UtcNow;
                    job.IsClaimLocked = true;
                }

                job.LastStatusChangeUtc ??= DateTime.UtcNow;
                job.UpdatedUtc = DateTime.UtcNow;
                job.IsOverdue = CalculateIsOverdue(job);

                SaveJobs();
            }
        }

        public static bool ClaimJob(DispatchJob job, string driverName)
        {
            if (job == null) return false;

            var driver = (driverName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(driver) ||
                driver.Equals("Unassigned", StringComparison.OrdinalIgnoreCase))
                return false;

            if (job.IsClaimLocked)
                return false;

            if (!string.IsNullOrWhiteSpace(job.ClaimedBy))
                return false;

            job.ClaimedBy = driver;
            job.ClaimedUtc = DateTime.UtcNow;
            job.IsClaimLocked = true;

            job.AssignedDriver = driver;
            job.DispatchMode = "Assigned";
            job.Status = "Accepted";
            job.AcceptedUtc ??= DateTime.UtcNow;
            job.LastStatusChangeUtc = DateTime.UtcNow;
            job.UpdatedUtc = DateTime.UtcNow;
            job.IsOverdue = CalculateIsOverdue(job);

            SaveJobs();
            return true;
        }

        public static void AcceptJob(DispatchJob job)
        {
            if (job == null) return;

            job.Status = "Accepted";
            job.AcceptedUtc ??= DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(job.AssignedDriver) &&
                !job.AssignedDriver.Equals("Unassigned", StringComparison.OrdinalIgnoreCase))
            {
                job.DispatchMode = "Assigned";
                if (string.IsNullOrWhiteSpace(job.ClaimedBy))
                {
                    job.ClaimedBy = job.AssignedDriver;
                    job.ClaimedUtc ??= DateTime.UtcNow;
                }
                job.IsClaimLocked = true;
            }

            job.LastStatusChangeUtc = DateTime.UtcNow;
            job.UpdatedUtc = DateTime.UtcNow;
            job.IsOverdue = CalculateIsOverdue(job);

            SaveJobs();
        }

        public static void MarkPickedUp(DispatchJob job)
        {
            if (job == null) return;

            job.Status = "Picked Up";
            job.PickedUpUtc ??= DateTime.UtcNow;
            if ((job.RevenueUsd > 0 || job.Payout > 0) && job.RevenueCapturedUtc == null)
                job.RevenueCapturedUtc = DateTime.UtcNow;
            job.LastStatusChangeUtc = DateTime.UtcNow;
            job.UpdatedUtc = DateTime.UtcNow;
            job.IsOverdue = CalculateIsOverdue(job);

            SaveJobs();
        }

        public static void MarkInTransit(DispatchJob job)
        {
            if (job == null) return;

            job.Status = "In Transit";
            job.InTransitUtc ??= DateTime.UtcNow;
            job.LastStatusChangeUtc = DateTime.UtcNow;
            job.UpdatedUtc = DateTime.UtcNow;
            job.IsOverdue = CalculateIsOverdue(job);

            SaveJobs();
        }

        public static void MarkDelivered(DispatchJob job)
        {
            if (job == null) return;

            job.Status = "Delivered";
            job.DeliveredUtc = DateTime.UtcNow;
            job.LastStatusChangeUtc = DateTime.UtcNow;
            job.UpdatedUtc = DateTime.UtcNow;
            job.IsOverdue = false;

            SaveJobs();
        }

        public static void MarkCancelled(DispatchJob job)
        {
            if (job == null) return;

            job.Status = "Cancelled";
            job.CancelledUtc = DateTime.UtcNow;
            job.LastStatusChangeUtc = DateTime.UtcNow;
            job.UpdatedUtc = DateTime.UtcNow;
            job.IsOverdue = false;

            SaveJobs();
        }

        public static bool CalculateIsOverdue(DispatchJob job)
        {
            if (job == null) return false;

            if (job.Status.Equals("Delivered", StringComparison.OrdinalIgnoreCase) ||
                job.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
                return false;

            return job.DeliveryDeadline.HasValue &&
                   job.DeliveryDeadline.Value < DateTime.Now;
        }

        public static void RefreshOverdueFlags()
        {
            foreach (var job in Jobs)
                job.IsOverdue = CalculateIsOverdue(job);
        }

        public static int ActiveLoadsCount()
        {
            RefreshOverdueFlags();
            return Jobs.Count(x =>
                !x.Status.Equals("Delivered", StringComparison.OrdinalIgnoreCase) &&
                !x.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase));
        }

        public static int InTransitCount()
        {
            RefreshOverdueFlags();
            return Jobs.Count(x => x.Status.Equals("In Transit", StringComparison.OrdinalIgnoreCase));
        }

        public static int DeliveredTodayCount()
        {
            var today = DateTime.Today;
            return Jobs.Count(x =>
                x.DeliveredUtc.HasValue &&
                x.DeliveredUtc.Value.Date == today);
        }

        public static int OverdueCount()
        {
            RefreshOverdueFlags();
            return Jobs.Count(x => x.IsOverdue);
        }

        public static int UnassignedCount()
        {
            RefreshOverdueFlags();
            return Jobs.Count(x =>
                string.IsNullOrWhiteSpace(x.AssignedDriver) ||
                x.AssignedDriver.Equals("Unassigned", StringComparison.OrdinalIgnoreCase));
        }

        public static int ActiveDriversCount()
        {
            RefreshOverdueFlags();

            return Jobs
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.AssignedDriver) &&
                    !x.AssignedDriver.Equals("Unassigned", StringComparison.OrdinalIgnoreCase) &&
                    !x.Status.Equals("Delivered", StringComparison.OrdinalIgnoreCase) &&
                    !x.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.AssignedDriver.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
        }

        public static DispatchMemberStats GetMemberStats(string driverName)
        {
            var name = (driverName ?? "").Trim();
            var stats = new DispatchMemberStats { DriverName = string.IsNullOrWhiteSpace(name) ? "Unknown" : name };

            if (string.IsNullOrWhiteSpace(name))
                return stats;

            RefreshOverdueFlags();

            var mine = Jobs.Where(x =>
                    string.Equals((x.AssignedDriver ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals((x.ClaimedBy ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            stats.ActiveLoads = mine.Count(x =>
                !x.Status.Equals("Delivered", StringComparison.OrdinalIgnoreCase) &&
                !x.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase));

            stats.CompletedLoads = mine.Count(x => x.Status.Equals("Delivered", StringComparison.OrdinalIgnoreCase));
            stats.OverdueLoads = mine.Count(x => x.IsOverdue);
            stats.TotalMiles = mine.Where(x => x.Status.Equals("Delivered", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Miles);
            stats.TotalPayout = mine
                .Where(x => x.Status.Equals("Delivered", StringComparison.OrdinalIgnoreCase))
                .Sum(x => x.BestRevenue);

            var completed = mine.Where(x => x.Status.Equals("Delivered", StringComparison.OrdinalIgnoreCase)).ToList();
            if (completed.Count > 0)
            {
                var onTime = completed.Count(x => !x.DeliveryDeadline.HasValue || (x.DeliveredUtc.HasValue && x.DeliveredUtc.Value <= x.DeliveryDeadline.Value));
                stats.OnTimePercent = Math.Round((double)onTime / completed.Count * 100.0, 1);
            }

            return stats;
        }

        public static DispatchJob? GetCurrentActiveJobForDriver(string driverName)
        {
            var name = (driverName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
                return null;

            RefreshOverdueFlags();

            return Jobs
                .Where(x =>
                    (string.Equals((x.AssignedDriver ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals((x.ClaimedBy ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase)) &&
                    !x.Status.Equals("Delivered", StringComparison.OrdinalIgnoreCase) &&
                    !x.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.UpdatedUtc)
                .ThenByDescending(x => x.CreatedUtc)
                .FirstOrDefault();
        }

        public static List<DispatchLeaderboardEntry> GetLeaderboard()
        {
            RefreshOverdueFlags();

            var delivered = Jobs
                .Where(x => x.Status.Equals("Delivered", StringComparison.OrdinalIgnoreCase))
                .Where(x => !string.IsNullOrWhiteSpace(x.AssignedDriver) && !x.AssignedDriver.Equals("Unassigned", StringComparison.OrdinalIgnoreCase));

            var board = delivered
                .GroupBy(x => (x.AssignedDriver ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var completedLoads = g.Count();
                    var miles = g.Sum(x => x.Miles);
                    var revenue = g.Sum(x => x.BestRevenue);
                    var onTime = g.Count(x => !x.DeliveryDeadline.HasValue || (x.DeliveredUtc.HasValue && x.DeliveredUtc.Value <= x.DeliveryDeadline.Value));
                    var onTimePct = completedLoads <= 0 ? 0 : Math.Round((double)onTime / completedLoads * 100.0, 1);

                    return new DispatchLeaderboardEntry
                    {
                        DriverName = g.Key,
                        CompletedLoads = completedLoads,
                        TotalMiles = miles,
                        TotalRevenue = revenue,
                        OnTimePercent = onTimePct
                    };
                })
                .OrderByDescending(x => x.CompletedLoads)
                .ThenByDescending(x => x.TotalRevenue)
                .ThenByDescending(x => x.TotalMiles)
                .Take(10)
                .ToList();

            for (int i = 0; i < board.Count; i++)
                board[i].Rank = i + 1;

            return board;
        }

        public static List<DispatchJob> GetFilteredJobs(string search, string filter, string currentDriverName = "")
        {
            RefreshOverdueFlags();

            IEnumerable<DispatchJob> query = Jobs;
            var currentDriver = (currentDriverName ?? "").Trim();

            var q = (search ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(q))
            {
                query = query.Where(x =>
                    Contains(x.LoadNumber, q) ||
                    Contains(x.Company, q) ||
                    Contains(x.OriginCity, q) ||
                    Contains(x.OriginState, q) ||
                    Contains(x.DestinationCity, q) ||
                    Contains(x.DestinationState, q) ||
                    Contains(x.Cargo, q) ||
                    Contains(x.Trailer, q) ||
                    Contains(x.AssignedDriver, q) ||
                    Contains(x.AssignedTruck, q) ||
                    Contains(x.Status, q) ||
                    Contains(x.ClaimedBy, q) ||
                    Contains(x.PostedBy, q) ||
                    Contains(x.Notes, q));
            }

            switch ((filter ?? "All").Trim())
            {
                case "Open Board":
                    query = query.Where(x =>
                        x.DispatchMode.Equals("Open", StringComparison.OrdinalIgnoreCase) &&
                        string.IsNullOrWhiteSpace(x.ClaimedBy) &&
                        !x.IsClaimLocked &&
                        !x.Status.Equals("Delivered", StringComparison.OrdinalIgnoreCase) &&
                        !x.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase));
                    break;

                case "My Loads":
                    if (!string.IsNullOrWhiteSpace(currentDriver))
                    {
                        query = query.Where(x =>
                            string.Equals((x.AssignedDriver ?? "").Trim(), currentDriver, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals((x.ClaimedBy ?? "").Trim(), currentDriver, StringComparison.OrdinalIgnoreCase));
                    }
                    else
                    {
                        query = Enumerable.Empty<DispatchJob>();
                    }
                    break;

                case "Available":
                    query = query.Where(x => x.Status.Equals("Available", StringComparison.OrdinalIgnoreCase));
                    break;

                case "Assigned":
                    query = query.Where(x => x.Status.Equals("Assigned", StringComparison.OrdinalIgnoreCase));
                    break;

                case "Accepted":
                    query = query.Where(x => x.Status.Equals("Accepted", StringComparison.OrdinalIgnoreCase));
                    break;

                case "Picked Up":
                    query = query.Where(x => x.Status.Equals("Picked Up", StringComparison.OrdinalIgnoreCase));
                    break;

                case "In Transit":
                    query = query.Where(x => x.Status.Equals("In Transit", StringComparison.OrdinalIgnoreCase));
                    break;

                case "Delivered":
                case "History":
                    query = query.Where(x => x.Status.Equals("Delivered", StringComparison.OrdinalIgnoreCase));
                    break;

                case "Cancelled":
                    query = query.Where(x => x.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase));
                    break;

                case "Overdue":
                    query = query.Where(x => x.IsOverdue);
                    break;

                case "Unassigned":
                    query = query.Where(x =>
                        string.IsNullOrWhiteSpace(x.AssignedDriver) ||
                        x.AssignedDriver.Equals("Unassigned", StringComparison.OrdinalIgnoreCase));
                    break;

                case "Claimed":
                    query = query.Where(x => !string.IsNullOrWhiteSpace(x.ClaimedBy));
                    break;
            }

            return query
                .OrderByDescending(x => x.IsOverdue)
                .ThenByDescending(x => x.UpdatedUtc)
                .ThenBy(x => x.LoadNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static void ApplyTelemetryRevenue(DispatchJob job, TelemetrySnapshot snapshot)
        {
            if (job == null || snapshot == null)
                return;

            var revenue = ParseRevenue(snapshot.RevenueDisplay);
            if (revenue <= 0)
                return;

            if (job.RevenueUsd <= 0)
                job.RevenueUsd = revenue;

            if (job.Payout <= 0)
                job.Payout = revenue;

            if (job.Miles <= 0 && snapshot.PlannedMiles.HasValue && snapshot.PlannedMiles.Value > 0)
                job.Miles = (int)Math.Round(snapshot.PlannedMiles.Value);

            var bestRevenue = job.RevenueUsd > 0 ? job.RevenueUsd : job.Payout;
            job.RatePerMile = job.Miles > 0 && bestRevenue > 0
                ? Math.Round(bestRevenue / Math.Max(1, job.Miles), 2)
                : job.RatePerMile;

            job.RevenueCapturedUtc ??= DateTime.UtcNow;
            job.RevenueSource = string.IsNullOrWhiteSpace(snapshot.RevenueDisplay)
                ? "ATS Telemetry"
                : "ATS Telemetry: " + snapshot.RevenueDisplay.Trim();

            job.UpdatedUtc = DateTime.UtcNow;
        }

        private static decimal ParseRevenue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0;

            var chars = new List<char>();
            foreach (var c in value)
            {
                if (char.IsDigit(c) || c == '.' || c == '-')
                    chars.Add(c);
            }

            var cleaned = new string(chars.ToArray());

            return decimal.TryParse(
                cleaned,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var result)
                    ? result
                    : 0;
        }

        public static void SaveJobs()
        {
            try
            {
                Directory.CreateDirectory(DataFolder);
                var list = Jobs.ToList();
                var json = JsonSerializer.Serialize(list, JsonOptions);
                File.WriteAllText(JobsFilePath, json);
            }
            catch
            {
            }
        }

        public static void LoadJobs()
        {
            try
            {
                Directory.CreateDirectory(DataFolder);

                if (!File.Exists(JobsFilePath))
                    return;

                var json = File.ReadAllText(JobsFilePath);
                if (string.IsNullOrWhiteSpace(json))
                    return;

                var list = JsonSerializer.Deserialize<List<DispatchJob>>(json, JsonOptions);
                if (list == null || list.Count == 0)
                    return;

                Jobs.Clear();

                foreach (var job in list)
                {
                    if (string.IsNullOrWhiteSpace(job.Id))
                        job.Id = Guid.NewGuid().ToString("N");

                    if (job.PostedUtc == default)
                        job.PostedUtc = job.CreatedUtc == default ? DateTime.UtcNow : job.CreatedUtc;

                    if (string.IsNullOrWhiteSpace(job.DispatchMode))
                        job.DispatchMode = "Open";

                    job.IsOverdue = CalculateIsOverdue(job);
                    Jobs.Add(job);
                }
            }
            catch
            {
            }
        }

        private static bool Contains(string source, string value)
        {
            return (source ?? "").IndexOf(value ?? "", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static void UpdateJob(object savedJob)
        {
            throw new NotImplementedException();
        }
    }

    public sealed class DispatchMemberStats
    {
        public string DriverName { get; set; } = "";
        public int ActiveLoads { get; set; }
        public int CompletedLoads { get; set; }
        public int OverdueLoads { get; set; }
        public int TotalMiles { get; set; }
        public decimal TotalPayout { get; set; }
        public double OnTimePercent { get; set; }
    }

    public sealed class DispatchLeaderboardEntry
    {
        public int Rank { get; set; }
        public string DriverName { get; set; } = "";
        public int CompletedLoads { get; set; }
        public int TotalMiles { get; set; }
        public decimal TotalRevenue { get; set; }
        public double OnTimePercent { get; set; }

        public string RevenueDisplay => TotalRevenue <= 0 ? "$0.00" : TotalRevenue.ToString("C2");
        public string OnTimeDisplay => $"{OnTimePercent:0.#}%";
    }

    public sealed class DispatchJob
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string LoadNumber { get; set; } = DispatchService.NextLoadNumber();

        public string Company { get; set; } = "";
        public string OriginCity { get; set; } = "";
        public string OriginState { get; set; } = "";
        public string DestinationCity { get; set; } = "";
        public string DestinationState { get; set; } = "";

        public int Miles { get; set; }
        public string Cargo { get; set; } = "";
        public string Trailer { get; set; } = "";

        public string AssignedDriver { get; set; } = "Unassigned";
        public string AssignedTruck { get; set; } = "";
        public string Status { get; set; } = "Available";
        public string Notes { get; set; } = "";

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        public DateTime? PickupDate { get; set; }
        public DateTime? DeliveryDeadline { get; set; }

        public decimal Payout { get; set; }
        public decimal RevenueUsd { get; set; }
        public decimal RatePerMile { get; set; }
        public DateTime? RevenueCapturedUtc { get; set; }
        public string RevenueSource { get; set; } = "";
        public double CargoWeight { get; set; }
        public double ActualCargoWeightLbs { get; set; }
        public double? StartOdometerMiles { get; set; }
        public double ActualDrivenMiles { get; set; }

        public string Priority { get; set; } = "Normal";
        public string TrailerOwner { get; set; } = "Company";
        public bool IsConvoyLoad { get; set; }
        public string ConvoyName { get; set; } = "";
        public bool AutoFleetSync { get; set; } = true;

        public DateTime? AcceptedUtc { get; set; }
        public DateTime? PickedUpUtc { get; set; }
        public DateTime? InTransitUtc { get; set; }
        public DateTime? DeliveredUtc { get; set; }
        public DateTime? CancelledUtc { get; set; }
        public DateTime? LastStatusChangeUtc { get; set; }
        public string LastKnownLocation { get; set; } = "";
        public bool IsOverdue { get; set; }

        public string DispatchMode { get; set; } = "Open";
        public string ClaimedBy { get; set; } = "";
        public DateTime? ClaimedUtc { get; set; }
        public string PostedBy { get; set; } = "";
        public DateTime PostedUtc { get; set; } = DateTime.UtcNow;
        public bool IsClaimLocked { get; set; }

        public string LastKnownTruckName { get; set; } = "";
        public string LastKnownPlateNumber { get; set; } = "";
        public double LastKnownOdometerMiles { get; set; }
        public DateTime? DestinationReachedUtc { get; set; }
        public string DeliveryDiscordSentUtcText { get; set; } = "";

        public string OriginDisplay => $"{OriginCity}, {OriginState}".Trim(' ', ',');
        public string DestinationDisplay => $"{DestinationCity}, {DestinationState}".Trim(' ', ',');
        public string RouteDisplay => $"{OriginDisplay} → {DestinationDisplay}";
        public decimal BestRevenue => RevenueUsd > 0 ? RevenueUsd : Payout;
        public string PayoutDisplay => Payout <= 0 ? "--" : Payout.ToString("C0");
        public string RevenueUsdDisplay => BestRevenue <= 0 ? "$0.00" : BestRevenue.ToString("C2");
        public string RatePerMileDisplay => RatePerMile > 0 ? RatePerMile.ToString("C2") + "/mi" : "--";
        public string RevenueCapturedDisplay => RevenueCapturedUtc?.ToLocalTime().ToString("MM/dd/yyyy HH:mm") ?? "--";
        public string RevenueSourceDisplay => string.IsNullOrWhiteSpace(RevenueSource) ? "--" : RevenueSource;
        public string ActualCargoWeightDisplay => ActualCargoWeightLbs <= 0 ? "--" : $"{ActualCargoWeightLbs:N0} lbs";
        public string ActualDrivenMilesDisplay => ActualDrivenMiles <= 0 ? "0.0" : ActualDrivenMiles.ToString("N1");
        public string DeadlineDisplay => DeliveryDeadline?.ToString("MM/dd/yyyy HH:mm") ?? "--";
        public string TruckDisplay => string.IsNullOrWhiteSpace(AssignedTruck) ? "Unassigned" : AssignedTruck;
        public string UpdatedDisplay => UpdatedUtc.ToLocalTime().ToString("MM/dd/yyyy HH:mm");
        public string PostedDisplay => PostedUtc.ToLocalTime().ToString("MM/dd/yyyy HH:mm");
        public string ClaimedDisplay => ClaimedUtc?.ToLocalTime().ToString("MM/dd/yyyy HH:mm") ?? "--";
    }
}