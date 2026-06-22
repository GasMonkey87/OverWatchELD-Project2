namespace OverWatchELD.Models
{
    public class DiscordWebhookSettings
    {
        public string SubmissionWebhookUrl { get; set; } = "";
        public string FleetWebhookUrl { get; set; } = "";
        public string FinanceWebhookUrl { get; set; } = "";
        public string DriverManagementWebhookUrl { get; set; } = "";
        public string DispatchWebhookUrl { get; set; } = "";
        public string DiscordGuildId { get; set; } = "";
        public string DiscordBotName { get; set; } = "";
        public string DispatchChannelId { get; set; } = "";
        public string Notes { get; set; } = "";
    }
}
