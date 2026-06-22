using System;
using System.IO;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public sealed class SessionPersistenceService
    {
        private static readonly string Folder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ATS_ELD");

        private static readonly string FilePath =
            Path.Combine(Folder, "session.json");

        public sealed class PersistedState
        {
            public string DriverName { get; set; } = "Driver";
            public string VtcProvider { get; set; } = "None";

            // Duty/session
            public string LastDutyStatus { get; set; } = "OFF"; // OFF/ON/SB/DR
            public DateTimeOffset LastDutyChangeUtc { get; set; } = DateTimeOffset.UtcNow;

            // Optional: last viewed day in logs
            public DateTime LastLogDateLocal { get; set; } = DateTime.Today;
        }

        public PersistedState LoadOrDefault()
        {
            try
            {
                if (!System.IO.File.Exists(FilePath))
                    return new PersistedState();

                var json = System.IO.File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<PersistedState>(json) ?? new PersistedState();
            }
            catch
            {
                return new PersistedState();
            }
        }

        public void Save(PersistedState state)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(FilePath, json);
            }
            catch
            {
                // never crash app for persistence
            }
        }
    }
}
