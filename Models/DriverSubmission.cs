using System;

namespace OverWatchELD.Models
{
    public class DriverSubmission
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime DateUtc { get; set; } = DateTime.UtcNow;
        public string SubmissionType { get; set; } = "";
        public string Title { get; set; } = "";
        public decimal? Amount { get; set; }
        public string TruckName { get; set; } = "";
        public string DiscordUserId { get; set; } = "";
        public string DiscordUsername { get; set; } = "";
        public string Details { get; set; } = "";
        public bool IsApproved { get; set; }
    }
}
