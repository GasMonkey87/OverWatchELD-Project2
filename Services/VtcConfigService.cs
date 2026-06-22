using System;
using System.IO;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public sealed class VtcConfig
    {
        public bool Enabled { get; set; }
        public string VtcName { get; set; } = "";
        public string VtcShort { get; set; } = "";
        public string GuildId { get; set; } = "";

        public string BotApiBaseUrl { get; set; } = "";
        public string BotApiKey { get; set; } = "";

        public string PairCode { get; set; } = "";
        public string DeviceToken { get; set; } = "";

        public string AnnouncementsLastSeenUtc { get; set; } = "";

        public string EventWebhookUrl { get; set; } = "";

        public LinkingConfig Linking { get; set; } = new LinkingConfig();

        public sealed class LinkingConfig
        {
            public string Code { get; set; } = "";
            public string DiscordUserId { get; set; } = "";
            public string DiscordUsername { get; set; } = "";

            public int CodeLength { get; set; } = 6;
            public int ExpiresMinutes { get; set; } = 10;
        }

        public DiscordConfig Discord { get; set; } = new DiscordConfig();

        public sealed class DiscordConfig
        {
            public string GuildId { get; set; } = "";

            public string DispatchChannelId { get; set; } = "";
            public string LogsChannelId { get; set; } = "";
            public string SystemLogChannelId { get; set; } = "";
            public string InspectionsChannelId { get; set; } = "";
            public string AnnouncementsChannelId { get; set; } = "";

            public string BolChannelId { get; set; } = "";
            public string MaintenanceChannelId { get; set; } = "";
            public string LeaderboardChannelId { get; set; } = "";

            public string LoadboardChannelId { get; set; } = "";
            public bool UseLoadThreads { get; set; } = true;
            public bool AutoArchiveCompletedLoads { get; set; } = true;

            public string DispatchWebhookUrl { get; set; } = "";
            public string LogsWebhookUrl { get; set; } = "";
            public string InspectionsWebhookUrl { get; set; } = "";
            public string SystemWebhookUrl { get; set; } = "";
            public string AnnouncementsWebhookUrl { get; set; } = "";
            public string BolWebhookUrl { get; set; } = "";
            public string MaintenanceWebhookUrl { get; set; } = "";
            public string LeaderboardWebhookUrl { get; set; } = "";
        }
    }

    public static class VtcConfigService
    {
        public static readonly string ConfigPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OverWatchELD",
                "vtc_config.json");

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public static VtcConfig Get() => Load();

        public static void EnsureCreated()
        {
            if (!File.Exists(ConfigPath))
                Save(CreateDefault());
        }

        public static VtcConfig Load()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                    return CreateDefault();

                var json = File.ReadAllText(ConfigPath);

                var cfg = JsonSerializer.Deserialize<VtcConfig>(json, JsonOpts)
                          ?? CreateDefault();

                Normalize(cfg);
                return cfg;
            }
            catch
            {
                return CreateDefault();
            }
        }

        public static VtcConfig Load(bool forceReload)
        {
            return Load();
        }

        public static VtcConfig LoadOrCreate()
        {
            EnsureCreated();
            return Load();
        }

        public static void Save(VtcConfig cfg)
        {
            try
            {
                cfg ??= CreateDefault();
                Normalize(cfg);

                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);

                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(cfg, JsonOpts));
            }
            catch
            {
            }
        }

        private static void Normalize(VtcConfig cfg)
        {
            cfg.Linking ??= new VtcConfig.LinkingConfig();
            cfg.Discord ??= new VtcConfig.DiscordConfig();

            if (string.IsNullOrWhiteSpace(cfg.Discord.GuildId) && !string.IsNullOrWhiteSpace(cfg.GuildId))
                cfg.Discord.GuildId = cfg.GuildId;

            if (string.IsNullOrWhiteSpace(cfg.GuildId) && !string.IsNullOrWhiteSpace(cfg.Discord.GuildId))
                cfg.GuildId = cfg.Discord.GuildId;
        }

        private static VtcConfig CreateDefault()
        {
            var cfg = new VtcConfig
            {
                Enabled = false,
                VtcName = "",
                VtcShort = "",
                GuildId = "",
                BotApiBaseUrl = "",
                BotApiKey = "",
                PairCode = "",
                DeviceToken = "",
                AnnouncementsLastSeenUtc = "",
                EventWebhookUrl = "",
                Linking = new VtcConfig.LinkingConfig(),
                Discord = new VtcConfig.DiscordConfig()
            };

            Normalize(cfg);
            return cfg;
        }
    }
}