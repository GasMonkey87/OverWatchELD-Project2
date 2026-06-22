namespace OverWatchELD.Models
{
    public class LinkedDriverInfo
    {
        public bool IsLinked { get; set; }
        public string DiscordUsername { get; set; } = "No linked Discord";
        public string DiscordUserId { get; set; } = "";
        public string GuildId { get; set; } = "";
        public string VtcName { get; set; } = "";
        public string AssignedTruckNumber { get; set; } = "";
        public string AssignedTruckModel { get; set; } = "";

        public string LinkStatusText => IsLinked ? "Linked" : "Not Linked";
        public string TruckSummary =>
            string.IsNullOrWhiteSpace(AssignedTruckNumber) && string.IsNullOrWhiteSpace(AssignedTruckModel)
                ? "No truck assigned"
                : $"{AssignedTruckNumber} • {AssignedTruckModel}";
    }
}