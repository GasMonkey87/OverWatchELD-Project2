using System;
using System.IO;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public sealed class DiscordIdentityService
    {
        public sealed class DiscordIdentity
        {
            public string DiscordUserId { get; set; } = "";
            public string DiscordUsername { get; set; } = "";
            public string GuildId { get; set; } = "";
            public string VtcName { get; set; } = "";
            public string Role { get; internal set; }
        }

        private readonly string _path;
        private readonly string _legacyPath;

        public DiscordIdentityService()
        {
            _path = AppPaths.FileInConfig("discord_identity.json");
            _legacyPath = Path.Combine(AppContext.BaseDirectory, "discord_identity.json");
        }

        public DiscordIdentity LoadOrDefault()
        {
            try
            {
                TryMigrateLegacyFile();

                if (!File.Exists(_path))
                    return new DiscordIdentity();

                var json = File.ReadAllText(_path);
                var obj = JsonSerializer.Deserialize<DiscordIdentity>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return obj ?? new DiscordIdentity();
            }
            catch
            {
                return new DiscordIdentity();
            }
        }

        public void SaveOrUpdate(DiscordIdentity identity)
        {
            try
            {
                identity ??= new DiscordIdentity();
                Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? AppContext.BaseDirectory);

                var json = JsonSerializer.Serialize(identity, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(_path, json);
            }
            catch
            {
            }
        }

        private void TryMigrateLegacyFile()
        {
            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(_path))
                    return;

                if (!File.Exists(_legacyPath))
                    return;

                File.Copy(_legacyPath, _path, overwrite: false);
            }
            catch
            {
            }
        }

        internal static DiscordIdentity Load()
        {
            return new DiscordIdentityService().LoadOrDefault();
        }
    }
}