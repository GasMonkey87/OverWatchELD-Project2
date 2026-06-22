using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public sealed class VtcDriverActivityFeedService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private static string DataFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OverWatchELD");

        private static string ActivityFile =>
            Path.Combine(DataFolder, "vtc_driver_activity_feed.json");

        public List<VtcDriverActivityItem> BuildActivityFeed()
        {
            Directory.CreateDirectory(DataFolder);

            var previous = LoadPrevious();
            var current = BuildCurrentSnapshots();
            var feed = new List<VtcDriverActivityItem>();

            foreach (var snap in current)
            {
                previous.TryGetValue(snap.DriverName, out var old);

                if (old == null)
                {
                    feed.Add(new VtcDriverActivityItem
                    {
                        DriverName = snap.DriverName,
                        Message = $"{snap.DriverName} activity started • {snap.MilesDriven:N0} miles • {snap.LoadsPickedUp} picked up • {snap.LoadsDelivered} delivered",
                        TimeDisplay = "New driver history snapshot",
                        EventUtc = DateTime.UtcNow,
                        Kind = "Snapshot"
                    });

                    continue;
                }

                var milesDiff = Math.Max(0, snap.MilesDriven - old.MilesDriven);
                var pickedDiff = Math.Max(0, snap.LoadsPickedUp - old.LoadsPickedUp);
                var deliveredDiff = Math.Max(0, snap.LoadsDelivered - old.LoadsDelivered);

                if (milesDiff > 0 || pickedDiff > 0 || deliveredDiff > 0)
                {
                    var parts = new List<string>();

                    if (milesDiff > 0)
                        parts.Add($"{milesDiff:N0} miles driven");

                    if (pickedDiff > 0)
                        parts.Add($"{pickedDiff} load{Plural(pickedDiff)} picked up");

                    if (deliveredDiff > 0)
                        parts.Add($"{deliveredDiff} load{Plural(deliveredDiff)} delivered");

                    feed.Add(new VtcDriverActivityItem
                    {
                        DriverName = snap.DriverName,
                        Message = $"{snap.DriverName} • {string.Join(" • ", parts)}",
                        TimeDisplay = "Periodic driver history update",
                        EventUtc = DateTime.UtcNow,
                        Kind = "Progress"
                    });
                }
                else
                {
                    feed.Add(new VtcDriverActivityItem
                    {
                        DriverName = snap.DriverName,
                        Message = $"{snap.DriverName} • {snap.MilesDriven:N0} total miles • {snap.LoadsPickedUp} picked up • {snap.LoadsDelivered} delivered",
                        TimeDisplay = "Driver history snapshot",
                        EventUtc = DateTime.UtcNow,
                        Kind = "Snapshot"
                    });
                }
            }

            SaveCurrent(current);

            return feed
                .OrderByDescending(x => x.EventUtc)
                .ThenBy(x => x.DriverName)
                .Take(20)
                .ToList();
        }

        private static Dictionary<string, VtcDriverActivitySnapshot> LoadPrevious()
        {
            try
            {
                if (!File.Exists(ActivityFile))
                    return new Dictionary<string, VtcDriverActivitySnapshot>(StringComparer.OrdinalIgnoreCase);

                var json = File.ReadAllText(ActivityFile);
                var rows = JsonSerializer.Deserialize<List<VtcDriverActivitySnapshot>>(json, JsonOptions)
                           ?? new List<VtcDriverActivitySnapshot>();

                return rows
                    .Where(x => !string.IsNullOrWhiteSpace(x.DriverName))
                    .GroupBy(x => x.DriverName.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.LastUpdatedUtc).First(), StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, VtcDriverActivitySnapshot>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static List<VtcDriverActivitySnapshot> BuildCurrentSnapshots()
        {
            var jobs = DispatchService.Jobs.ToList();

            return jobs
                .SelectMany(j => new[]
                {
                    j.AssignedDriver,
                    j.ClaimedBy
                })
                .Where(x => !string.IsNullOrWhiteSpace(x) &&
                            !x.Equals("Unassigned", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(driver =>
                {
                    var mine = jobs.Where(j =>
                        Same(j.AssignedDriver, driver) ||
                        Same(j.ClaimedBy, driver)).ToList();

                    return new VtcDriverActivitySnapshot
                    {
                        DriverName = driver.Trim(),
                        MilesDriven = mine
                            .Where(j => j.Status.Equals("Delivered", StringComparison.OrdinalIgnoreCase))
                            .Sum(j => j.ActualDrivenMiles > 0 ? j.ActualDrivenMiles : j.Miles),

                        LoadsPickedUp = mine.Count(j =>
                            j.PickedUpUtc.HasValue ||
                            j.Status.Equals("Picked Up", StringComparison.OrdinalIgnoreCase) ||
                            j.Status.Equals("In Transit", StringComparison.OrdinalIgnoreCase) ||
                            j.Status.Equals("Delivered", StringComparison.OrdinalIgnoreCase)),

                        LoadsDelivered = mine.Count(j =>
                            j.DeliveredUtc.HasValue ||
                            j.Status.Equals("Delivered", StringComparison.OrdinalIgnoreCase)),

                        ActiveLoads = mine.Count(j =>
                            !j.Status.Equals("Delivered", StringComparison.OrdinalIgnoreCase) &&
                            !j.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)),

                        LastUpdatedUtc = DateTime.UtcNow
                    };
                })
                .OrderBy(x => x.DriverName)
                .ToList();
        }

        private static void SaveCurrent(List<VtcDriverActivitySnapshot> rows)
        {
            try
            {
                Directory.CreateDirectory(DataFolder);
                File.WriteAllText(ActivityFile, JsonSerializer.Serialize(rows, JsonOptions));
            }
            catch
            {
            }
        }

        private static bool Same(string? a, string? b)
        {
            return !string.IsNullOrWhiteSpace(a) &&
                   !string.IsNullOrWhiteSpace(b) &&
                   string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static string Plural(double count) => Math.Abs(count - 1) < 0.001 ? "" : "s";
    }

    public sealed class VtcDriverActivitySnapshot
    {
        public string DriverName { get; set; } = "";
        public double MilesDriven { get; set; }
        public int LoadsPickedUp { get; set; }
        public int LoadsDelivered { get; set; }
        public int ActiveLoads { get; set; }
        public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
    }

    public sealed class VtcDriverActivityItem
    {
        public string DriverName { get; set; } = "";
        public string Message { get; set; } = "";
        public string TimeDisplay { get; set; } = "";
        public string Kind { get; set; } = "";
        public DateTime EventUtc { get; set; } = DateTime.UtcNow;
    }
}