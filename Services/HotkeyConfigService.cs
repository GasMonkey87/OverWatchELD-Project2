using System;
using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace OverWatchELD.Services
{
    public sealed class HotkeyConfig
    {
        // User-facing string, e.g. "Ctrl+Shift+E"
        public string toggle_eld { get; set; } = "Ctrl+Shift+E";
    }

    public readonly struct HotkeyBinding
    {
        public readonly ModifierKeys Modifiers;
        public readonly Key Key;

        public HotkeyBinding(ModifierKeys modifiers, Key key)
        {
            Modifiers = modifiers;
            Key = key;
        }

        public bool IsValid => Key != Key.None;

        public override string ToString()
        {
            if (!IsValid) return "None";
            string s = "";
            if (Modifiers.HasFlag(ModifierKeys.Control)) s += "Ctrl+";
            if (Modifiers.HasFlag(ModifierKeys.Shift)) s += "Shift+";
            if (Modifiers.HasFlag(ModifierKeys.Alt)) s += "Alt+";
            if (Modifiers.HasFlag(ModifierKeys.Windows)) s += "Win+";
            s += Key.ToString();
            return s;
        }
    }

    public static class HotkeyConfigService
    {
        private const string FileName = "hotkeys.json";

        // Primary: alongside the EXE (good for "mod folder" distribution)
        private static string GetPrimaryPath()
        {
            // AppContext.BaseDirectory = folder containing the running exe
            var dir = AppContext.BaseDirectory;
            return Path.Combine(dir, FileName);
        }

        // Fallback: AppData (if exe folder is read-only)
        private static string GetFallbackPath()
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(root, "GasMonkeyCreations", "ATS-ELD", FileName);
        }

        public static string GetConfigPath()
        {
            var primary = GetPrimaryPath();
            if (CanWriteToDirectory(Path.GetDirectoryName(primary)))
                return primary;

            return GetFallbackPath();
        }

        public static HotkeyBinding LoadToggleBindingOrDefault()
        {
            var cfg = LoadOrCreateDefault();
            if (TryParseHotkey(cfg.toggle_eld, out var binding))
                return binding;

            // Fallback if user typed something invalid
            TryParseHotkey("Ctrl+Shift+E", out var fallback);
            return fallback;
        }

        private static HotkeyConfig LoadOrCreateDefault()
        {
            var path = GetConfigPath();

            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                if (!File.Exists(path))
                {
                    var def = new HotkeyConfig();
                    File.WriteAllText(path, JsonSerializer.Serialize(def, new JsonSerializerOptions { WriteIndented = true }));
                    return def;
                }

                var json = File.ReadAllText(path);
                var cfg = JsonSerializer.Deserialize<HotkeyConfig>(json);
                return cfg ?? new HotkeyConfig();
            }
            catch
            {
                // If primary failed mid-way, try fallback once
                try
                {
                    var fallback = GetFallbackPath();
                    var dir2 = Path.GetDirectoryName(fallback);
                    if (!string.IsNullOrWhiteSpace(dir2))
                        Directory.CreateDirectory(dir2);

                    if (!File.Exists(fallback))
                    {
                        var def = new HotkeyConfig();
                        File.WriteAllText(fallback, JsonSerializer.Serialize(def, new JsonSerializerOptions { WriteIndented = true }));
                        return def;
                    }

                    var json = File.ReadAllText(fallback);
                    var cfg = JsonSerializer.Deserialize<HotkeyConfig>(json);
                    return cfg ?? new HotkeyConfig();
                }
                catch
                {
                    return new HotkeyConfig();
                }
            }
        }

        private static bool CanWriteToDirectory(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return false;

            try
            {
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // Try a tiny temp write/delete to prove write permission
                var test = Path.Combine(dir, ".write_test.tmp");
                File.WriteAllText(test, "x");
                File.Delete(test);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryParseHotkey(string? text, out HotkeyBinding binding)
        {
            binding = default;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            ModifierKeys mods = ModifierKeys.None;
            Key key = Key.None;

            var parts = text.Split(new[] { '+', ' ' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var raw in parts)
            {
                var t = raw.Trim();

                // Modifiers
                if (t.Equals("CTRL", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("CONTROL", StringComparison.OrdinalIgnoreCase))
                {
                    mods |= ModifierKeys.Control;
                    continue;
                }

                if (t.Equals("SHIFT", StringComparison.OrdinalIgnoreCase))
                {
                    mods |= ModifierKeys.Shift;
                    continue;
                }

                if (t.Equals("ALT", StringComparison.OrdinalIgnoreCase))
                {
                    mods |= ModifierKeys.Alt;
                    continue;
                }

                if (t.Equals("WIN", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("WINDOWS", StringComparison.OrdinalIgnoreCase))
                {
                    mods |= ModifierKeys.Windows;
                    continue;
                }

                // Key (last non-modifier token wins)
                try
                {
                    var kc = new KeyConverter();
                    var obj = kc.ConvertFromString(t);
                    if (obj is Key k && k != Key.None)
                        key = k;
                }
                catch
                {
                    // ignore invalid token
                }
            }

            if (key == Key.None)
                return false;

            binding = new HotkeyBinding(mods, key);
            return true;
        }
    }
}
