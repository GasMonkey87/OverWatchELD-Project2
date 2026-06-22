using OverWatchELD.Models;
using System;
using System.IO;
using System.Text.Json;

namespace OverWatchELD.Stores
{
    public static class VtcMaintenanceStore
    {
        private static readonly object LockObj = new();

        private static readonly string Folder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OverWatchELD");

        private static readonly string FilePath =
            Path.Combine(Folder, "vtc_maintenance.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public static VtcMaintenanceState Load()
        {
            lock (LockObj)
            {
                try
                {
                    Directory.CreateDirectory(Folder);

                    if (!File.Exists(FilePath))
                    {
                        var fresh = new VtcMaintenanceState();
                        Save(fresh);
                        return fresh;
                    }

                    var json = File.ReadAllText(FilePath);
                    return JsonSerializer.Deserialize<VtcMaintenanceState>(json, JsonOptions)
                           ?? new VtcMaintenanceState();
                }
                catch
                {
                    return new VtcMaintenanceState();
                }
            }
        }

        public static void Save(VtcMaintenanceState state)
        {
            lock (LockObj)
            {
                Directory.CreateDirectory(Folder);
                var json = JsonSerializer.Serialize(state, JsonOptions);
                File.WriteAllText(FilePath, json);
            }
        }
    }
}