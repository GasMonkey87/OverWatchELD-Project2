using System;
using System.IO;
using System.Text.Json;

namespace OverWatchELD.Overlay
{
    public sealed class OverlaySettings
    {
        public double Left { get; set; } = double.NaN;
        public double Top { get; set; } = double.NaN;
        public double Opacity { get; set; } = 0.92;
        public bool StartHidden { get; set; }
        public bool Locked { get; set; } = true;

        private static string SettingsDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OverWatchELD");

        private static string SettingsPath => Path.Combine(SettingsDirectory, "overlay-settings.json");

        public static OverlaySettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return new OverlaySettings();

                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<OverlaySettings>(json) ?? new OverlaySettings();
            }
            catch
            {
                return new OverlaySettings();
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(SettingsDirectory);
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // Overlay settings should never crash the ELD.
            }
        }

        public double ClampOpacity()
        {
            if (Opacity < 0.35) Opacity = 0.35;
            if (Opacity > 1.0) Opacity = 1.0;
            return Opacity;
        }
    }
}
