using System;
using System.IO;
using System.Text.Json;

namespace OverWatchELD.Services
{
    public static class VtcModeService
    {
        private static readonly object _gate = new();
        private static bool _loaded;
        private static bool _locked;

        private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ATS_ELD");
        private static string FilePath => Path.Combine(Folder, "vtc.mode.json");

        public static bool IsVtcLocked()
        {
            EnsureLoaded();
            return _locked;
        }

        public static void SetVtcLocked(bool locked)
        {
            lock (_gate)
            {
                _locked = locked;
                _loaded = true;
                try
                {
                    Directory.CreateDirectory(Folder);
                    var json = JsonSerializer.Serialize(new Persist { Locked = _locked }, new JsonSerializerOptions { WriteIndented = true });
                    System.IO.File.WriteAllText(FilePath, json);
                }
                catch { }
            }
        }

        private static void EnsureLoaded()
        {
            lock (_gate)
            {
                if (_loaded) return;
                _loaded = true;
                try
                {
                    if (!System.IO.File.Exists(FilePath)) return;
                    var json = System.IO.File.ReadAllText(FilePath);
                    var p = JsonSerializer.Deserialize<Persist>(json) ?? new Persist();
                    _locked = p.Locked;
                }
                catch { }
            }
        }

        private sealed class Persist
        {
            public bool Locked { get; set; }
        }
    }
}
