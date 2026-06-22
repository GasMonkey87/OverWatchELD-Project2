using System;
using System.IO;
using System.Text.Json;
using OverWatchELD.Models;

namespace OverWatchELD.Services
{
    public static class VtcInfoStore
    {
        private static string Folder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ATS_ELD");

        private static string FilePath => Path.Combine(Folder, "vtc.json");

        public static VtcInfo Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new VtcInfo();

                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<VtcInfo>(json) ?? new VtcInfo();
            }
            catch
            {
                return new VtcInfo();
            }
        }

        public static void Save(VtcInfo info)
        {
            Directory.CreateDirectory(Folder);

            var json = JsonSerializer.Serialize(info, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(FilePath, json);
        }
    }
}
