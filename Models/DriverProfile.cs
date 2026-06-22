using System;

namespace OverWatchELD.Models
{
    public sealed class DriverProfile
    {
        public string ProfileId { get; set; } = Guid.NewGuid().ToString("N");

        // Stable unique key for this profile.
        // Prefer DiscordUserId. Fallback to normalized DriverId/DisplayName.
        public string DriverId { get; set; } = "";

        public string DiscordUserId { get; set; } = "";
        public string DiscordUsername { get; set; } = "";
        public string DisplayName { get; set; } = "New Driver";

        public string Role { get; set; } = "";
        public string Status { get; set; } = "";
        public string TruckNumber { get; set; } = "";
        public string TruckName { get; set; } = "";
        public string PlateNumber { get; set; } = "";
        public string Location { get; set; } = "";

        public double WeeklyMiles { get; set; }
        public double MonthlyMiles { get; set; }
        public int LoadsCompleted { get; set; }
        public int InspectionCount { get; set; }
        public int? SafeScore { get; set; }

        public string Notes { get; set; } = "";

        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

        public static string BuildStableDriverId(string? discordUserId, string? driverName, string? discordUsername = null)
        {
            var id = (discordUserId ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(id) && !id.Equals("-", StringComparison.OrdinalIgnoreCase))
                return "discord:" + id;

            var username = (discordUsername ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(username) && !username.Equals("-", StringComparison.OrdinalIgnoreCase))
                return "name:" + username.ToLowerInvariant();

            var name = (driverName ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(name) && !name.Equals("-", StringComparison.OrdinalIgnoreCase))
                return "name:" + name.ToLowerInvariant();

            return "";
        }
    }
}
