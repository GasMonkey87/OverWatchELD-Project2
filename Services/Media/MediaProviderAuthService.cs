using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace OverWatchELD.Services.Media
{
    public sealed class MediaProviderAuthConfig
    {
        public string SpotifyClientId { get; set; } = "";
        public string SpotifyRedirectUri { get; set; } = "http://127.0.0.1:5234/media/spotify/callback";
        public string AppleMusicTeamId { get; set; } = "";
        public string AppleMusicKeyId { get; set; } = "";
        public string AppleMusicDeveloperToken { get; set; } = "";
        public string LastLocalMusicFolder { get; set; } = "";
    }

    public static class MediaProviderAuthService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public static string ConfigPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "OverWatchELD",
                "media_provider_config.json");

        public static MediaProviderAuthConfig Load()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                    return new MediaProviderAuthConfig();

                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<MediaProviderAuthConfig>(json, JsonOptions)
                    ?? new MediaProviderAuthConfig();
            }
            catch
            {
                return new MediaProviderAuthConfig();
            }
        }

        public static void Save(MediaProviderAuthConfig config)
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, JsonOptions));
            }
            catch
            {
            }
        }

        public static void OpenSpotifyLoginOrPlayer()
        {
            var cfg = Load();

            // Spotify playback inside a desktop app requires a Spotify developer app,
            // OAuth PKCE, and Spotify playback rules. Until the user configures a client id,
            // open Spotify's player so they can log in and play playlists immediately.
            if (string.IsNullOrWhiteSpace(cfg.SpotifyClientId))
            {
                OpenUrl("https://open.spotify.com/");
                return;
            }

            var scope = Uri.EscapeDataString("user-read-private user-read-email playlist-read-private streaming user-read-playback-state user-modify-playback-state");
            var redirect = Uri.EscapeDataString(cfg.SpotifyRedirectUri);
            var clientId = Uri.EscapeDataString(cfg.SpotifyClientId.Trim());

            var url =
                "https://accounts.spotify.com/authorize" +
                $"?client_id={clientId}" +
                "&response_type=code" +
                $"&redirect_uri={redirect}" +
                $"&scope={scope}";

            OpenUrl(url);
        }

        public static void OpenAppleMusicLoginOrPlayer()
        {
            // Apple Music playback requires MusicKit / Apple developer credentials.
            // This opens the web player immediately while keeping config fields ready
            // for a future MusicKit token-backed integration.
            OpenUrl("https://music.apple.com/");
        }

        public static void OpenAtsMusicFolder()
        {
            var folder = LocalMusicLibraryService.AtsMusicFolder;
            Directory.CreateDirectory(folder);
            OpenPath(folder);
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
            }
        }

        private static void OpenPath(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch
            {
            }
        }
    }
}
