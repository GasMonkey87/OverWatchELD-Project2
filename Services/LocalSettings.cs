using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public static class LocalSettings
    {
        private static readonly string _file = AppPaths.FileInConfig("localsettings.json");

        private static Dictionary<string, string> _cache = Load();

        public static string? Get(string key)
            => _cache.TryGetValue(key, out var v) ? v : null;

        public static void Set(string key, string value)
        {
            _cache[key] = value ?? "";
            Save();
        }

        private static Dictionary<string, string> Load()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file)!);

                if (!File.Exists(_file))
                    TryMigrateLegacyFile();

                if (!File.Exists(_file))
                    return new Dictionary<string, string>();

                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_file))
                       ?? new Dictionary<string, string>();
            }
            catch
            {
                return new Dictionary<string, string>();
            }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
                File.WriteAllText(_file, JsonSerializer.Serialize(_cache, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
            }
            catch { }
        }

        private static void TryMigrateLegacyFile()
        {
            try
            {
                var candidates = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ATS_ELD", "settings.json"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OverWatchELD", "settings.json"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ATS_ELD", "settings.json"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OverWatchELD", "settings.json")
                };

                var oldFile = Array.Find(candidates, File.Exists);
                if (!string.IsNullOrWhiteSpace(oldFile))
                    File.Copy(oldFile, _file, overwrite: false);
            }
            catch { }
        }
    }
}