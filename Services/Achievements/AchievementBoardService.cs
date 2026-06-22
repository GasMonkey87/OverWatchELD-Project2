using OverWatchELD.Models.Achievements;
using OverWatchELD.Services.Analytics;
using OverWatchELD.Services.Fleet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using OverWatchELD.Services;

namespace OverWatchELD.Services.Achievements
{
    public static class AchievementBoardService
    {
        private static readonly object Gate = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private static string StorePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "OverWatchELD",
                "achievements.json");

        public static List<AchievementRecord> BuildBoard()
        {
            lock (Gate)
            {
                var previous = LoadUnlockedMap();
                var rows = BuildCurrentRows(previous);

                foreach (var custom in LoadAllStored()
                             .Where(x => x.IsCustom || string.Equals(x.Category, "Custom", StringComparison.OrdinalIgnoreCase)))
                {
                    if (!rows.Any(x => string.Equals(x.Id, custom.Id, StringComparison.OrdinalIgnoreCase)))
                        rows.Add(Normalize(custom));
                }

                rows = rows
                    .OrderByDescending(x => x.IsUnlocked)
                    .ThenBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.DriverName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                Save(rows);
                return rows;
            }
        }

        public static List<AchievementRecord> LoadAll() => BuildBoard();

        public static List<AchievementRecord> GetAwardsForDriver(string? driverName)
        {
            var key = (driverName ?? "").Trim();

            return BuildBoard()
                .Where(x => x.IsUnlocked)
                .Where(x =>
                    string.IsNullOrWhiteSpace(key) ||
                    string.IsNullOrWhiteSpace(x.DriverName) ||
                    string.Equals(x.DriverName.Trim(), key, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.UnlockedUtc ?? x.CreatedUtc)
                .ToList();
        }

        public static List<string> GetKnownDrivers()
        {
            return BuildBoard()
                .Select(x => (x.DriverName ?? "").Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static AchievementRecord AddCustomAchievement(
            string title,
            string description,
            string icon,
            string awardedTo)
        {
            return AddCustomAchievement(title, description, icon, awardedTo, "Custom", "Common", EldDriverIdentityResolver.DriverName());
        }

        public static AchievementRecord AddCustomAchievement(
            string title,
            string description,
            string icon,
            string awardedTo,
            string category,
            string rarity,
            string createdBy)
        {
            lock (Gate)
            {
                var rows = LoadAllStored();

                var record = Normalize(new AchievementRecord
                {
                    Id = "custom-" + Guid.NewGuid().ToString("N"),
                    Title = Safe(title, "Custom Achievement"),
                    Description = Safe(description, ""),
                    Icon = string.IsNullOrWhiteSpace(icon) ? "🏆" : icon.Trim(),
                    DriverName = Safe(awardedTo, ""),
                    Category = Safe(category, "Custom"),
                    Rarity = Safe(rarity, "Common"),
                    IsCustom = true,
                    IsUnlocked = true,
                    Progress = 1,
                    Target = 1,
                    ProgressText = "Complete",
                    RewardText = "Custom award",
                    CreatedBy = Safe(createdBy, EldDriverIdentityResolver.DriverName()),
                    CreatedUtc = DateTime.UtcNow,
                    UpdatedUtc = DateTime.UtcNow,
                    UnlockedUtc = DateTime.UtcNow
                });

                rows.Add(record);
                Save(rows);
                return record;
            }
        }

        public static bool UpdateCustomAchievement(
            string id,
            string title,
            string description,
            string icon,
            string awardedTo,
            string category,
            string rarity)
        {
            lock (Gate)
            {
                var rows = LoadAllStored();
                var item = rows.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

                if (item == null || !item.IsCustom)
                    return false;

                item.Title = Safe(title, item.Title);
                item.Description = Safe(description, item.Description);
                item.Icon = string.IsNullOrWhiteSpace(icon) ? item.Icon : icon.Trim();
                item.DriverName = Safe(awardedTo, item.DriverName);
                item.Category = Safe(category, item.Category);
                item.Rarity = Safe(rarity, item.Rarity);
                item.UpdatedUtc = DateTime.UtcNow;
                item.IsUnlocked = true;
                item.UnlockedUtc ??= DateTime.UtcNow;

                Save(rows.Select(Normalize).ToList());
                return true;
            }
        }

        public static bool DeleteCustomAchievement(string id)
        {
            lock (Gate)
            {
                var rows = LoadAllStored();

                var removed = rows.RemoveAll(x =>
                    x.IsCustom &&
                    string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

                if (removed <= 0)
                    return false;

                Save(rows);
                return true;
            }
        }

        public static AchievementRecord AwardAchievementToDriver(string sourceAchievementId, string driverName)
        {
            lock (Gate)
            {
                var rows = BuildBoard();
                var source = rows.FirstOrDefault(x => string.Equals(x.Id, sourceAchievementId, StringComparison.OrdinalIgnoreCase));

                if (source == null)
                    throw new InvalidOperationException("Select an achievement first.");

                var driver = Safe(driverName, "");
                if (string.IsNullOrWhiteSpace(driver))
                    throw new InvalidOperationException("Enter a driver name before awarding.");

                var award = Normalize(new AchievementRecord
                {
                    Id = "manual-" + source.Id + "-" + Guid.NewGuid().ToString("N"),
                    Category = source.Category,
                    Title = source.Title,
                    Description = source.Description,
                    Icon = source.Icon,
                    Rarity = source.Rarity,
                    IsCustom = true,
                    IsUnlocked = true,
                    UnlockedUtc = DateTime.UtcNow,
                    CreatedUtc = DateTime.UtcNow,
                    UpdatedUtc = DateTime.UtcNow,
                    DriverName = driver,
                    Progress = 1,
                    Target = 1,
                    ProgressText = "Complete",
                    RewardText = "Manual award"
                });

                var stored = LoadAllStored();
                stored.Add(award);
                Save(stored);
                return award;
            }
        }

        public static bool RemoveAwardFromDriver(string achievementId)
        {
            lock (Gate)
            {
                var rows = LoadAllStored();

                var removed = rows.RemoveAll(x =>
                    string.Equals(x.Id, achievementId, StringComparison.OrdinalIgnoreCase) &&
                    (x.IsCustom || x.Id.StartsWith("manual-", StringComparison.OrdinalIgnoreCase)));

                if (removed <= 0)
                    return false;

                Save(rows);
                return true;
            }
        }

        public static void ResetUnlockedForTesting()
        {
            lock (Gate)
            {
                try
                {
                    if (File.Exists(StorePath))
                        File.Delete(StorePath);
                }
                catch
                {
                }
            }
        }

        private static Dictionary<string, AchievementRecord> LoadUnlockedMap()
        {
            try
            {
                return LoadAllStored()
                    .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                    .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => Normalize(g.First()), StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, AchievementRecord>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static List<AchievementRecord> LoadAllStored()
        {
            try
            {
                if (!File.Exists(StorePath))
                    return new List<AchievementRecord>();

                var json = File.ReadAllText(StorePath);
                return JsonSerializer.Deserialize<List<AchievementRecord>>(json, JsonOptions)?
                           .Select(Normalize)
                           .ToList()
                       ?? new List<AchievementRecord>();
            }
            catch
            {
                return new List<AchievementRecord>();
            }
        }

        private static void Save(List<AchievementRecord> rows)
        {
            try
            {
                var dir = Path.GetDirectoryName(StorePath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(StorePath, JsonSerializer.Serialize(rows.Select(Normalize).ToList(), JsonOptions));
            }
            catch
            {
            }
        }

        private static List<AchievementRecord> BuildCurrentRows(Dictionary<string, AchievementRecord> previous)
        {
            var rows = new List<AchievementRecord>();

            var snapshot = Safe(() => FleetAnalyticsService.BuildSnapshot(), null);
            var fleetRows = Safe(() => FleetTruckApprovalService.BuildFleetTruckRows(), new());

            var totalLoads = snapshot?.TotalLoadsDelivered ?? 0;
            var totalMiles = snapshot?.TotalMiles ?? 0;
            var netProfit = snapshot?.NetProfit ?? 0;
            var weekProfit = snapshot?.WeekProfit ?? 0;
            var driverScore = snapshot?.AverageDriverScore ?? 0;
            var trucksTracked = fleetRows.Count;
            var activeTrucks = fleetRows.Count(x => (x.ActiveLight ?? "").Contains("🟢", StringComparison.OrdinalIgnoreCase));
            var approvedTrucks = fleetRows.Count(x => !(x.ApprovalBadge ?? "").Equals("Pending", StringComparison.OrdinalIgnoreCase));
            var pendingTrucks = fleetRows.Count(x => (x.ApprovalBadge ?? "").Equals("Pending", StringComparison.OrdinalIgnoreCase));

            Add(rows, previous, "first-load", "Dispatch", "First Load Delivered", "Deliver your first tracked load.", "📦", totalLoads, 1, "Unlocks when the first load is completed.", "Common");
            Add(rows, previous, "loads-10", "Dispatch", "Reliable Hauler", "Deliver 10 tracked loads.", "📦", totalLoads, 10, "Dispatch badge", "Common");
            Add(rows, previous, "loads-50", "Dispatch", "Loadboard Veteran", "Deliver 50 tracked loads.", "🚚", totalLoads, 50, "Veteran dispatch badge", "Rare");
            Add(rows, previous, "loads-100", "Dispatch", "Century Carrier", "Deliver 100 tracked loads.", "🏆", totalLoads, 100, "Company milestone", "Epic");

            Add(rows, previous, "miles-1000", "Driver", "1,000 Mile Club", "Track 1,000 total fleet miles.", "🛣", totalMiles, 1000, "Mileage badge", "Common");
            Add(rows, previous, "miles-10000", "Driver", "10,000 Mile Club", "Track 10,000 total fleet miles.", "🛣", totalMiles, 10000, "Mileage badge", "Rare");
            Add(rows, previous, "miles-50000", "Driver", "Long Haul Legend", "Track 50,000 total fleet miles.", "🌟", totalMiles, 50000, "Legend badge", "Legendary");

            Add(rows, previous, "profit-positive", "Economy", "In The Black", "Reach positive lifetime net profit.", "💰", netProfit > 0 ? 1 : 0, 1, "Economy badge", "Common");
            Add(rows, previous, "profit-100k", "Economy", "$100K Company", "Reach $100,000 lifetime net profit.", "💰", (double)netProfit, 100000, "Company economy badge", "Rare");
            Add(rows, previous, "profit-1m", "Economy", "Million Dollar Company", "Reach $1,000,000 lifetime net profit.", "💎", (double)netProfit, 1000000, "Elite company badge", "Legendary");
            Add(rows, previous, "week-positive", "Economy", "Clean Week", "Finish the current week with positive profit.", "📈", weekProfit > 0 ? 1 : 0, 1, "Weekly badge", "Common");

            Add(rows, previous, "score-80", "Safety", "Safe Fleet", "Reach an average driver score of 80+.", "🛡", driverScore, 80, "Safety badge", "Rare");
            Add(rows, previous, "score-90", "Safety", "Elite Safety Fleet", "Reach an average driver score of 90+.", "🛡", driverScore, 90, "Elite safety badge", "Epic");

            Add(rows, previous, "first-truck", "Fleet", "First Fleet Truck", "Register and approve your first fleet truck.", "🚛", approvedTrucks, 1, "Fleet badge", "Common");
            Add(rows, previous, "fleet-5", "Fleet", "Small Fleet", "Approve 5 fleet trucks.", "🚛", approvedTrucks, 5, "Fleet badge", "Rare");
            Add(rows, previous, "fleet-10", "Fleet", "Growing Fleet", "Approve 10 fleet trucks.", "🚛", approvedTrucks, 10, "Fleet badge", "Epic");
            Add(rows, previous, "active-truck", "Fleet", "Truck Online", "Have at least one fleet truck active on telemetry.", "🟢", activeTrucks, 1, "Operations badge", "Common");
            Add(rows, previous, "pending-reviewed", "Fleet", "Approval Pipeline", "Have at least one truck submission pending review.", "🟡", pendingTrucks, 1, "Management badge", "Common");
            Add(rows, previous, "fleet-tracked", "Fleet", "Fleet Tracker Online", "Have at least one truck visible on the Fleet Trucks board.", "📡", trucksTracked, 1, "Tracker badge", "Common");

            return rows;
        }

        private static void Add(
            List<AchievementRecord> rows,
            Dictionary<string, AchievementRecord> previous,
            string id,
            string category,
            string title,
            string description,
            string icon,
            double current,
            double target,
            string reward,
            string rarity)
        {
            var unlocked = target <= 0 || current >= target;
            previous.TryGetValue(id, out var old);

            var wasUnlocked = old?.IsUnlocked == true;
            var unlockedUtc = wasUnlocked
                ? old!.UnlockedUtc
                : unlocked
                    ? DateTime.UtcNow
                    : null;

            var progress = target <= 0 ? 1 : Math.Max(0, Math.Min(1, current / target));

            rows.Add(Normalize(new AchievementRecord
            {
                Id = id,
                Category = category,
                Title = title,
                Description = description,
                Icon = icon,
                IsUnlocked = wasUnlocked || unlocked,
                UnlockedUtc = unlockedUtc,
                Progress = progress,
                Target = target,
                ProgressText = FormatProgress(current, target),
                RewardText = reward,
                Rarity = rarity,
                IsCustom = false,
                CreatedUtc = old?.CreatedUtc == default ? DateTime.UtcNow : old?.CreatedUtc ?? DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            }));
        }

        private static AchievementRecord Normalize(AchievementRecord x)
        {
            x.Id ??= "";
            x.Category = Safe(x.Category, x.IsCustom ? "Custom" : "General");
            x.Title = Safe(x.Title, "Achievement");
            x.Description ??= "";
            x.Icon = string.IsNullOrWhiteSpace(x.Icon) ? "🏆" : x.Icon.Trim();
            x.Rarity = Safe(x.Rarity, "Common");
            x.ProgressText ??= "";
            x.RewardText ??= "";
            x.DriverName ??= "";
            x.CreatedBy ??= "";
            if (x.CreatedUtc == default) x.CreatedUtc = DateTime.UtcNow;
            if (x.UpdatedUtc == default) x.UpdatedUtc = DateTime.UtcNow;
            if (x.IsUnlocked && x.UnlockedUtc == null) x.UnlockedUtc = DateTime.UtcNow;
            return x;
        }

        private static string FormatProgress(double current, double target)
        {
            if (target <= 1)
                return current >= target ? "Complete" : "Not yet";

            return $"{current:N0} / {target:N0}";
        }

        private static string Safe(string? value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static T Safe<T>(Func<T> work, T fallback)
        {
            try { return work(); }
            catch { return fallback; }
        }
    }
}
