using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using OverWatchELD.ViewModels;

namespace OverWatchELD.Services.Vtc
{
    public sealed class VtcDriverProfileService
    {
        private static readonly JsonSerializerOptions JsonReadOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public sealed class DriverProfileRecord
        {
            public string Driver { get; set; } = "";
            public string Subtitle { get; set; } = "";
            public string DiscordUserId { get; set; } = "";
            public string DriverKey { get; set; } = "";
            public string Role { get; set; } = "";
            public string Status { get; set; } = "";
            public string Truck { get; set; } = "";
            public string Location { get; set; } = "";
            public string LastSeen { get; set; } = "";

            public string WeeklyMiles { get; set; } = "0";
            public string MonthlyMiles { get; set; } = "0";
            public string LoadsCompleted { get; set; } = "0";
            public string InspectionCount { get; set; } = "0";
            public string SafeScore { get; set; } = "--";

            public string ServiceDue { get; set; } = "";
            public string InspectionStatus { get; set; } = "";
            public string Health { get; set; } = "";
            public string Notes { get; set; } = "";

            public Brush StatusBrush { get; set; } = Brushes.Gray;
            public Brush HealthBrush { get; set; } = Brushes.Gray;

            public List<ActivityRecord> Activity { get; set; } = new();
        }

        public sealed class ActivityRecord
        {
            public string Message { get; set; } = "";
            public string TimeDisplay { get; set; } = "";
        }

        private sealed class DriverStatsData
        {
            public string DriverKey { get; set; } = "";
            public double WeeklyMiles { get; set; }
            public double MonthlyMiles { get; set; }
            public int LoadsCompleted { get; set; }
            public int InspectionCount { get; set; }
            public int? SafeScore { get; set; }
        }

        private sealed class FleetData
        {
            public string TruckKey { get; set; } = "";
            public int? MilesUntilService { get; set; }
            public bool? InspectionPassed { get; set; }
            public bool? InspectionOverdue { get; set; }
            public bool? NeedsRepair { get; set; }
            public string InspectionText { get; set; } = "";
            public string Status { get; set; } = "";
        }

        private sealed class ActivityData
        {
            public string DriverKey { get; set; } = "";
            public string Message { get; set; } = "";
            public string TimeDisplay { get; set; } = "";
            public DateTimeOffset SortUtc { get; set; } = DateTimeOffset.MinValue;
        }

        public DriverProfileRecord Build(VtcRosterViewModel.RosterDriverRow row)
        {
            if (row == null)
            {
                row = new VtcRosterViewModel.RosterDriverRow();
            }

            var driverName = SafeText(row.Driver, "Unknown Driver");
            var discordUserId = CleanId(row.DiscordUserId);
            var driverKey = NormalizeDriverKey(driverName, discordUserId);
            var truckKey = NormalizeTruckKey(row.Truck);

            var dispatchStats = BuildDispatchStats(driverName, discordUserId);
            var fleetMap = LoadFleetData();
            var activities = LoadActivityData();

            FleetData? fleet = null;
            if (!string.IsNullOrWhiteSpace(truckKey))
                fleetMap.TryGetValue(truckKey, out fleet);

            var status = SafeText(row.Status, "Unknown");
            var role = SafeText(row.Role, "-");
            var truck = SafeText(row.Truck, "-");
            var location = SafeText(row.Location, "-");
            var lastSeen = SafeText(row.LastSeen, "-");
            var discordIdDisplay = string.IsNullOrWhiteSpace(discordUserId) ? "-" : discordUserId;

            var weeklyMiles = dispatchStats.WeeklyMiles;
            var monthlyMiles = dispatchStats.MonthlyMiles;
            var loadsCompleted = dispatchStats.LoadsCompleted;
            var inspectionCount = dispatchStats.InspectionCount;
            var safeScore = dispatchStats.SafeScore;

            var serviceDue = BuildServiceDueText(fleet?.MilesUntilService);
            var inspectionStatus = BuildInspectionText(fleet);
            var health = BuildHealthText(fleet);

            var activityItems = activities
                .Where(a => string.Equals(a.DriverKey, driverKey, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(a => a.SortUtc)
                .ThenByDescending(a => a.TimeDisplay)
                .Take(10)
                .ToList();

            if (activityItems.Count == 0)
            {
                AddDispatchDerivedActivity(activityItems, driverKey, driverName, driverName, discordUserId, status, truck, location, lastSeen);
            }

            return new DriverProfileRecord
            {
                Driver = driverName,
                Subtitle = $"{role} • {status}",
                DiscordUserId = discordIdDisplay,
                DriverKey = driverKey,
                Role = role,
                Status = status,
                Truck = truck,
                Location = location,
                LastSeen = lastSeen,

                WeeklyMiles = weeklyMiles.ToString("N1", CultureInfo.InvariantCulture),
                MonthlyMiles = monthlyMiles.ToString("N1", CultureInfo.InvariantCulture),
                LoadsCompleted = loadsCompleted.ToString("N0", CultureInfo.InvariantCulture),
                InspectionCount = inspectionCount.ToString("N0", CultureInfo.InvariantCulture),
                SafeScore = safeScore.HasValue ? safeScore.Value.ToString("N0", CultureInfo.InvariantCulture) : "--",

                ServiceDue = serviceDue,
                InspectionStatus = inspectionStatus,
                Health = health,
                Notes = BuildNotes(driverName, role, status, truck, location, safeScore, weeklyMiles, monthlyMiles, loadsCompleted, inspectionCount, driverKey),
                StatusBrush = GetStatusBrush(status),
                HealthBrush = GetHealthBrush(health),

                Activity = activityItems
                    .Select(a => new ActivityRecord
                    {
                        Message = a.Message,
                        TimeDisplay = a.TimeDisplay
                    })
                    .ToList()
            };
        }

        private DriverStatsData BuildDispatchStats(string driverName, string? discordUserId)
        {
            var result = new DriverStatsData
            {
                DriverKey = NormalizeDriverKey(driverName, discordUserId)
            };

            try
            {
                var jobs = GetAllDispatchJobs();
                var now = DateTime.UtcNow;
                var weekCutoff = now.AddDays(-7);
                var monthCutoff = now.AddDays(-30);

                var driverJobs = jobs
                    .Where(j => JobMatchesDriver(j, driverName, discordUserId))
                    .ToList();

                result.WeeklyMiles = driverJobs
                    .Where(j => GetRelevantJobUtc(j) >= weekCutoff)
                    .Sum(GetBestMilesForJob);

                result.MonthlyMiles = driverJobs
                    .Where(j => GetRelevantJobUtc(j) >= monthCutoff)
                    .Sum(GetBestMilesForJob);

                result.LoadsCompleted = driverJobs.Count(IsDeliveredJob);
                result.InspectionCount = CountDriverInspections(driverName, discordUserId);

                var recentDelivered = driverJobs
                    .Where(IsDeliveredJob)
                    .OrderByDescending(GetRelevantJobUtc)
                    .Take(20)
                    .ToList();

                if (recentDelivered.Count > 0)
                {
                    var withMiles = recentDelivered.Count(j => GetBestMilesForJob(j) > 0);
                    var withRevenue = recentDelivered.Count(j => GetBestRevenueForJob(j) > 0);

                    var score = 70;
                    if (withMiles > 0) score += 10;
                    if (withRevenue > 0) score += 10;
                    if (recentDelivered.Count >= 5) score += 5;
                    if (recentDelivered.Count >= 10) score += 5;

                    result.SafeScore = Math.Min(score, 100);
                }
                else
                {
                    result.SafeScore = null;
                }
            }
            catch
            {
            }

            return result;
        }

        private static List<DispatchJob> GetAllDispatchJobs()
        {
            try
            {
                DispatchService.LoadJobs();
            }
            catch
            {
            }

            try
            {
                var jobsProp = typeof(DispatchService).GetProperty("Jobs",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Static);

                if (jobsProp?.GetValue(null) is IEnumerable<DispatchJob> jobsFromProp)
                    return jobsFromProp.ToList();
            }
            catch
            {
            }

            try
            {
                var getJobsMethod = typeof(DispatchService).GetMethod("GetJobs",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Static,
                    null,
                    Type.EmptyTypes,
                    null);

                if (getJobsMethod?.Invoke(null, null) is IEnumerable<DispatchJob> jobsFromMethod)
                    return jobsFromMethod.ToList();
            }
            catch
            {
            }

            return new List<DispatchJob>();
        }

        private static bool JobMatchesDriver(DispatchJob job, string driverName, string? discordUserId)
        {
            var targetName = (driverName ?? "").Trim();
            var targetDiscord = CleanId(discordUserId);

            var jobName = FirstNonEmpty(
                TryReadString(job, "DriverName"),
                TryReadString(job, "Driver"),
                TryReadString(job, "AssignedDriver"));

            var jobDiscord = CleanId(FirstNonEmpty(
                TryReadString(job, "DiscordUserId"),
                TryReadString(job, "DriverDiscordUserId"),
                TryReadString(job, "DiscordId"),
                TryReadString(job, "UserId")));

            if (!string.IsNullOrWhiteSpace(targetDiscord) &&
                string.Equals(targetDiscord, jobDiscord, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrWhiteSpace(targetName) &&
                string.Equals(targetName, jobName, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private static bool IsDeliveredJob(DispatchJob job)
        {
            var status = FirstNonEmpty(TryReadString(job, "Status"), "");

            return status.Equals("Delivered", StringComparison.OrdinalIgnoreCase) ||
                   status.Equals("Complete", StringComparison.OrdinalIgnoreCase) ||
                   status.Equals("Completed", StringComparison.OrdinalIgnoreCase);
        }

        private static double GetBestMilesForJob(DispatchJob job)
        {
            var actual = TryReadDouble(job, "ActualDrivenMiles");
            if (actual > 0) return actual;

            var planned = TryReadDouble(job, "Miles");
            if (planned > 0) return planned;

            return 0;
        }

        private static double GetBestRevenueForJob(DispatchJob job)
        {
            var revenue = TryReadDouble(job, "RevenueUsd");
            if (revenue > 0) return revenue;

            var payout = TryReadDouble(job, "Payout");
            if (payout > 0) return payout;

            return 0;
        }

        private static DateTime GetRelevantJobUtc(DispatchJob job)
        {
            var dt = TryReadDateTime(job, "DestinationReachedUtc");
            if (dt != DateTime.MinValue) return dt;

            dt = TryReadDateTime(job, "DeliveredUtc");
            if (dt != DateTime.MinValue) return dt;

            dt = TryReadDateTime(job, "UpdatedUtc");
            if (dt != DateTime.MinValue) return dt;

            dt = TryReadDateTime(job, "CreatedUtc");
            if (dt != DateTime.MinValue) return dt;

            return DateTime.MinValue;
        }

        private int CountDriverInspections(string driverName, string? discordUserId)
        {
            var driverKey = NormalizeDriverKey(driverName, discordUserId);
            var count = 0;

            foreach (var path in EnumerateInspectionFiles())
            {
                try
                {
                    if (!File.Exists(path))
                        continue;

                    var json = File.ReadAllText(path);
                    if (string.IsNullOrWhiteSpace(json))
                        continue;

                    using var doc = JsonDocument.Parse(json);
                    var arr = ExtractArray(doc.RootElement);
                    if (arr.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var el in arr.EnumerateArray())
                    {
                        var driver = FirstString(el, "driver", "driverName", "name", "displayName", "username");
                        var discord = FirstString(el, "discordUserId", "discordId", "userId", "DiscordUserId");
                        var itemKey = NormalizeDriverKey(driver, discord);

                        if (string.Equals(driverKey, itemKey, StringComparison.OrdinalIgnoreCase))
                            count++;
                    }
                }
                catch
                {
                }
            }

            return count;
        }

        private void AddDispatchDerivedActivity(
            List<ActivityData> items,
            string driverKey,
            string profileDriverName,
            string driverName,
            string? discordUserId,
            string status,
            string truck,
            string location,
            string lastSeen)
        {
            try
            {
                var jobs = GetAllDispatchJobs();

                var driverJobs = jobs
                    .Where(j => JobMatchesDriver(j, driverName, discordUserId))
                    .OrderByDescending(GetRelevantJobUtc)
                    .Take(10)
                    .ToList();

                foreach (var job in driverJobs)
                {
                    var cargo = FirstNonEmpty(TryReadString(job, "Cargo"), "--");
                    var loadNumber = FirstNonEmpty(TryReadString(job, "LoadNumber"), "--");
                    var origin = FirstNonEmpty(TryReadString(job, "OriginDisplay"), "--");
                    var destination = FirstNonEmpty(TryReadString(job, "DestinationDisplay"), "--");
                    var statusText = FirstNonEmpty(TryReadString(job, "Status"), "Updated");

                    var when = GetRelevantJobUtc(job);
                    var timeText = when == DateTime.MinValue
                        ? "Dispatch history"
                        : when.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

                    items.Add(new ActivityData
                    {
                        DriverKey = driverKey,
                        Message = $"{profileDriverName} • {statusText} • Load {loadNumber} • {cargo} • {origin} → {destination}",
                        TimeDisplay = timeText,
                        SortUtc = when == DateTime.MinValue ? DateTimeOffset.MinValue : new DateTimeOffset(DateTime.SpecifyKind(when, DateTimeKind.Utc))
                    });
                }
            }
            catch
            {
            }

            if (items.Count == 0)
            {
                items.Add(new ActivityData
                {
                    DriverKey = driverKey,
                    Message = $"{profileDriverName} currently shows status: {status}.",
                    TimeDisplay = string.IsNullOrWhiteSpace(lastSeen) || lastSeen == "-" ? "Roster snapshot" : $"Last seen {lastSeen}"
                });

                if (!string.IsNullOrWhiteSpace(truck) && truck != "-")
                {
                    items.Add(new ActivityData
                    {
                        DriverKey = driverKey,
                        Message = $"{profileDriverName} is assigned to truck {truck}.",
                        TimeDisplay = "Current roster data"
                    });
                }

                if (!string.IsNullOrWhiteSpace(location) && location != "-")
                {
                    items.Add(new ActivityData
                    {
                        DriverKey = driverKey,
                        Message = $"{profileDriverName} location is listed as {location}.",
                        TimeDisplay = "Current roster data"
                    });
                }
            }
        }

        private Dictionary<string, FleetData> LoadFleetData()
        {
            var map = new Dictionary<string, FleetData>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in EnumerateFleetFiles())
            {
                try
                {
                    if (!File.Exists(path))
                        continue;

                    var json = File.ReadAllText(path);
                    if (string.IsNullOrWhiteSpace(json))
                        continue;

                    using var doc = JsonDocument.Parse(json);
                    var arr = ExtractArray(doc.RootElement);
                    if (arr.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var el in arr.EnumerateArray())
                    {
                        var truck = FirstString(el, "truckId", "truckID", "truck", "truckNumber", "unit", "unitNumber", "name");
                        var key = NormalizeTruckKey(truck);
                        if (string.IsNullOrWhiteSpace(key))
                            continue;

                        var milesUntilService = FirstInt(el, "milesUntilService", "serviceDueMiles", "milesToService", "remainingServiceMiles");
                        if (milesUntilService == null)
                        {
                            var odometer = FirstInt(el, "odometer", "odo", "currentOdometer");
                            var nextSvc = FirstInt(el, "nextServiceOdometer", "serviceAtOdometer", "serviceDueAt");
                            if (odometer != null && nextSvc != null)
                                milesUntilService = nextSvc.Value - odometer.Value;
                        }

                        map[key] = new FleetData
                        {
                            TruckKey = key,
                            MilesUntilService = milesUntilService,
                            InspectionPassed = FirstBool(el, "inspectionPassed", "lastInspectionPassed", "isInspectionPassed"),
                            InspectionOverdue = FirstBool(el, "inspectionOverdue", "isInspectionOverdue", "inspectionDue"),
                            NeedsRepair = FirstBool(el, "needsRepair", "requiresRepair", "repairNeeded", "inRepair"),
                            InspectionText = FirstString(el, "inspectionStatus", "inspectionText", "inspection") ?? "",
                            Status = FirstString(el, "status", "truckStatus", "dutyStatus") ?? ""
                        };
                    }
                }
                catch
                {
                }
            }

            return map;
        }

        private List<ActivityData> LoadActivityData()
        {
            var items = new List<ActivityData>();

            foreach (var path in EnumerateActivityFiles())
            {
                try
                {
                    if (!File.Exists(path))
                        continue;

                    var json = File.ReadAllText(path);
                    if (string.IsNullOrWhiteSpace(json))
                        continue;

                    using var doc = JsonDocument.Parse(json);
                    var arr = ExtractArray(doc.RootElement);
                    if (arr.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var el in arr.EnumerateArray())
                    {
                        var driver = FirstString(el, "driver", "driverName", "name", "displayName", "username");
                        var discord = FirstString(el, "discordUserId", "discordId", "userId", "DiscordUserId");
                        var key = NormalizeDriverKey(driver, discord);

                        if (string.IsNullOrWhiteSpace(key))
                            continue;

                        var msg = FirstString(el, "message", "text", "activity", "description");
                        if (string.IsNullOrWhiteSpace(msg))
                            continue;

                        var when = FirstString(el, "timeDisplay", "displayTime", "when", "timestamp", "createdUtc", "updatedUtc");
                        var sort = ParseSortUtc(when);

                        items.Add(new ActivityData
                        {
                            DriverKey = key,
                            Message = msg.Trim(),
                            TimeDisplay = string.IsNullOrWhiteSpace(when) ? "Recorded activity" : NormalizeTimeDisplay(when),
                            SortUtc = sort
                        });
                    }
                }
                catch
                {
                }
            }

            return items;
        }

        private static IEnumerable<string> EnumerateFleetFiles()
        {
            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OverWatchELD");
            var baseDir = AppContext.BaseDirectory;
            var current = Environment.CurrentDirectory;

            return new[]
            {
                Path.Combine(baseDir, "fleet.json"),
                Path.Combine(baseDir, "fleet-maintenance.json"),
                Path.Combine(baseDir, "fleetMaintenance.json"),
                Path.Combine(baseDir, "fleet_trucks.json"),
                Path.Combine(baseDir, "trucks.json"),

                Path.Combine(current, "fleet.json"),
                Path.Combine(current, "fleet-maintenance.json"),
                Path.Combine(current, "fleetMaintenance.json"),
                Path.Combine(current, "fleet_trucks.json"),
                Path.Combine(current, "trucks.json"),

                Path.Combine(appData, "fleet.json"),
                Path.Combine(appData, "fleet-maintenance.json"),
                Path.Combine(appData, "fleetMaintenance.json"),
                Path.Combine(appData, "fleet_trucks.json"),
                Path.Combine(appData, "trucks.json"),
                Path.Combine(appData, "fleet_unified.json")
            }.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> EnumerateActivityFiles()
        {
            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OverWatchELD");
            var baseDir = AppContext.BaseDirectory;
            var current = Environment.CurrentDirectory;

            return new[]
            {
                Path.Combine(baseDir, "driver-activity.json"),
                Path.Combine(baseDir, "driverActivity.json"),
                Path.Combine(baseDir, "activity.json"),
                Path.Combine(baseDir, "dispatch-history.json"),
                Path.Combine(baseDir, "dispatchHistory.json"),

                Path.Combine(current, "driver-activity.json"),
                Path.Combine(current, "driverActivity.json"),
                Path.Combine(current, "activity.json"),
                Path.Combine(current, "dispatch-history.json"),
                Path.Combine(current, "dispatchHistory.json"),

                Path.Combine(appData, "driver-activity.json"),
                Path.Combine(appData, "driverActivity.json"),
                Path.Combine(appData, "activity.json"),
                Path.Combine(appData, "dispatch-history.json"),
                Path.Combine(appData, "dispatchHistory.json"),
            }.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> EnumerateInspectionFiles()
        {
            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OverWatchELD");
            var docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OverWatchELD");
            var baseDir = AppContext.BaseDirectory;
            var current = Environment.CurrentDirectory;

            return new[]
            {
                Path.Combine(baseDir, "inspections.json"),
                Path.Combine(baseDir, "inspection-history.json"),
                Path.Combine(baseDir, "inspectionHistory.json"),

                Path.Combine(current, "inspections.json"),
                Path.Combine(current, "inspection-history.json"),
                Path.Combine(current, "inspectionHistory.json"),

                Path.Combine(appData, "inspections.json"),
                Path.Combine(appData, "inspection-history.json"),
                Path.Combine(appData, "inspectionHistory.json"),

                Path.Combine(docs, "inspections.json"),
                Path.Combine(docs, "inspection-history.json"),
                Path.Combine(docs, "inspectionHistory.json")
            }.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static JsonElement ExtractArray(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Array)
                return root;

            foreach (var name in new[] { "items", "drivers", "data", "records", "activities", "trucks", "fleet", "inspections" })
            {
                if (root.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array)
                    return arr;
            }

            return default;
        }

        private static string BuildServiceDueText(int? milesUntilService)
        {
            if (milesUntilService == null)
                return "Service Due: --";

            if (milesUntilService <= 0)
                return "Service Due: Overdue";

            return $"Service Due: {milesUntilService.Value.ToString("N0", CultureInfo.InvariantCulture)} mi";
        }

        private static string BuildInspectionText(FleetData? fleet)
        {
            if (fleet != null && !string.IsNullOrWhiteSpace(fleet.InspectionText))
            {
                var text = fleet.InspectionText.Trim();
                return text.StartsWith("Inspection:", StringComparison.OrdinalIgnoreCase)
                    ? text
                    : $"Inspection: {text}";
            }

            if (fleet?.InspectionOverdue == true)
                return "Inspection: Attention Needed";

            if (fleet?.InspectionPassed == true)
                return "Inspection: OK";

            if (fleet?.InspectionPassed == false)
                return "Inspection: Failed";

            return "Inspection: --";
        }

        private static string BuildHealthText(FleetData? fleet)
        {
            if (fleet?.NeedsRepair == true || fleet?.InspectionOverdue == true || (fleet?.MilesUntilService != null && fleet.MilesUntilService <= 0))
                return "Needs Check";

            if (fleet?.MilesUntilService != null && fleet.MilesUntilService <= 500)
                return "Due Soon";

            if (fleet?.MilesUntilService != null || fleet?.InspectionPassed == true)
                return "Healthy";

            return "Unknown";
        }

        private static string BuildNotes(
            string driver,
            string role,
            string status,
            string truck,
            string location,
            int? safeScore,
            double weeklyMiles,
            double monthlyMiles,
            int loadsCompleted,
            int inspectionCount,
            string driverKey)
        {
            return
                $"Driver Key: {driverKey}\n" +
                $"Driver: {driver}\n" +
                $"Role: {role}\n" +
                $"Status: {status}\n" +
                $"Truck: {truck}\n" +
                $"Location: {location}\n" +
                $"Safe Score: {(safeScore.HasValue ? safeScore.Value.ToString(CultureInfo.InvariantCulture) : "--")}\n" +
                $"Weekly Miles: {weeklyMiles.ToString("N1", CultureInfo.InvariantCulture)}\n" +
                $"Monthly Miles: {monthlyMiles.ToString("N1", CultureInfo.InvariantCulture)}\n" +
                $"Loads Completed: {loadsCompleted.ToString("N0", CultureInfo.InvariantCulture)}\n" +
                $"Inspections: {inspectionCount.ToString("N0", CultureInfo.InvariantCulture)}\n\n" +
                "This profile is built from roster, dispatch history, inspections, and fleet data when available.";
        }

        private static string NormalizeDriverKey(string? driver, string? discordId)
        {
            var cleanDiscord = CleanId(discordId);
            if (!string.IsNullOrWhiteSpace(cleanDiscord))
                return $"discord:{cleanDiscord}";

            var cleanDriver = (driver ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(cleanDriver) &&
                !cleanDriver.Equals("-", StringComparison.OrdinalIgnoreCase) &&
                !cleanDriver.Equals("Unknown Driver", StringComparison.OrdinalIgnoreCase) &&
                !cleanDriver.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                return $"name:{cleanDriver.ToLowerInvariant()}";

            return "";
        }

        private static string CleanId(string? value)
        {
            var s = (value ?? "").Trim();

            if (string.IsNullOrWhiteSpace(s)) return "";
            if (s.Equals("-", StringComparison.OrdinalIgnoreCase)) return "";
            if (s.Equals("0", StringComparison.OrdinalIgnoreCase)) return "";
            if (s.Equals("unknown", StringComparison.OrdinalIgnoreCase)) return "";
            if (s.Equals("null", StringComparison.OrdinalIgnoreCase)) return "";

            return s;
        }

        private static string NormalizeTruckKey(string? truck)
        {
            return string.IsNullOrWhiteSpace(truck) ? "" : truck.Trim();
        }

        private static string SafeText(string? value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static string? FirstString(JsonElement el, params string[] names)
        {
            foreach (var name in names)
            {
                if (!el.TryGetProperty(name, out var p))
                    continue;

                if (p.ValueKind == JsonValueKind.String)
                    return p.GetString();

                if (p.ValueKind == JsonValueKind.Number || p.ValueKind == JsonValueKind.True || p.ValueKind == JsonValueKind.False)
                    return p.ToString();
            }

            return null;
        }

        private static int? FirstInt(JsonElement el, params string[] names)
        {
            foreach (var name in names)
            {
                if (!el.TryGetProperty(name, out var p))
                    continue;

                if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var i))
                    return i;

                if (p.ValueKind == JsonValueKind.String &&
                    int.TryParse(p.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                    return parsed;
            }

            return null;
        }

        private static bool? FirstBool(JsonElement el, params string[] names)
        {
            foreach (var name in names)
            {
                if (!el.TryGetProperty(name, out var p))
                    continue;

                if (p.ValueKind == JsonValueKind.True) return true;
                if (p.ValueKind == JsonValueKind.False) return false;

                if (p.ValueKind == JsonValueKind.String &&
                    bool.TryParse(p.GetString(), out var parsed))
                    return parsed;
            }

            return null;
        }

        private static DateTimeOffset ParseSortUtc(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return DateTimeOffset.MinValue;

            if (DateTimeOffset.TryParse(value, out var dt))
                return dt;

            return DateTimeOffset.MinValue;
        }

        private static string NormalizeTimeDisplay(string raw)
        {
            if (DateTimeOffset.TryParse(raw, out var dt))
                return dt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture);

            return raw.Trim();
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var v in values)
            {
                var s = (v ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(s))
                    return s;
            }

            return "";
        }

        private static string? TryReadString(object obj, string propertyName)
        {
            try
            {
                var p = obj.GetType().GetProperty(propertyName);
                var v = p?.GetValue(obj);
                return v?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static double TryReadDouble(object obj, string propertyName)
        {
            try
            {
                var p = obj.GetType().GetProperty(propertyName);
                var v = p?.GetValue(obj);

                if (v == null) return 0;
                if (v is double d) return d;
                if (v is float f) return f;
                if (v is decimal m) return (double)m;
                if (v is int i) return i;
                if (v is long l) return l;

                if (double.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                    return parsed;
            }
            catch
            {
            }

            return 0;
        }

        private static DateTime TryReadDateTime(object obj, string propertyName)
        {
            try
            {
                var p = obj.GetType().GetProperty(propertyName);
                var v = p?.GetValue(obj);

                if (v is DateTime dt)
                    return dt;

                if (v != null && DateTime.TryParse(v.ToString(), out var parsed))
                    return parsed;
            }
            catch
            {
            }

            return DateTime.MinValue;
        }

        private static bool StatusLooksOnline(string? status)
        {
            if (string.IsNullOrWhiteSpace(status)) return false;

            var s = status.Trim();
            return s.Equals("online", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("active", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("driving", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("on duty", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("onduty", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsIdleish(string? status)
        {
            if (string.IsNullOrWhiteSpace(status)) return false;

            var s = status.Trim();
            return s.Equals("idle", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("parked", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("break", StringComparison.OrdinalIgnoreCase);
        }

        private static Brush GetStatusBrush(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return Brushes.Gray;

            var s = status.Trim();

            if (StatusLooksOnline(s))
                return Brushes.LimeGreen;

            if (IsIdleish(s))
                return Brushes.Goldenrod;

            return Brushes.IndianRed;
        }

        private static Brush GetHealthBrush(string? health)
        {
            if (string.IsNullOrWhiteSpace(health))
                return Brushes.Gray;

            var s = health.Trim();

            if (s.Equals("Healthy", StringComparison.OrdinalIgnoreCase))
                return Brushes.ForestGreen;

            if (s.Equals("Due Soon", StringComparison.OrdinalIgnoreCase))
                return Brushes.Goldenrod;

            if (s.Equals("Needs Check", StringComparison.OrdinalIgnoreCase))
                return Brushes.IndianRed;

            return Brushes.Gray;
        }
    }
}
