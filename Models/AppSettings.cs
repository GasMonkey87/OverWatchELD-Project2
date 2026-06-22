using System;

namespace OverWatchELD.Models
{
    public sealed class DiscordSettings
    {
        public string ExportWebhookUrl { get; set; } = "";
        public string LogsWebhookUrl { get; set; } = "";
        public string InspectionsWebhookUrl { get; set; } = "";
        public string VtcSyncWebhookUrl { get; set; } = "";
        public string AnnouncementsWebhookUrl { get; set; } = "";
    }

    public sealed class AppSettings
    {
        public string TimeSourceMode { get; set; } = "RealTime";
        public string DiscordWebhookUrl { get; set; } = "";
        public DiscordSettings Discord { get; set; } = new DiscordSettings();

        public bool FirstRunCompleted { get; set; }
        public string FirstRunCompletedUtc { get; set; } = "";

        public string DriverName { get; set; } = "Driver";
        public string CompanyName { get; set; } = "";
        public string TruckNumber { get; set; } = "";
        public string HomeTerminal { get; set; } = "";

        public bool UseVtcMode { get; set; }
        public bool TelemetryEnabled { get; set; } = true;
        public bool ShowTelemetryReminder { get; set; } = true;
        public bool AllowConnectSkipDuringSetup { get; set; } = true;

        public bool RequirePreTripInspection { get; set; } = true;
        public bool RequirePostTripInspection { get; set; } = true;
        public bool RequireCertificationReminders { get; set; } = true;

        public string HosRuleSet { get; set; } = "US 70hr / 8 day";
        public bool OpenLastPageOnLaunch { get; set; }
        public bool PreferMinimizeToTray { get; set; }
    }
}