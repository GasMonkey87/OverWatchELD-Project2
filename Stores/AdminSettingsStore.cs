using System;
using System.IO;
using System.Text.Json;

namespace OverWatchELD.Stores
{
    public sealed class AdminSettings
    {
        public bool AutoLockTruckOnInspectionDefect { get; set; } = true;
    }

    public static class AdminSettingsStore
    {
        private static readonly object Gate = new();

        private static string Folder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OverWatchELD");

        private static string FilePath =>
            Path.Combine(Folder, "AdminSettings.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public static AdminSettings Load()
        {
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(Folder);

                    if (!File.Exists(FilePath))
                    {
                        var fresh = new AdminSettings();
                        Save(fresh);
                        return fresh;
                    }

                    return JsonSerializer.Deserialize<AdminSettings>(
                        File.ReadAllText(FilePath),
                        JsonOptions) ?? new AdminSettings();
                }
                catch
                {
                    return new AdminSettings();
                }
            }
        }
        
        public static void Save(AdminSettings settings)
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOptions));
            }
        }
    }
}